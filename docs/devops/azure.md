# Evolución en Azure

## W. Azure evolution strategy

El dominio y la aplicación no referencian ningún SDK de Azure; todo llega por las abstracciones de F.6 y por configuración.

| Servicio | Clase | Uso | Equivalente local |
|---|---|---|---|
| Azure Container Apps | MVP | Una aplicación con dos contenedores, API y Worker, y una réplica siempre activa | `dotnet run` de cada proyecto |
| Azure Database for PostgreSQL Flexible Server | MVP | Base única, nivel burstable, respaldo con recuperación a un punto en el tiempo | Contenedor postgres |
| Azure Static Web Apps | MVP | SPA Angular; mismo sitio que la API para la cookie | `ng serve` con proxy |
| Azure Key Vault | MVP | Cadena de conexión, secretos de WhatsApp y de correo, claves de Data Protection | `dotnet user-secrets` |
| Azure Monitor y Application Insights | MVP | Destino de OpenTelemetry | Aspire Dashboard |
| Azure Communication Services Email | MVP | Confirmación, recuperación, invitaciones, avisos | Mailpit |
| Azure Container Registry | MVP | Imágenes | Docker local |
| Azure Storage (Blob) | OPTIONAL | Archivos de importación, avatares y exportaciones tras `IFileStorage`; se activa en la Fase 10 si el disco no basta | Disco local |
| Azure Service Bus | SCALING | Transporte de eventos al separar un módulo en un servicio propio | Eventos en proceso |
| Azure SignalR Service | SCALING | Al pasar de una réplica | SignalR en proceso |
| `PersonalFinance.Worker` en su propia Container App | SCALING | Al pasar de una réplica de API, para que los jobs sigan corriendo en un solo sitio | El Worker como segundo contenedor |
| Azure App Configuration | OPTIONAL | Flags y configuración centralizada | `appsettings` y variables de entorno |
| Azure Functions | OPTIONAL | No previsto | — |
| Front Door con WAF | OPTIONAL | Si se abre a público general | — |

**Camino de evolución.**

| Disparador | Cambio |
|---|---|
| Segunda réplica de API | Sacar el Worker a su propia aplicación con una réplica; añadir Azure SignalR |
| Un módulo necesita escalar o desplegarse por separado | Darle su propio `DbContext` e historial, cortar las FKs hacia su esquema y moverlo a otra base, sustituir las llamadas a su contrato por HTTP o mensajes, y entregar sus eventos por outbox y Service Bus |
| Sumas de saldo lentas | Snapshots mensuales por cuenta (opción C de K.2) |
| Reportes lentos | Vistas materializadas publicadas por Ledger como contrato de lectura |
| Muchos más usuarios | Row-Level Security como segunda barrera |

Infraestructura como código (Bicep) se añade al preparar el primer despliegue, no en la Fase 0.
