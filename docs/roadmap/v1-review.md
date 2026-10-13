# Revisión del diseño anterior (V1)

## Anexo 1 — Revisión del diseño actual (V1)

Veredictos sobre `..\finance-hub-api` y `..\fiances-hub-web`. KEEP = se porta casi tal cual; CHANGE = se porta con cambios; MOVE = cambia de sitio; DELETE = no pasa a V2; ADD = nuevo.

### Proyectos del backend

| Proyecto o carpeta | Veredicto | Detalle |
|---|---|---|
| `BuildingBlocks.Domain` | KEEP y ADD | Hoy solo `DomainException` y `WorkspaceCalendar`. `DomainException` se sustituye por `Result` y `Error`. `WorkspaceCalendar` deja de hacer falta en gran parte al usar `date`; lo que quede va a Workspaces. Se añaden `Entity`, `AggregateRoot`, `Money`, `Currency`, `IOwned` |
| `BuildingBlocks.Application` | CHANGE | KEEP `IClock`. DELETE `IUnitOfWork` (lo cubre el decorador de transacción). DELETE `IOutbox`: los eventos se entregan en proceso. ADD mensajería, seguridad, `IDomainEventHandler` y abstracciones de F.6 |
| `BuildingBlocks.Infrastructure` | CHANGE, pasa a `PersonalFinance.Infrastructure` | Sigue referenciando a los 12 módulos y conteniendo el único contexto. Lo que cambia: deja de ser un "building block" situado bajo los módulos (ninguno lo referencia), y las reglas entre módulos existen y están probadas. KEEP un solo `DbContext`, ahora con un esquema por módulo y los mapeos en una carpeta por módulo. DELETE los 20 repositorios de `Persistence/Repositories` y su `CanAccessAsync` repetido: cada módulo usa su `IXDb` y el filtro global por dueño. DELETE las 28 migraciones (esquema nuevo). KEEP `SystemClock.cs`. DELETE `OutboxMessage.cs`, `OutboxRepository.cs` e `InboundWebhookMessage.cs` (ADR-019) |
| `BuildingBlocks.Observability` | DELETE como proyecto | Un archivo (`Telemetry.cs`) y ningún paquete de OpenTelemetry: hoy no exporta nada. MOVE a `PersonalFinance.Infrastructure/Observability`, con un `ActivitySource` y un `Meter` comunes |
| `PersonalFinance.Api` | KEEP y CHANGE | Sigue alojando todos los controllers, ahora en una carpeta por módulo. Deja de contener lógica y acceso a datos |
| `Api/Controllers` | KEEP, reordenado | En `Controllers/<Módulo>/`. Cada controller llama a handlers en lugar de a servicios, y `ApiControllerBase.TryGetCurrentUserId` se sustituye por `ICurrentUser` |
| `Api/DependencyInjection` | MOVE | A `Infrastructure/DependencyInjection.cs` (`AddInfrastructure`), que llaman los dos hosts |
| `Api/Documentation` | DELETE | `Program.cs` registra a la vez `AddOpenApi` y `AddSwaggerGen`. Queda un solo generador de OpenAPI y `AuthorizeOperationFilter` deja de hacer falta |
| `Api/Helpers`, `Api/Utilities` | DELETE | Carpetas vacías |
| `Api/Integrations/WhatsApp` | MOVE | `WhatsAppWebhookService` usa `ApplicationDbContext` desde el host. La recepción va a Notifications; la interpretación de comandos a Integrations |
| `Api/WhatsAppWebhook.cs` | MOVE, se divide | Reúne tres cosas: opciones, verificador HMAC e intérprete de comandos. Las dos primeras van a `Infrastructure/WhatsApp`, tras una interfaz de Notifications; `ParseReadOnlyCommand` va a `Integrations/Application/ChatCommands`. El verificador se porta tal cual |
| `Api/DemoDataSeeder.cs` | MOVE | A `tools/seed`, ejecutando comandos de aplicación en lugar de escribir en el `DbContext` |
| `Api/Program.cs` | CHANGE | KEEP la validación de CORS y de cadena de conexión. CHANGE el manejador que convierte toda `DomainException` en 409. CHANGE `MapIdentityApi` por endpoints propios |
| `Api/Dockerfile`, `appsettings.json` | KEEP | — |
| `PersonalFinance.Contracts` | DELETE | Solo requests HTTP en un namespace plano, referenciado solo por Api. Los requests van junto a su controller; lo que un módulo expone a otros, a su carpeta `Contracts` |
| `PersonalFinance.Worker` | KEEP | Aloja todos los jobs, en `Jobs/<Módulo>/`. DELETE `OutboxDispatcher.cs`. MOVE `SchedulingWorker.cs` a `Jobs/Scheduling`, llamando a un caso de uso. MOVE `WhatsAppCloudOutboxEventHandler.cs` al canal de WhatsApp y a `Jobs/Notifications`; hoy consulta `ApplicationDbContext` y envía a todos los destinatarios del workspace cualquier evento `finance.*` |

### Módulos

| Módulo V1 | Veredicto | Detalle |
|---|---|---|
| Identity | CHANGE | Hoy un archivo (`ApplicationUser`). Pasa a módulo real con registro propio, perfil y preferencias |
| Workspaces | KEEP y ADD | KEEP `Workspace` y `WorkspaceMembership` con sus reglas de rol. ADD estado, onboarding, invitaciones (hoy `AddMemberAsync` exige que el usuario ya exista). MOVE `WorkspaceWhatsAppRecipient` a `ChannelEndpoint` por usuario. CHANGE roles a tres |
| Accounts, Transactions, Transfers | MOVE, fusión en Ledger | KEEP invariantes de `Account`, `LedgerTransaction` y `Transfer`. CHANGE `LedgerTransaction` a `Operation` con `Entry`. CHANGE `Debit/Credit` a `Out/In`. MOVE `Label` a `Tag` en Categories. Reconciliación, estados de cuenta y cuotas esperan a la Fase 14 |
| Categories | CHANGE | `CategoryKind { Group, Item }` pasa a `{ Income, Expense }`; las transacciones pueden usar la categoría raíz. `CategoryTag` como texto pasa a relación con `Tag`. ADD dueño y `system_key` |
| Budgets | CHANGE | KEEP `BudgetPeriodRange`, `BudgetProration`, `BudgetProgress`, `BudgetReportBuilder`. DELETE `MonthlyBudget` (legado). MOVE `Goal` y `GoalContribution` al módulo Goals. CHANGE `BudgetSpendingRule`, que hoy referencia tipos de dominio de Transactions, a una consulta del contrato de Ledger |
| Dashboard | MOVE | A Reporting |
| Imports | KEEP, renombrado a Integrations | KEEP `ImportSession`, `IImportSourceAdapter`, `BluecoinsCsvAdapter`. ADD la ruta de confirmación que hoy falta, y las reglas |
| Investments | KEEP y ADD | KEEP `InvestmentAsset` e `InvestmentTransaction`. ADD `Holding`, `PriceSnapshot`, depósitos a plazo y el enlace con el ledger |
| Scheduling | KEEP | El módulo mejor resuelto de V1. CHANGE dueño por usuario y uso del contrato de Ledger en lugar de sus tipos de dominio |
| SharedExpenses | CHANGE | KEEP el cálculo de reparto con sus cuatro tipos. CHANGE `SharedExpenseGroupMember` de nombre libre a usuario. ADD estados, asignación de pagos por parte y los dos lados de la liquidación. Hoy no tiene autorización por participación |

### Código de V1 que se porta

Rutas relativas a `..\finance-hub-api\`.

| Origen | Destino en V2 | Fase |
|---|---|---|
| `src\BuildingBlocks\...Infrastructure\OutboxMessage.cs`, `Persistence\Repositories\OutboxRepository.cs`, `src\PersonalFinance.Worker\OutboxDispatcher.cs` | No se portan: sin outbox (ADR-019) | — |
| `src\PersonalFinance.Api\Program.cs` (validación de CORS y conexión) | `PersonalFinance.Api/Program.cs` | 0 |
| `src\Modules\Workspaces\...\Domain\Workspace.cs`, `WorkspaceMembership.cs`; `tests\...\WorkspaceTests.cs` | `Modules.Workspaces/Domain` | 1 |
| `src\Modules\Categories\...\Domain\Category.cs` | `Modules.Categories/Domain` | 2 |
| `src\Modules\Accounts\...\Domain\Account.cs`; `src\Modules\Transactions\...\Domain\LedgerTransaction.cs`, `LedgerStatusRules.cs`; `tests\...\AccountTests.cs`, `LedgerTransactionTests.cs` | `Modules.Ledger/Domain` | 3 |
| `src\Modules\Transfers\...\Domain\Transfer.cs`; `src\Modules\Accounts\...\Services\CreditCardPaymentService.cs`; `tests\...\TransferTests.cs` | `Modules.Ledger/Domain/Operations`, `Application/CreditCards` | 4 |
| `src\Modules\Budgets\...\Domain\BudgetPeriodRange.cs`, `BudgetProration.cs`, `BudgetProgress.cs`, `BudgetSpendingRule.cs`; `Application\Services\BudgetReportBuilder.cs`; `tests\...\BudgetReportTests.cs` | `Modules.Budgets/Domain` | 5 |
| `src\Modules\Scheduling\...\Domain\RecurrenceSchedule.cs`, `RecurringTransactionRule.cs`, `ScheduledOccurrence.cs`; `Application\Services\ScheduleProjection.cs`, `SchedulingRunner.cs`, `OccurrencePoster.cs`; `src\PersonalFinance.Worker\SchedulingWorker.cs`; `tests\...\SchedulingTests.cs` | `Modules.Scheduling`; el worker a `Worker/Jobs/Scheduling` | 7 |
| `src\Modules\Budgets\...\Domain\Goal.cs`; `tests\...\GoalTests.cs` | `Modules.Goals/Domain` | 8 |
| `src\Modules\SharedExpenses\...\Domain\SharedExpense.cs`, `SharedExpenseSettlement.cs`; `tests\...\SharedExpenseTests.cs`, `SharedExpenseSettlementServiceTests.cs` | `Modules.SharedExpenses/Domain` | 9 |
| `src\Modules\Imports\...\Domain\ImportSession.cs`, `Adapters\BluecoinsCsvAdapter.cs`, `Application\Services\ImportService.cs`; `tests\...\ImportServiceTests.cs`, `BluecoinsCsvAdapterTests.cs` | `Modules.Integrations` | 10 |
| `src\PersonalFinance.Api\WhatsAppWebhook.cs`, `Integrations\WhatsApp\WhatsAppWebhookService.cs`; `src\PersonalFinance.Worker\WhatsAppCloudOutboxEventHandler.cs` | `Infrastructure/WhatsApp`, `Api/Controllers/Notifications`, `Worker/Jobs/Notifications`, `Modules.Integrations/Application/ChatCommands` | 11 |
| `src\Modules\Investments\...\Domain\InvestmentAsset.cs`, `InvestmentTransaction.cs`; `tests\...\InvestmentTransactionTests.cs` | `Modules.Investments/Domain` | 13 |
| `src\Modules\Accounts\...\Domain\CreditCardCycle.cs`, `InstallmentPlan.cs`, `AccountReconciliation.cs`; `Application\Services\CreditCardStatementBuilder.cs`, `InstallmentService.cs`, `ReconciliationService.cs`; `tests\...\CreditCardStatementTests.cs`, `InstallmentTests.cs`, `ReconciliationTests.cs` | `Modules.Ledger/CreditCards` | 14 |

Al portar, cada `throw new DomainException(...)` pasa a `Result` con un `Error` de código estable, y cada entidad recibe `owner_user_id` o participantes según la sección J.2.

### Tests, documentación y entorno

| Elemento | Veredicto | Detalle |
|---|---|---|
| `tests/PersonalFinance.Modules.Workspaces.UnitTests` | CHANGE | El nombre no corresponde: contiene los tests de todos los módulos. Se reparten por carpeta en `UnitTests`. DELETE el uso de EF InMemory |
| `tests/PersonalFinance.Api.IntegrationTests` | KEEP | Tests de CORS, salud y OpenAPI |
| ADD | — | `ArchitectureTests`; fixture de Testcontainers; matriz de seguridad |
| `docs/adr/0008`, `0009`, `0010`, `0013`, `0014` | KEEP | Antecedentes de los ADR 004 y 008 y de la Fase 14 |
| `AGENTS.md` | CHANGE | Indica React como frontend; es Angular. Sus prioridades de ingeniería se conservan |
| `docker-compose.yml` | KEEP y ADD | Añadir mailpit y aspire-dashboard |

### Frontend

| Elemento | Veredicto | Detalle |
|---|---|---|
| `shared/ui/*` | KEEP | data-table, empty-state, error-state, loading-state, status-badge, currency-amount, pagination, confirm-dialog |
| `shared/components/*` | DELETE | Duplicado de `shared/ui` |
| `core/layout/*` | DELETE | Stubs de 70 bytes; lo real está en `layout/` |
| `core/api/*`, `core/models/*` | DELETE | Duplican `features/*/services` y `features/*/models`; se sustituyen por el cliente generado |
| `core/interceptors/correlation-id`, `error` | KEEP | Con sus tests |
| `core/interceptors/auth`, `bearer` | CHANGE | Pasan a credenciales por cookie y XSRF |
| `core/services/theme.service`, `workspace-context.service` | KEEP, adaptados | El segundo pierde los workspaces de demostración |
| `core/services/demo-data.service`, `demo-data-toggle` | DELETE | Datos falsos mezclados con código de producción |
| `features/auth/login-page.component.ts` (50 KB) y páginas de 25 a 32 KB | CHANGE | Se dividen en página y componentes presentacionales |
| `features/transfers`, `features/workspaces`, `features/imports` | MOVE | A `transactions`, `settings` e `integrations`, como en el mock |
| `styles/_tokens.scss` | CHANGE | Se alinea con las variables del mock |
