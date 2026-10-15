# Procesos en segundo plano y webhooks

## R. Background processing

### R.1 Responsabilidades

Todo lo que corre sin una petición es un job del Worker, en `PersonalFinance.Worker/Jobs/<Módulo>/`.

| Job | Módulo | Disparo | Idempotencia |
|---|---|---|---|
| Purga de claves de idempotencia caducadas | Platform | Diario | Natural |
| Envío de correos (confirmación, recuperación, invitaciones) | Platform | Cada 10 s | Fila reclamada con `SKIP LOCKED` y marcada como enviada |
| Envío de notificaciones por correo y WhatsApp | Notifications | Cada 10 s | `(notification_id, channel)` |
| Ocurrencias vencidas y recordatorios | Scheduling | Cada 15 min | `(rule_id, scheduled_date)`; clave de deduplicación del aviso |
| Vencimiento de tarjeta | Ledger, invocado por el ciclo de Scheduling | Cada 15 min | Clave de deduplicación |
| Aportes automáticos a metas | Goals, vía `IScheduledActionHandler` | Con el ciclo de Scheduling | `(rule_id, scheduled_date)` |
| Análisis de archivo importado | Integrations | Cada pocos segundos, sobre las sesiones en estado `Uploaded` | Estado de la sesión |
| Interés de cuotas (Fase 14) | Ledger | Diario | `(plan_id, number)` |
| Tipos de cambio y precios (futuro) | Ledger, Investments | Diario | `(base, quote, as_of, source)` |
| Invitaciones caducadas y notificaciones antiguas | Workspaces, Notifications | Diario | Natural |

La evaluación de umbrales de presupuesto no es un job: es una reacción a `OperationPosted` dentro de la transacción del gasto (ver [module-communication.md](module-communication.md)).

Hoy existen los dos primeros, el ciclo de Scheduling y el envío de WhatsApp (`SendWhatsAppJob`); el correo de los avisos va por la cola de correos de la plataforma. Sin servidor SMTP configurado, el de correos escribe cada mensaje como un archivo `.eml` en una carpeta, que es lo que se usa en local y en los tests E2E.

### R.2 Cómo es un job

```csharp
internal sealed class PurgeExpiredIdempotencyKeysJob(IServiceScopeFactory scopeFactory, ILogger<PurgeExpiredIdempotencyKeysJob> logger)
    : PeriodicJob(scopeFactory, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(24);

    protected override async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var removed = await services.GetRequiredService<IIdempotencyKeyCleanup>().PurgeExpiredAsync(cancellationToken);
        Logger.LogInformation("Removed {Count} expired idempotency key(s)", removed);
    }
}
```

- `PeriodicJob` ejecuta una vez al arrancar y luego cada `Interval`, con un scope de DI nuevo por ejecución.
- Una ejecución que falla se registra y se reintenta al minuto. Nunca tumba el proceso.
- Un job no contiene reglas de negocio ni toca persistencia: llama a un caso de uso de su módulo. Lo comprueba un test de arquitectura.
- Un job que actúa por un usuario (registrar la ocurrencia de una regla) fija el actor con `IActorContextInitializer` antes de llamar al handler, de modo que el filtro por dueño y la auditoría funcionan igual que en una petición.
- Se registra en `PersonalFinance.Worker/Program.cs` con `AddHostedService`.

### R.3 Tecnología

| Opción | Evaluación |
|---|---|
| **`BackgroundService` en `PersonalFinance.Worker`, con PostgreSQL como cola** | **Elegida.** Sin infraestructura adicional; suficiente para esta carga |
| Alojar los jobs dentro de la API | Descartado: se pidió ver el flujo del Worker separado, como en V1. Sigue siendo posible si un día se quiere un solo proceso |
| Hangfire | Innecesario ahora. Aporta panel y reintentos que no hacen falta a esta escala |
| Quartz.NET | Útil más adelante si hacen falta expresiones cron o manejo fino de ejecuciones perdidas |
| Azure Functions | Innecesario. Añadiría un segundo modelo de despliegue |
| Service Bus | Solo para escalar o al separar un servicio |

**Topología.** Dos procesos: la API atiende HTTP y el Worker ejecuta los jobs. Los dos llaman a `AddInfrastructure`, así que ejecutan la misma aplicación. En local el Worker solo hace falta cuando se trabaja en un job.

Un job que toma trabajo de una tabla lo reclama con `FOR UPDATE SKIP LOCKED` y cada efecto tiene una clave natural única, de modo que dos instancias del Worker no duplicarían nada.

**Tiempo real.** El hub de SignalR vive en la API. Un aviso creado por un job no puede empujarse al navegador desde el Worker: llega por el sondeo de respaldo del cliente (cada 60 s y al recuperar el foco). Ver [realtime.md](realtime.md).

## S. Webhooks

### S.1 Inventario

| Proveedor | Para qué | Estado |
|---|---|---|
| WhatsApp Cloud API (Meta) | Mensajes entrantes (comandos) y estados de entrega | MVP, Fase 11 |
| Proveedor de correo | Rebotes y quejas | Opcional |
| Agregador bancario | Movimientos nuevos, token caducado | Diferido (D4) |
| Pasarela de pago | Suscripción del plan | Diferido (D4) |
| Broker o fuente de precios | — | No aplica: se consulta por sondeo programado |
| Lectura de correo (Gmail, Outlook) | Constancias de Yape y Plin | Diferido |
| Webhooks salientes y tokens de API propios | Integraciones del usuario | Diferido |

Un webhook es una petición HTTP que nos hace un proveedor, así que se recibe con un controller normal. SignalR va en el otro sentido (la API avisa al navegador) y no lo sustituye.

### S.2 Especificación de WhatsApp

| Aspecto | Diseño |
|---|---|
| Provider | Meta, WhatsApp Cloud API |
| Endpoint | `GET /webhooks/v1/whatsapp` para la verificación inicial; `POST /webhooks/v1/whatsapp` para eventos. Fuera de `/api`, anónimos, con límite de peticiones. Controller en `Api/Controllers/Notifications/` |
| Event | `messages` (texto entrante) y `statuses` (sent, delivered, read, failed) |
| Authentication | En el GET, `hub.verify_token` comparado con el secreto configurado |
| Signature verification | Cabecera `X-Hub-Signature-256`: HMAC-SHA256 del cuerpo crudo con el App Secret, comparado en tiempo constante. Se porta `WhatsAppWebhookVerifier.IsValid` de V1. Firma inválida responde 401 y no se guarda nada |
| Idempotency | Índice único sobre `(provider, external_id)` en una tabla de Notifications, donde `external_id` es el id del mensaje, o id más estado en los estados de entrega. Un evento repetido se ignora y se responde 200 |
| Processing | En la misma petición y en una sola transacción: registrar el evento, actualizar la entrega o levantar `InboundMessageReceived`, y encolar la respuesta en la cola de envíos |
| Retry | Si el procesamiento falla se responde 500 y nada queda guardado; Meta reintenta |
| Error handling | Un comando de un teléfono no verificado se descarta con log informativo. La respuesta al usuario sale por la cola de envíos, que tiene sus propios reintentos |

Cambios frente a V1: el controller y `WhatsAppWebhookService` usaban `ApplicationDbContext` directamente; la deduplicación era "consultar y luego insertar"; y el teléfono se resolvía a un workspace en lugar de a un usuario.

No hay una bandeja genérica de webhooks. Si un segundo proveedor necesita guardar el evento y procesarlo después (porque su procesamiento sea lento o el proveedor no reintente), se añade entonces.

### S.3 Flujo

Diagrama: [Flujo de un webhook](../diagrams/flow-webhook.md).
