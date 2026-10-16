# Tiempo real

## T. SignalR / realtime

### T.1 Qué lo justifica

| Escenario | ¿Tiempo real? | Motivo |
|---|---|---|
| Notificaciones in-app y badge | **Sí** | Es el canal natural y su coste es mínimo |
| Grupo de gastos compartidos abierto | **Sí, como señal** | Otro miembro añade un gasto o un pago mientras lo miras |
| Meta compartida abierta | **Sí, como señal** | Lo mismo con un aporte |
| Dashboard colaborativo | No | Bajo D2 el dashboard es privado |
| Transacciones, cuentas, presupuestos propios | No | Solo los cambia el propio usuario |
| Progreso de importación | No | Sondeo cada 2 s mientras se analiza |

"Como señal" significa que por el socket no viajan datos de negocio: solo "este recurso cambió". El cliente vuelve a pedir por HTTP y la autorización se aplica ahí.

### T.2 Especificación

Un único hub en `/hubs/realtime`.

| Event | Publisher | Subscribers | Payload | Authorization | Fallback |
|---|---|---|---|---|---|
| `notification.created` | Notifications, al insertar en la bandeja | Grupo `user:{userId}` | `{ id, category, severity, title, unreadCount }` | El usuario entra en su grupo al conectar, según su identidad | Consultar el contador cada 60 s y al recuperar el foco |
| `resource.changed` con `shared-group` | SharedExpenses, al reaccionar a `SharedExpensePublished`, `SettlementRecorded` y `SharedExpenseCancelled` | Grupo `shared-group:{id}` | `{ resource, id, reason }` | `Subscribe(resource, id)` comprueba membresía del grupo mediante `SharedExpenses.Contracts` | Recargar al recuperar el foco y botón de actualizar |
| `resource.changed` con `goal` | Goals, al reaccionar a sus eventos | Grupo `goal:{id}` | `{ resource, id, reason }` | `Subscribe` comprueba participación mediante `Goals.Contracts` | Igual |

Los módulos no dependen de SignalR: llaman a `IRealtimeNotifier`. La implementación con SignalR vive en `PersonalFinance.Api/Realtime`: acumula las señales durante la petición y las envía después del commit, para no anunciar algo que luego se revierte. Si el proceso muere entre el commit y el envío, el cliente se pone al día con el sondeo de respaldo.

El hub vive en la API. Un aviso creado por un job del Worker (un recordatorio, un pago programado) no se empuja: el navegador lo recoge con el sondeo de respaldo, cada 60 s y al recuperar el foco. Azure SignalR Service se añade cuando haya más de una réplica.

Desde la fase 6 existe el hub y `notification.created`, que lleva `{ id, category, title }`: el navegador vuelve a pedir el contador por HTTP. Se dirige con `Clients.User`, que SignalR resuelve con la identidad de la cookie, en lugar de un grupo propio. Las señales `resource.changed` llegan con sus módulos.

### T.3 Flujo

Diagrama: [Flujo de tiempo real](../diagrams/flow-realtime.md).
