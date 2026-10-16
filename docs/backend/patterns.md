# Patrones del backend

Cada patrón con el problema que resuelve, dónde vive y un ejemplo tomado del código. Las rutas son relativas a `finanzas-hub-api/src/`.

## 1. Monolito modular

**Problema.** Doce áreas de negocio que deben evolucionar sin enredarse, mantenidas por una persona y para unos 10 usuarios: microservicios serían un coste sin beneficio.

**Solución.** Un proceso, una base y una solución, con un proyecto por módulo (`Modules/<X>/PersonalFinance.Modules.<X>`). Las fronteras no las impone el compilador (los tipos son públicos) sino `ModuleBoundaryTests` y `LayeringTests`. Ver [ADR-001](../adr/ADR-001-monolito-modular.md) y [module-communication.md](module-communication.md).

## 2. CQRS ligero: un caso de uso, una clase

**Problema.** Los servicios del tipo `BudgetService` con veinte métodos acaban mezclando reglas, consultas y permisos.

**Solución.** Cada caso de uso es un `record` (comando o consulta) más su handler. Las interfaces están en `BuildingBlocks/PersonalFinance.BuildingBlocks.Application/Messaging/Messaging.cs`:

```csharp
public interface ICommand<TResponse>;                 // cambia estado; corre en una transacción
public interface IQuery<TResponse>;                   // solo lee

public interface ICommandHandler<in TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
```

Ejemplo real: `CreateBudgetCommand` y `CreateBudgetHandler` en `Modules/Budgets/.../Application/Budgets.cs`. No hay mediador: el controller recibe el handler por inyección (`[FromServices] ICommandHandler<CreateBudgetCommand, BudgetResponse> handler`). Así «ir a la definición» lleva directo al código que se ejecuta.

Las consultas no pasan por la transacción ni por la idempotencia; leen con `AsNoTracking`.

## 3. Decorator: lo transversal fuera del handler

**Problema.** Abrir transacción, guardar, despachar eventos, registrar trazas y aplicar idempotencia en cada handler sería repetirlo 100 veces y olvidarlo alguna.

**Solución.** `Infrastructure/Messaging/HandlerRegistration.cs` envuelve cada handler de comando, de fuera hacia dentro:

```text
LoggingCommandHandlerDecorator        span "Módulo.CasoDeUso", log, métrica de duración
 └ TransactionCommandHandlerDecorator  BEGIN … SaveChanges + eventos … COMMIT (o ROLLBACK)
    └ IdempotencyCommandHandlerDecorator   solo si el comando implementa IIdempotentCommand
       └ tu handler
```

Código en `Infrastructure/Messaging/HandlerDecorators.cs` (transacción en la línea 89, idempotencia en la 156). Consecuencia: **un handler nunca llama a `SaveChanges`**. Si lo hace con eventos pendientes, `AppDbContext.EnsureDomainEventsWereDispatched` lanza una excepción.

## 4. Result en lugar de excepciones

**Problema.** Las excepciones para reglas de negocio («el presupuesto ya existe») son caras, invisibles en la firma y fáciles de convertir en un 500.

**Solución.** `Result<T>` y `Error` en `BuildingBlocks/PersonalFinance.BuildingBlocks.Domain/`. `Error` tiene un `Type` que decide el código HTTP y un `Code` estable que el frontend traduce:

```csharp
if (await db.Budgets.AnyAsync(..., cancellationToken))
{
    return Error.Conflict("budgets.already_exists", "That category already has a budget.");   // 409
}
```

| `ErrorType` | HTTP |
|---|---|
| `Validation` | 400 (con `FieldErrors` por campo) |
| `Unauthorized` | 401 |
| `Forbidden` | 403 |
| `NotFound` | 404 |
| `Conflict` | 409 |
| `BusinessRule` | 422 |

`Error` se convierte implícitamente en `Result`, así que `return error;` basta. Las excepciones quedan para lo inesperado (base caída, bug), que el manejador global devuelve como 500 sin detalles.

## 5. Modelo de dominio rico y agregados

**Problema.** Entidades con setters públicos dejan que cualquiera las ponga en un estado imposible (un presupuesto con monto negativo).

**Solución.** Constructores privados, setters privados y fábricas o métodos que validan y devuelven `Result`. Ejemplo: `Modules/Budgets/.../Domain/Budget.cs`:

```csharp
public static Result<Budget> Create(Guid workspaceId, Guid ownerUserId, Guid categoryId, decimal amount, ...)
{
    if (Check(amount, alertThresholdPercent) is { } error)
    {
        return error;
    }

    return new Budget(NewId()) { WorkspaceId = workspaceId, ... };
}
```

Un `AggregateRoot` (`BuildingBlocks.Domain/Entity.cs:34`) es la unidad que se guarda entera y la única puerta para cambiar lo que contiene. Las piezas internas (las líneas de una operación, los participantes de una meta) se mapean como *owned types* y se borran en cascada con su raíz.

## 6. Eventos de dominio en proceso

**Problema.** Cuando se registra un gasto, Presupuestos debe avisar si se pasa del límite, pero Ledger no puede conocer a Budgets.

**Solución.** El agregado levanta un evento con `Raise` (`Entity.cs:51`) y otro módulo reacciona:

```csharp
// Modules/Ledger/.../Domain/Operation.cs:114
Raise(new LedgerOperationChanged(Id, WorkspaceId, OwnerUserId, change, ...));

// Modules/Budgets/.../Application/BudgetAlerts.cs
internal sealed class EvaluateBudgetThresholds(...) : IDomainEventHandler<LedgerOperationChanged>
```

`DomainEventDispatcher` (`Infrastructure/Messaging/DomainEventDispatcher.cs`) guarda primero (para que la reacción vea los datos), entrega los eventos y repite por rondas si una reacción levanta otros eventos, hasta un máximo de 10 rondas (más indica un ciclo). Todo ocurre **dentro de la misma transacción**: si una reacción falla, se revierte el comando entero. Por eso una reacción no puede llamar a nada externo ([ADR-006](../adr/ADR-006-eventos.md)).

El evento se declara en `Contracts/Events` si otro módulo reacciona; en `Domain` si solo reacciona el propio.

## 7. Contracts: la fachada de un módulo

**Problema.** Budgets necesita los gastos del mes, pero no puede leer las tablas del ledger.

**Solución.** Ledger publica una interfaz en su `Contracts` y la implementa con una clase `internal`:

```csharp
// Modules/Ledger/.../Contracts/LedgerContracts.cs:128
public interface ILedgerQueries
{
    Task<IReadOnlyList<LedgerExpense>> GetExpensesAsync(IReadOnlyCollection<Guid> categoryIds, DateOnly from, DateOnly to, CancellationToken ct);
    ...
}
```

`AddModule` registra sola la implementación (toda clase `internal` de `Application` que no sea un handler, bajo sus interfaces). Los tipos de `Contracts` son DTO propios del contrato, nunca entidades.

## 8. Una interfaz de persistencia por módulo (`IXDb`)

**Problema.** Un repositorio genérico (`IRepository<T>`) esconde EF sin aportar nada; un `DbContext` por módulo complica transacciones y migraciones ([ADR-019](../adr/ADR-019-simplificacion-de-la-plataforma.md)).

**Solución.** Cada módulo declara los `DbSet` que puede ver:

```csharp
// Modules/Budgets/.../Application/Abstractions/IBudgetsDb.cs
public interface IBudgetsDb
{
    DbSet<Budget> Budgets { get; }
}
```

`AppDbContext` implementa todas y es `internal`, así que ni la API ni el Worker pueden usarlo directamente.

## 9. Filtro global por dueño (multi-tenencia por fila)

**Problema.** Olvidar un `Where(x => x.OwnerUserId == me)` filtraría las finanzas de otra persona.

**Solución.** Toda entidad privada implementa `IOwned` y `AppDbContext` le aplica un filtro de consulta en el arranque:

```csharp
// Infrastructure/Persistence/AppDbContext.cs
modelBuilder.Entity<TEntity>().HasQueryFilter(entity =>
    entity.OwnerUserId == CurrentUserId && entity.WorkspaceId == PersonalWorkspaceId);
```

Metas y grupos de gastos compartidos tienen su filtro propio por participación. `IgnoreQueryFilters()` solo se permite en la lista blanca de `LayeringTests` (hoy, la siembra de categorías por defecto). Ver [security.md](security.md).

## 10. Ledger de doble entrada

**Problema.** Una columna `balance` se desincroniza; las transferencias contadas como gasto inflan los reportes.

**Solución.** Una `Operation` (el hecho que ve la persona) tiene `Entry` por cuenta afectada; el saldo es la suma de asientos. Una transferencia son dos asientos que se compensan y no es ingreso ni gasto. Nada se borra: se anula. Ver [ledger.md](ledger.md) y [ADR-004](../adr/ADR-004-ledger-financiero.md).

## 11. Clave de idempotencia

**Problema.** Un doble clic o un reintento de red no puede registrar el gasto dos veces.

**Solución.** El comando implementa `IIdempotentCommand` y lleva la cabecera `Idempotency-Key`. El decorador guarda la respuesta bajo `(usuario, clave)` y la devuelve en la repetición sin ejecutar nada. Detalle y carreras en [concurrency.md](concurrency.md#3-idempotencia).

## 12. Cola en tabla (en lugar de un broker)

**Problema.** Enviar un correo dentro de la transacción haría que una caída del SMTP rompiera el registro de un gasto, y un rollback no puede «des-enviar» un correo.

**Solución.** El comando escribe una fila (`platform.outgoing_emails`, `notifications.deliveries`) en su transacción; un job del Worker la envía después, con reintentos. Sin Redis, sin colas externas ([ADR-018](../adr/ADR-018-notificaciones.md)).

## 13. Template method para los jobs

`Worker/Jobs/PeriodicJob.cs` resuelve el bucle, el scope de DI por ejecución, el registro de errores y el reintento al minuto. Un job solo define `Interval` y `RunAsync`, fija en nombre de quién actúa y llama a un handler.

## 14. Registro por escaneo de ensamblado

`services.AddModule(typeof(IBudgetsDb).Assembly)` registra handlers, decoradores, reacciones y servicios del módulo. Añadir un caso de uso no requiere tocar la inyección de dependencias.

## 15. Options pattern

Configuración tipada: `EmailOptions`, `FrontendOptions` y `WhatsAppOptions` con su `SectionName`, enlazadas en `DependencyInjection.cs` e inyectadas como `IOptions<T>`. Ver [devops/configuration.md](../devops/configuration.md).

## 16. Auditoría funcional

Cada acción de negocio relevante llama a `IAuditLog.Record(new AuditEntry(...))` con el estado anterior y el nuevo. Se escribe en `platform.audit_log` dentro de la misma transacción ([ADR-017](../adr/ADR-017-auditoria-funcional.md)).

## 17. Contexto del actor

`Infrastructure/Security/ActorContext.cs` implementa `ICurrentUser`, `IWorkspaceContext` e `IPersonalSpace`. En HTTP lo inicializan la cookie y `WorkspaceScopeFilter` (`Api/Http/RequestContext.cs:68`); en el Worker, el job con `ActAs(...)`. Los handlers no saben de dónde vienen: el mismo caso de uso sirve a la API y al Worker con las mismas reglas de privacidad.

## Antipatrones que no se usan, y por qué

| No usar | Por qué | En su lugar |
|---|---|---|
| MediatR | Indirección sin beneficio; dificulta navegar el código | Inyectar el handler |
| AutoMapper | Errores de mapeo en tiempo de ejecución; esconde qué se expone | Construir la respuesta a mano o con un *reader* |
| `IRepository<T>` / `IUnitOfWork` genéricos | Duplican EF Core | `IXDb` y el decorador de transacción |
| Outbox, eventos de integración, Service Bus | Un solo proceso y una base no los necesitan ([ADR-019](../adr/ADR-019-simplificacion-de-la-plataforma.md)) | Eventos en proceso y cola en tabla |
| Hangfire, Quartz | Dependencia pesada para cuatro jobs | `PeriodicJob` |
| Lógica en controllers | Se salta decoradores, tests y el Worker | Handler |
| Excepciones de negocio | Ver §4 | `Result` |

Añadir cualquiera de ellos exige un ADR.
