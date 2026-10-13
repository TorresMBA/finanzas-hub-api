# Estructura del backend

## N. Backend folder/project structure

Raíz: `finanzas-hub-api/`. El árbol muestra lo que existe tras la fase 3; cada módulo de negocio que se añada repite la forma de `Ledger`.

```text
finanzas-hub-api/
  PersonalFinance.slnx
  Directory.Build.props            net10.0, Nullable, TreatWarningsAsErrors
  Directory.Packages.props         versiones de paquetes centralizadas
  docker-compose.yml               postgres, mailpit, aspire-dashboard
  .env.example                     variables de docker-compose
  dotnet-tools.json                dotnet-ef
  tools/
    migrate.ps1                    aplica las migraciones pendientes
    add-migration.ps1              crea una migración
    update-openapi.ps1             regenera docs/api/openapi.v1.json
  src/
    BuildingBlocks/
      PersonalFinance.BuildingBlocks.Domain/
        Entity.cs                  Entity, AggregateRoot, IDomainEvent, IOwned
        Result.cs, Error.cs
        Money.cs, Currency.cs, DateRange.cs
      PersonalFinance.BuildingBlocks.Application/
        Messaging/                 ICommand, IQuery, ICommandHandler, IQueryHandler, IIdempotentCommand, Unit
        Security/                  ICurrentUser, IWorkspaceContext, IWorkspaceAccess, WorkspaceRole
        Events/                    IDomainEventHandler
        Abstractions/              IClock, IAuditLog, IEmailSender, IAppLinks
    Modules/
      Identity/                    cuentas, sesión, perfil y preferencias (sobre ASP.NET Core Identity)
      Workspaces/                  workspaces, miembros, roles e invitaciones
      Categories/                  categorías, subcategorías y tags, privados por usuario
      Ledger/                      cuentas, operaciones y asientos: la única fuente de los saldos
        PersonalFinance.Modules.Ledger/
          Domain/                  Account, Operation, Entry, OperationTag
          Application/
            Abstractions/          ILedgerDb: las tablas que el módulo puede ver
            Accounts/              comandos, consultas, respuestas y handlers de cuentas
            Transactions/          lo mismo para transacciones; TransactionReader arma las respuestas
            LedgerScope.cs         dueño y fecha de hoy de la petición; AccountBalances
          Contracts/               lo que otro módulo puede usar (aparece cuando hace falta;
                                   ver Categories/Contracts, que usa el ledger)
    PersonalFinance.Infrastructure/
      DependencyInjection.cs       AddInfrastructure, MigrateDatabaseAsync; la lista de módulos
      Persistence/
        AppDbContext.cs            el único contexto; filtro por dueño
        Configurations/<Módulo>/   mapeos EF, una carpeta por módulo y otra para Platform
        Migrations/
      Platform/                    IdempotencyRecord, AuditLogEntry
      Messaging/                   registro de handlers; decoradores de logging, transacción e idempotencia;
                                   DomainEventDispatcher; limpieza de claves de idempotencia
      Audit/                       AuditLogWriter
      Email/                       cola de correos y su envío por SMTP
      Security/                    ActorContext, IActorContextInitializer
      Observability/               OpenTelemetry, Telemetry, CorrelationContext, health checks
    PersonalFinance.Api/
      Program.cs
      Controllers/
        ApiControllerBase.cs       Result a respuesta, lectura de Idempotency-Key
        Identity/                  AuthController, MeController
        Workspaces/                WorkspacesController, MembersController, invitaciones
        Categories/                CategoriesController, TagsController
        Ledger/                    AccountsController, TransactionsController: un controller por recurso,
                                   en la carpeta de su módulo
      Http/                        correlación, contexto de actor, WorkspaceScopeFilter, Result a ProblemDetails,
                                   manejador global de excepciones, cookie de sesión, CORS, origen y límite de peticiones
    PersonalFinance.Worker/
      Program.cs                   registra los jobs
      Jobs/
        PeriodicJob.cs             base de un job periódico
        Platform/                  PurgeExpiredIdempotencyKeysJob, SendEmailsJob
        <Módulo>/                  jobs de cada módulo
  tests/
    PersonalFinance.UnitTests/           dominio y funciones puras, carpeta por módulo
    PersonalFinance.IntegrationTests/    API real sobre PostgreSQL real
    PersonalFinance.ArchitectureTests/   reglas de dependencias y de capas
```

La documentación (`docs/`) está en la raíz del repositorio, no dentro de `finanzas-hub-api`, porque cubre también el frontend.

Recuento objetivo con los 12 módulos: 2 BuildingBlocks, 12 módulos, Infrastructure, 2 hosts y 3 de tests. Total 20.

## El flujo, por entrada

```text
PersonalFinance.Api/Controllers/<Módulo>     PersonalFinance.Worker/Jobs/<Módulo>
                    \                           /
                     Modules.<Módulo>/Application     un handler por caso de uso
                                  |
                     Modules.<Módulo>/Domain          reglas e invariantes
```

`PersonalFinance.Infrastructure` queda fuera de ese camino: implementa lo que la aplicación pide (persistencia, auditoría, adaptadores externos) y ningún módulo la referencia.

**Controller.** Valida la forma del request, arma el comando o la consulta, llama a su handler y convierte el `Result` en respuesta. No contiene reglas de negocio ni toca persistencia. Declara su ruta completa (`api/v1/workspaces/{workspaceId:guid}/...`). El request HTTP se define junto al controller; la respuesta es el tipo que devuelve el handler.

**Job.** Corre en el Worker, en un scope propio por ejecución. Fija para quién actúa y llama a un caso de uso de su módulo. Si falla, se registra y se reintenta al minuto.

**Handler.** Un caso de uso. Devuelve `Result<T>`, no llama a `SaveChanges` y accede a los datos por el `IXDb` de su módulo.

**Cada carpeta de caso de uso** contiene el comando o la consulta, su handler y, si los hay, la validación de negocio y la política de acceso. Añadir una funcionalidad es añadir una carpeta.

## Visibilidad

Un módulo es un solo proyecto y sus tipos no están ocultos por el compilador.

| Tipo | Visibilidad | Por qué |
|---|---|---|
| Entidades y eventos de `Domain` | `public` | Infrastructure los mapea |
| Comandos, consultas y respuestas | `public` | Los usan los controllers y los jobs |
| `IXDb` | `public` | Lo implementa `AppDbContext` |
| `Contracts` | `public` | Es lo que otros módulos pueden usar |
| Handlers e implementaciones | `internal` | Nadie de fuera los nombra |
| `AppDbContext` | `internal` | Ni la API ni el Worker pueden usarlo: no compila |

Que un módulo no use las entidades de otro lo garantizan los tests de arquitectura, no el compilador. Ver [module-communication.md](module-communication.md).

## Añadir un módulo

1. Proyecto `src/Modules/X/PersonalFinance.Modules.X` con `Domain/` y `Application/`. Referencia los dos BuildingBlocks y el paquete `Microsoft.EntityFrameworkCore`.
2. `Application/Abstractions/IXDb.cs` con los `DbSet` de sus tablas.
3. En `PersonalFinance.Infrastructure`: referencia al proyecto, `IXDb` en `AppDbContext`, sus configuraciones en `Persistence/Configurations/X/` con el esquema del módulo, y dos líneas en `DependencyInjection.cs`:

   ```csharp
   services.AddModule(typeof(IXDb).Assembly);
   services.AddScoped<IXDb>(provider => provider.GetRequiredService<AppDbContext>());
   ```

4. Migración: `tools/add-migration.ps1 -Name AddX`.
5. Controllers en `PersonalFinance.Api/Controllers/X/` y, si los hay, jobs en `PersonalFinance.Worker/Jobs/X/`.
6. Su entrada en el grafo de `ModuleBoundaryTests.AllowedDependencies`.

`AddModule` registra los handlers de comando, de consulta y de evento de dominio (cada handler de comando envuelto en los decoradores) y los servicios del módulo: toda clase `internal` de su capa `Application` que no sea un handler, bajo las interfaces que implemente. Así un módulo aporta la implementación de sus `Contracts` sin que nadie de fuera la nombre.

**Paquetes.** EF Core y Npgsql; `EFCore.NamingConventions`; OpenTelemetry con exportador OTLP; `Microsoft.AspNetCore.OpenApi`. Para tests: xUnit, Testcontainers para PostgreSQL y NetArchTest. Previstos: ASP.NET Core Identity (fase 1) y un lector de XLSX en `Integrations` (fase 10).

**Diferido respecto al diseño.** Estas piezas se crean con el primer módulo que las use, no antes:

| Pieza | Llega en |
|---|---|
| Tipos de paginación por cursor | Fase 3 |
| `ETag` e `If-Match` en la API | Fase 3 |
| Marca de "comando con efecto financiero" y su test de arquitectura | Fase 3 |
| `IRealtimeNotifier` y el hub SignalR | Fase 6 |
| Cola de envíos (`notifications.deliveries`) y su job | Fase 6 |
| `IFileStorage` | Fase 10 |
| Controller del webhook de WhatsApp | Fase 11 |
