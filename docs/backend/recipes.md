# Recetas del backend

Pasos concretos para los cambios más habituales. Los nombres de ejemplo (`Thing`, módulo `X`) se sustituyen por los reales; los fragmentos siguen la forma del código existente. Las rutas son relativas a `finanzas-hub-api/`.

## 1. Un caso de uso

1. **Dominio** (`src/Modules/X/.../Domain/`). Si la regla es nueva, añade un método o una fábrica al agregado que devuelva `Result` y cubre sus invariantes con un test unitario en `tests/PersonalFinance.UnitTests/X/`.
2. **Comando o consulta y handler** (`src/Modules/X/.../Application/`), en el archivo del área (por ejemplo `Budgets.cs`):

```csharp
/// <summary>Qué hace, en una frase, desde el punto de vista de la persona.</summary>
public sealed record RenameThingCommand(Guid ThingId, string Name) : ICommand<ThingResponse>;

internal sealed class RenameThingHandler(IXDb db, IAuditLog audit, IClock clock) : ICommandHandler<RenameThingCommand, ThingResponse>
{
    public async Task<Result<ThingResponse>> HandleAsync(RenameThingCommand command, CancellationToken cancellationToken)
    {
        // Lo ajeno no aparece: el filtro global lo deja fuera, así que es 404.
        if (await db.Things.SingleOrDefaultAsync(thing => thing.Id == command.ThingId, cancellationToken) is not { } thing)
        {
            return Error.NotFound("x.thing_not_found", "The thing was not found.");
        }

        var before = new { thing.Name };
        var renamed = thing.Rename(command.Name, clock.UtcNow);
        if (renamed.IsFailure)
        {
            return renamed.Error;
        }

        audit.Record(new AuditEntry("x", "x.thing.renamed", "thing", thing.Id) { OwnerUserId = thing.OwnerUserId, Before = before, After = new { thing.Name } });
        return ThingResponse.From(thing);      // sin SaveChanges
    }
}
```

3. **Test de integración** en `tests/PersonalFinance.IntegrationTests/XTests.cs`: el camino feliz y el caso de seguridad (otra persona del mismo workspace recibe 404).
4. No hay que registrar nada: `AddModule` encuentra el handler.

Una **consulta** es igual con `IQuery<T>` / `IQueryHandler<,>`, `AsNoTracking()` y sin auditoría.

## 2. Un endpoint

En el controller del recurso (`src/PersonalFinance.Api/Controllers/X/ThingsController.cs`), con el request definido en el mismo archivo:

```csharp
public sealed class RenameThingRequest
{
    [Required, MaxLength(80)]
    public string? Name { get; init; }
}

/// <summary>Cambia el nombre. Lo ve solo su dueño.</summary>
[HttpPatch("{thingId:guid}", Name = "RenameThing")]
[ProducesResponseType<ThingResponse>(StatusCodes.Status200OK)]
[Problem(StatusCodes.Status400BadRequest)]
[Problem(StatusCodes.Status422UnprocessableEntity)]
public async Task<IActionResult> Rename(Guid workspaceId, Guid thingId, RenameThingRequest request,
    [FromServices] ICommandHandler<RenameThingCommand, ThingResponse> handler, CancellationToken cancellationToken) =>
    ToActionResult(await handler.HandleAsync(new RenameThingCommand(thingId, request.Name!), cancellationToken), Ok);
```

- La clase lleva `[Route("api/v1/workspaces/{workspaceId:guid}/things")]`: el parámetro se llama exactamente `workspaceId`.
- Después: `tools/update-openapi.ps1` (con la API en marcha) y, en `finanzas-hub-web`, `npm run api:generate`.

## 3. Un comando financiero (idempotente)

```csharp
public sealed record RegisterThingPaymentCommand(Guid AccountId, decimal Amount, string IdempotencyKey)
    : ICommand<PaymentResponse>, IIdempotentCommand;
```

En el controller:

```csharp
public async Task<IActionResult> Pay(Guid workspaceId, PayRequest request,
    [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
    [FromServices] ICommandHandler<RegisterThingPaymentCommand, PaymentResponse> handler, CancellationToken cancellationToken)
{
    if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
    {
        return problem;
    }

    return ToActionResult(await handler.HandleAsync(new RegisterThingPaymentCommand(request.AccountId!.Value, request.Amount!.Value, key), cancellationToken), Ok);
}
```

Ejemplos reales: `GoalsController` (aportes y retiros) y `TransfersController`. El movimiento de dinero se registra **llamando al contrato del ledger**, nunca escribiendo en sus tablas.

## 4. Reaccionar a algo que pasó en otro módulo

1. Si el evento no existe, declara un `record` que implemente `IDomainEvent` en `Contracts/Events/` del módulo emisor, con los ids y datos que la reacción necesite (no entidades).
2. En el agregado emisor, `Raise(new ThingHappened(...))` dentro del método que cambia el estado.
3. En el módulo que reacciona, una clase `internal` en `Application/`:

```csharp
internal sealed class DoSomethingOnThingHappened(IYDb db) : IDomainEventHandler<ThingHappened>
{
    public async Task HandleAsync(ThingHappened domainEvent, CancellationToken cancellationToken)
    {
        // Corre dentro de la transacción del comando: puede leer lo que este escribió.
        // Nada externo aquí: para avisar, encola (INotificationPublisher, IEmailSender).
    }
}
```

4. Si es una dependencia nueva entre módulos, añade la `ProjectReference`, la flecha en `ModuleBoundaryTests.AllowedDependencies` y en [diagrams/modules.md](../diagrams/modules.md).

## 5. Leer datos de otro módulo

1. Busca si su `Contracts` ya lo ofrece (por ejemplo `ILedgerQueries`, `ICategoryLookup`).
2. Si no, añade un método a su interfaz de contrato, con tipos de respuesta propios del contrato, y su implementación en la clase `internal` que ya lo implementa (`LedgerQueries`).
3. Inyecta la interfaz en tu handler. La implementación aplica sus propios filtros de privacidad.

## 6. Avisar a la persona

Usa el publicador de Notifications (`INotificationPublisher.PublishAsync(new NotificationRequest(...))`) desde un handler o una reacción. Pon una `dedupeKey` si el mismo aviso podría repetirse (ver `EvaluateBudgetThresholds` en `Modules/Budgets/.../Application/BudgetAlerts.cs`). La bandeja, el correo y el WhatsApp salen de ahí según las preferencias de la persona.

## 7. Un job del Worker

```csharp
// src/PersonalFinance.Worker/Jobs/X/DoPendingThingsJob.cs
/// <summary>Qué procesa y cada cuánto.</summary>
internal sealed class DoPendingThingsJob(IServiceScopeFactory scopeFactory, ILogger<DoPendingThingsJob> logger) : PeriodicJob(scopeFactory, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(15);

    protected override async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var result = await services.GetRequiredService<ICommandHandler<DoPendingThingsCommand, Unit>>()
            .HandleAsync(new DoPendingThingsCommand(), cancellationToken);
        // ...
    }
}
```

- Regístralo en `src/PersonalFinance.Worker/Program.cs` con `builder.Services.AddHostedService<DoPendingThingsJob>();`.
- Si actúa sobre datos privados, recorre a las personas y, para cada una, crea un scope y fija el actor con `ActAs` y `EnterWorkspace`, como `RunDueOccurrencesJob`.
- Que sea seguro ejecutarlo dos veces seguidas: «procesa lo pendiente», no «haz X a las 9:00».
- La lógica va en el handler; el job no toca la base.

## 8. Un módulo nuevo

1. Proyecto `src/Modules/X/PersonalFinance.Modules.X/` con carpetas `Domain/`, `Application/` y `Contracts/`; referencia los dos BuildingBlocks y `Microsoft.EntityFrameworkCore` (sin versión). Añádelo a `PersonalFinance.slnx`.
2. `Application/Abstractions/IXDb.cs` con sus `DbSet`.
3. En `PersonalFinance.Infrastructure`:
   - la referencia al proyecto;
   - `IXDb` en la lista de interfaces de `AppDbContext`;
   - la constante del esquema en `Schemas`;
   - las configuraciones en `Persistence/Configurations/X/`;
   - y en `DependencyInjection.cs`:

   ```csharp
   services.AddModule(typeof(IXDb).Assembly);
   services.AddScoped<IXDb>(provider => provider.GetRequiredService<AppDbContext>());
   ```

4. Referencia el módulo desde `PersonalFinance.Api` (y desde los tests unitarios si tiene dominio).
5. Su entrada en `ModuleBoundaryTests.AllowedDependencies` y en [diagrams/modules.md](../diagrams/modules.md).
6. Migración (§10), controllers en `Controllers/X/`, jobs en `Jobs/X/`.
7. Su documento en [docs/modules/](../modules/) y una fila en [backend/README.md](README.md).

## 9. Una clave de configuración

1. Propiedad en una clase `XOptions` con `public const string SectionName = "X";`, en Infrastructure.
2. `services.Configure<XOptions>(configuration.GetSection(XOptions.SectionName));` en `DependencyInjection.cs`; inyéctala como `IOptions<XOptions>`.
3. El valor no secreto por defecto en `appsettings.json`; los secretos, nunca.
4. Una fila en [devops/configuration.md](../devops/configuration.md) y, si es secreta, en [devops/secrets.md](../devops/secrets.md).

## 10. Una migración

```powershell
# desde finanzas-hub-api
tools/add-migration.ps1 -Name AddThings
# revisa src/PersonalFinance.Infrastructure/Persistence/Migrations/<fecha>_AddThings.cs
tools/migrate.ps1
node tools/update-db-docs.mjs
```

Revisa renombres, `NOT NULL` sobre datos existentes y `CHECK` nuevos. Más en [migrations.md](migrations.md).

## 11. Un paquete NuGet

1. Comprueba que no está en la lista de lo que exige un ADR ([AGENTS.md](../../AGENTS.md)).
2. Versión exacta en `Directory.Packages.props`, en el grupo que corresponda.
3. `<PackageReference Include="Paquete" />` sin versión en el `.csproj`. Los módulos no pueden traer proveedores de base de datos ni frameworks web **[test]**.
4. Una fila en la tabla de paquetes de [getting-started/onboarding.md](../getting-started/onboarding.md#3-paquetes-nuget).
