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
