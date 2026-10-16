# Estructura del backend

## N. Backend folder/project structure

Raíz: `finanzas-hub-api/`. El árbol muestra lo que existe tras la fase 13, con los 12 módulos; cada módulo repite la forma de `Ledger`, que se muestra desplegado.

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
    update-db-docs.mjs             regenera la referencia de docs/backend/database.md
  Dockerfile                       imagen de API y Worker sobre una carpeta publicada
  Jenkinsfile                      CI/CD en Mercury (ver docs/devops/ci-cd.md)
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
        Abstractions/              IClock, IAuditLog, IEmailSender, IAppLinks, IRealtimeNotifier, ISpreadsheets
    Modules/
      Identity/                    cuentas, sesión, perfil y preferencias (sobre ASP.NET Core Identity)
      Workspaces/                  workspaces, miembros, roles e invitaciones
      Categories/                  categorías, subcategorías y tags, privados por usuario
      Ledger/                      cuentas, operaciones y asientos: la única fuente de los saldos
        PersonalFinance.Modules.Ledger/
          Domain/                  Account, CreditCardProfile, Operation, Entry, OperationTag, ExchangeRate
          Application/
            Abstractions/          ILedgerDb: las tablas que el módulo puede ver
            Accounts/              comandos, consultas, respuestas y handlers de cuentas; NetWorthReader
            Transactions/          lo mismo para transacciones; TransactionReader arma las respuestas
            Transfers/             transferencia y pago de tarjeta
            ExchangeRates/         tipo de cambio; RateTable y BaseSum convierten a la moneda base
            Reports/               LedgerQueries: lo que el ledger responde a otros módulos
            LedgerScope.cs         dueño y fecha de hoy de la petición; AccountBalances
          Contracts/               ILedgerQueries y sus tipos: lo que otro módulo puede usar
      Notifications/               bandeja de avisos, preferencias, cola de envíos por WhatsApp, teléfono verificado
      Budgets/                     presupuestos mensuales por categoría y sus alertas
      Scheduling/                  movimientos programados, ocurrencias y calendario
      Goals/                       metas personales y compartidas, aportes y retiros
      SharedExpenses/              grupos, gastos repartidos y liquidaciones
      Integrations/                importación de archivos (bancos, Bluecoins), reglas de clasificación, consultas por WhatsApp
      Investments/                 inversiones, depósitos a plazo, movimientos y valorizaciones
      Reporting/                   Inicio, reportes, exportación y búsqueda global. Sin tablas ni dominio:
                                   lee los contratos de Ledger, Categories, Budgets, Goals, Scheduling e Investments
    PersonalFinance.Infrastructure/
      DependencyInjection.cs       AddInfrastructure, MigrateDatabaseAsync; la lista de módulos
      Persistence/
        AppDbContext.cs            el único contexto; filtro por dueño
        Configurations/<Módulo>/   mapeos EF, una carpeta por módulo y otra para Platform
        Migrations/
      Platform/                    IdempotencyRecord, AuditLogEntry, OutgoingEmail
      Messaging/                   registro de handlers; decoradores de logging, transacción e idempotencia;
                                   DomainEventDispatcher; RealtimeBuffer; limpieza de claves de idempotencia
      Audit/                       AuditLogWriter
      Email/                       cola de correos y su envío por SMTP; EmailOptions, FrontendOptions
      WhatsApp/                    WhatsAppOptions, firma del webhook, envío por la Graph API
      Spreadsheets/                ClosedXML: lectura de Excel de bancos y exportación de reportes
      Security/                    ActorContext, IActorContextInitializer
      Observability/               OpenTelemetry, Telemetry, CorrelationContext, health checks
    PersonalFinance.Api/
      Program.cs
      Controllers/
        ApiControllerBase.cs       Result a respuesta, lectura de Idempotency-Key
        Identity/                  AuthController, MeController
        Workspaces/                WorkspacesController, MembersController
        Categories/                CategoriesController
        Ledger/                    AccountsController, TransactionsController, TransfersController
        Budgets/                   BudgetsController
        Notifications/             NotificationsController, WhatsAppController (incluye el webhook)
        Scheduling/                SchedulingController
        Goals/                     GoalsController
        SharedExpenses/            SharedGroupsController
        Integrations/              ImportsController
        Investments/               InvestmentsController
        Reporting/                 DashboardController, ReportsController
      Http/                        correlación, contexto de actor, WorkspaceScopeFilter, Result a ProblemDetails,
                                   manejador global de excepciones, cookie de sesión, CORS, origen y límite de peticiones
      Realtime/                    RealtimeHub (/hubs/realtime) y su transporte SignalR
    PersonalFinance.Worker/
      Program.cs                   registra los jobs
      Jobs/
        PeriodicJob.cs             base de un job periódico
        Platform/                  PurgeExpiredIdempotencyKeysJob, SendEmailsJob
        Notifications/             SendWhatsAppJob
        Scheduling/                RunDueOccurrencesJob
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

**Paquetes.** EF Core y Npgsql; `EFCore.NamingConventions`; ASP.NET Core Identity y Data Protection sobre EF; ClosedXML; OpenTelemetry con exportador OTLP; `Microsoft.AspNetCore.OpenApi` y Scalar. Para tests: xUnit, Testcontainers para PostgreSQL y NetArchTest. La lista completa, con versiones y para qué sirve cada uno, está en [onboarding.md §3](../getting-started/onboarding.md#3-paquetes-nuget).

**Diferido respecto al diseño.** Piezas del diseño que siguen sin construirse:

| Pieza | Estado |
|---|---|
| `ETag` e `If-Match` en la API | Sin fecha. Los agregados llevan `xmin` y un choque dentro de una petición responde 409, pero la API no expone la versión: entre dos pestañas gana la última escritura. Ver [concurrency.md](concurrency.md) |
| Test de arquitectura «todo comando con efecto financiero implementa `IIdempotentCommand`» | Sin hacer: hoy se revisa a mano |
| `IFileStorage` | No hizo falta: las importaciones se leen en memoria y su contenido queda en la base |
