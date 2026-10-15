# Módulo Notifications

| | |
|---|---|
| Responsabilidad | Bandeja in-app, preferencias, canales, entregas, horario silencioso, puntos de contacto verificados |
| Aggregate roots | `Notification`, `NotificationSettings`, `ChannelEndpoint`, `Delivery` |
| Value objects | `NotificationCategory`, `Channel`, `Severity`, `QuietHours`, `DedupeKey` |
| Posee | Esquema `notifications` |
| Consulta de otros | `Identity.Contracts` (correo y nombre del destinatario) |
| Expone | `INotificationPublisher.Publish(NotificationRequest)`, `IDirectMessenger.Send(userId, channel, text)` |
| Eventos | `InboundMessageReceived` (I), `NotificationCreated` (D, dispara tiempo real), `DeliveryFailed` (D) |

No conoce a los módulos de negocio. Cada módulo decide qué avisar y con qué texto; Notifications decide por qué canales, cuándo y sin duplicar.

| Caso de uso | Autorización | Entrada | Reglas | Efectos y eventos |
|---|---|---|---|---|
| PublishNotification (Sistema, vía contrato) | Módulo llamante | destinatario, categoría, tipo, título, cuerpo, enlace, clave de deduplicación | Única por `(usuario, clave)`; respeta preferencias | Inserta en bandeja; crea entregas por canal; `NotificationCreated` |
| DeliverPending (Sistema) | — | — | Horario silencioso pospone canales externos salvo severidad crítica; reintento con espera creciente | Envía por el canal; actualiza la entrega |
| MarkRead, MarkAllRead | Destinatario | id | — | — |
| UpdatePreferences, UpdateSettings | Propio | categoría × canal; silencio; umbral de movimiento grande | Un canal externo exige punto de contacto verificado | — |
| StartWhatsAppVerification, ConfirmWhatsAppVerification | Propio | teléfono E.164; código | Código de un solo uso con caducidad; teléfono único entre verificados | `ChannelEndpoint` verificado |
| ReceiveWhatsAppWebhook (Sistema) | Firma HMAC | cuerpo crudo | Deduplicado por id externo | Actualiza entregas; `InboundMessageReceived` |
| ListNotifications, GetUnreadCount (queries) | Destinatario | filtro, cursor | — | — |

## Estado tras la fase 6

Implementado: `INotificationPublisher` (bandeja, señal en tiempo real y correo en la transacción de quien publica), `ListNotifications` (`GET /me/notifications?unreadOnly=`), `GetUnreadCount` (`GET /me/notifications/unread-count`), `MarkRead`, `MarkAllRead`, y `GET/PUT /me/notification-settings` con el correo por categoría, el horario silencioso y el monto del movimiento grande. Categorías: `Budget` y `LargeMovement`.

La bandeja y los ajustes son de la persona, no del workspace: sus tablas no llevan el filtro global de `IOwned` y cada consulta del módulo nombra al usuario de la sesión. El correo sale por la cola de correos de la plataforma; el horario silencioso se lee en la zona horaria del workspace del aviso.

Publican hoy: Budgets (`EvaluateBudgetThresholds`, al reaccionar a `LedgerOperationChanged`) y Ledger (`NotifyLargeMovement`).

Diferido: la purga a los 90 días. Las diferencias con el diseño están en [phases.md](../roadmap/phases.md).

## Estado tras la fase 11

| | |
|---|---|
| Dominio | `ChannelEndpoint` (número verificado, código con hash, intentos y caducidad), `Delivery` (cola de WhatsApp con reintentos y estado), `ReceivedWebhookEvent` (deduplicación por `(provider, external_id)`, levanta `InboundMessageReceived`) |
| Contratos | `IDirectMessenger.ReplyByWhatsAppAsync`; evento `InboundMessageReceived(UserId, Text)`, al que reacciona Integrations (`AnswerChatCommand`) |
| Casos de uso | `GetWhatsAppChannel`, `StartWhatsAppVerification`, `ConfirmWhatsAppVerification` (un código errado se guarda como intento: llega como valor, no como fallo), `RemoveWhatsApp`, `ReceiveWhatsAppWebhook` (sistema: estados de entrega y quién escribió) y `HandleWhatsAppMessage` (como el autor) |
| Preferencias | `whatsAppEnabled` por categoría; activarlo exige número verificado (`notifications.whatsapp_not_verified`) |
| Infraestructura | `Infrastructure/WhatsApp`: `WhatsAppOptions` (sección `WhatsApp`), `WhatsAppSignature`, `WhatsAppSender` (Cloud API o carpeta), `DeliveryDispatcher`; job `Jobs/Notifications/SendWhatsAppJob` |
| API | `/api/v1/me/channels/whatsapp` (GET, DELETE, `verify-start`, `verify-confirm`) y `/webhooks/v1/whatsapp` (fuera del contrato OpenAPI: solo lo usa Meta) |
| Métricas | `whatsapp.webhook.events` (tipo y resultado), `whatsapp.deliveries`, y `notifications.published` con la etiqueta `whatsapp` |
