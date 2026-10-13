# Comunicación entre módulos

## I. Dependency rules

### I.1 Diagrama de módulos

Las flechas apuntan hacia lo que se depende. El grafo es acíclico.

Diagrama: [Módulos](../diagrams/modules.md).

`Identity` crea el workspace personal a través de una interfaz que él mismo define y que `Workspaces` implementa, de modo que la dependencia de compilación va solo de `Workspaces` a `Identity`.

### I.2 Diagrama de dependencias entre proyectos

Diagrama: [Dependencias entre proyectos](../diagrams/dependencies.md).

### I.3 Reglas

| # | Permitido | Prohibido |
|---|---|---|
| 1 | `Modules.X` → namespace `Modules.Y.Contracts` | `Modules.X` → `Modules.Y.Domain` o `Modules.Y.Application` |
| 2 | `Infrastructure` → todos los módulos; hosts → `Infrastructure` y módulos | `BuildingBlocks.*` o un módulo → `Infrastructure` o un host |
| 3 | `X/Application` → `X/Domain` | `X/Domain` → `X/Application`, EF Core, ASP.NET, cualquier SDK |
| 4 | `X/Application` → abstracciones de EF Core (`DbSet`, LINQ) a través de `IXDb` | `X/Application` → ASP.NET, Npgsql, `Infrastructure` |
| 5 | `X/Contracts` → `BuildingBlocks.Domain` | `X/Contracts` → `X/Domain`, `X/Application`, EF Core, ASP.NET |
| 6 | Consultas solo sobre el `IXDb` propio | SQL crudo contra el esquema de otro módulo |
| 7 | Controller o job de la carpeta `X` → `Modules.X` | Controller o job → EF Core, Npgsql, `IXDb`, otro módulo |
| 8 | Dependencia de módulo en el sentido del diagrama I.1 | Dependencia en sentido contrario; `Ledger` nunca llama a `Goals` |

Un módulo es un solo proyecto con tipos públicos, así que el compilador no impide la regla 1: la impiden los tests de arquitectura. Todas las reglas se verifican ahí (ver [testing.md](../development/testing.md)); si un test falla se corrige el código, no el test.

### I.4 Comunicación entre módulos

| Necesidad | Mecanismo | Ejemplo |
|---|---|---|
| Leer datos de otro módulo | Llamada síncrona a su contrato | Budgets pide a Ledger el gasto por categoría |
| Cambiar el estado de otro módulo de forma atómica | Llamada síncrona a su contrato dentro de la misma transacción; solo hacia módulos aguas arriba | Goals registra una transferencia en Ledger |
| Que un módulo aguas arriba reaccione a algo aguas abajo | Interfaz definida por el de arriba e implementada por el de abajo | `IScheduledActionHandler` de Scheduling, implementado por Goals |
| Reaccionar a un hecho de otro módulo | Evento de dominio declarado en su `Contracts`, entregado en proceso | Budgets evalúa umbrales al recibir `OperationPosted` |
| Avisar a una persona | `INotificationPublisher` | "Diego registró un gasto. Tu parte: S/ 128" |
| Refrescar una pantalla abierta | `IRealtimeNotifier`, que envía la señal tras el commit | Señal `resource.changed` al grupo |

Una implementación de contrato no guarda: añade al contexto y deja que guarde el comando que la llamó. Así lo que hacen dos módulos queda en una sola transacción.

## Q. Events architecture

### Q.1 Un solo tipo de evento

Solo hay eventos de dominio. Lo que cambia es quién puede reaccionar.

| | Evento privado | Evento público |
|---|---|---|
| Qué expresa | Algo ocurrió dentro de un agregado | Lo mismo, y a otro módulo le interesa |
| Dónde se declara | `Modules.X/Domain` | `Modules.X/Contracts/Events` |
| Quién reacciona | El mismo módulo | Cualquier módulo aguas abajo |
| Contenido | Ids y datos mínimos | Ids y datos mínimos; es parte del contrato del módulo |

Ambos se entregan igual:

1. El agregado acumula el evento con `Raise`.
2. Cuando el handler del comando termina bien, el decorador de transacción guarda los cambios.
3. Entrega cada evento a sus `IDomainEventHandler<T>`, en proceso y dentro de la misma transacción. Como ya se guardó, la reacción puede consultar lo recién escrito.
4. Si una reacción cambió más agregados y levantó más eventos, repite (con un tope de rondas, para detectar ciclos).
5. Commit.

Consecuencias:

- **Todo o nada.** Si una reacción lanza una excepción, el comando entero se revierte y el usuario recibe un error. No hay consistencia eventual ni mensajes pendientes que vigilar.
- **Una reacción no llama a nada externo.** Para enviar un correo o un WhatsApp escribe una fila en la cola de envíos (`notifications.deliveries`), que un job procesa con reintentos. Es el único punto donde hace falta entrega diferida, y la fila se guarda con el resto del comando.
- **Guardar fuera del pipeline con eventos pendientes lanza una excepción**, para que ningún evento se pierda en silencio.

No hay outbox ni eventos de integración. La razón y la señal que haría reconsiderarlo están en el [ADR-019](../adr/ADR-019-simplificacion-de-la-plataforma.md).

### Q.2 Catálogo

| Módulo | Privados (en `Domain`) | Públicos (en `Contracts`) |
|---|---|---|
| Identity | PasswordResetRequested, EmailConfirmed | UserRegistered, UserProfileChanged |
| Workspaces | MemberRoleChanged, InvitationRevoked | WorkspaceCreated, OnboardingCompleted, MemberInvited, MemberJoined, MemberRemoved |
| Notifications | NotificationCreated, DeliveryFailed | InboundMessageReceived |
| Categories | CategoryCreated, CategoryRenamed, TagCreated | CategoryArchived |
| Ledger | AccountOpened, AccountUpdated | OperationPosted, OperationChanged, OperationVoided, AccountArchived |
| Scheduling | OccurrencePosted, OccurrenceSkipped, ReminderDue, OccurrenceFailed | — |
| Budgets | BudgetCreated, BudgetAdjusted, BudgetThresholdReached | — |
| Goals | GoalCreated, GoalUpdated | GoalParticipantAdded, GoalContributionAdded, GoalTargetReached, GoalCancelled |
| SharedExpenses | SharedExpenseSettled, GroupMemberAdded | SharedExpensePublished, SharedExpenseCancelled, SettlementRecorded |
| Integrations | ImportParsed, ImportCommitted | — |
| Investments | HoldingOpened, TermDepositMaturing | — |

`TransferCompleted` del encargo es `OperationPosted` con `kind = Transfer`. `BudgetThresholdReached` es privado porque su único efecto es publicar una notificación a través de `INotificationPublisher`; si un segundo módulo lo necesitara, pasaría a `Contracts`.

### Q.3 Suscripciones

| Evento | Quién reacciona | Reacción |
|---|---|---|
| UserRegistered | Notifications | Preferencias por defecto y punto de contacto de correo |
| OnboardingCompleted, MemberJoined | Categories | Sembrar categorías del usuario |
| MemberRemoved | Goals, SharedExpenses, Scheduling | Marcar participante inactivo; pausar reglas |
| OperationPosted, OperationChanged, OperationVoided | Budgets | Evaluar umbrales |
| OperationPosted | Ledger | Publica aviso de movimiento grande si supera el umbral que el usuario fijó en Notifications |
| AccountArchived | Scheduling | Pausar reglas que usan la cuenta |
| CategoryArchived | Scheduling, Integrations | Avisar de reglas y plantillas afectadas |
| GoalContributionAdded, GoalTargetReached, GoalParticipantAdded | Goals | Avisa al resto de participantes y pide la señal `resource.changed` |
| SharedExpensePublished, SettlementRecorded, SharedExpenseCancelled | SharedExpenses | Avisa con la parte de cada uno y pide la señal |
| InboundMessageReceived | Integrations | Interpretar comando y responder |

Para reaccionar a un evento hay que depender del módulo que lo publica, así que cada fila de esta tabla es también una flecha del grafo I.1.
