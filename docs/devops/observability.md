# Observabilidad y auditoría

## U. Observability

### U.1 Logging técnico

- `ILogger` con plantillas estructuradas y exportación por OpenTelemetry. No se añade Serilog.
- Cada petición abre un scope con `trace_id`, `correlation_id`, `user_id`, `workspace_id`, `module` y `use_case`.
- Niveles: `Information` para inicio y fin de caso de uso con duración y resultado; `Warning` para regla de negocio rechazada y reintento; `Error` para excepción.
- Se registran ids; nunca correos, teléfonos, tokens, descripciones de movimientos ni cuerpos de archivos.

### U.2 Correlación y trazas

| Elemento | Diseño |
|---|---|
| Trace ID | W3C `traceparent`, generado por la instrumentación de ASP.NET Core |
| Correlation ID | Cabecera `X-Correlation-Id`; si no llega, se usa el trace id. Se devuelve en la respuesta y se añade como etiqueta |
| Reacciones a eventos | Corren dentro de la petición, así que quedan en la misma traza, con un span por handler de evento |
| Trabajo diferido | Diseñado: que la cola guarde el `traceparent` de quien encoló y el job cree un span enlazado. **No implementado**: hoy el envío del Worker es una traza aparte |
| Instrumentación | ASP.NET Core, HttpClient, Npgsql, y un `ActivitySource` común (`PersonalFinance`) con un span por caso de uso etiquetado con `module` y `use_case` |
| Destino | OTLP. Local: contenedor Aspire Dashboard. Azure: Application Insights |

"Distributed tracing" aquí significa seguir una operación desde la petición en la API hasta el envío externo que hace el Worker.

### U.3 Métricas y salud

Métricas que existen hoy, todas en el `Meter` `PersonalFinance`:

| Métrica | Tipo | Dónde |
|---|---|---|
| `usecase.duration` por módulo, caso de uso y resultado | Histograma (ms) | `Infrastructure/Observability/Telemetry.cs` |
| `ledger.operations.posted` por tipo | Contador | Ledger |
| `scheduler.occurrences` por resultado | Contador | Scheduling |
| `notifications.published` | Contador | Notifications |
| `whatsapp.webhook.events` por tipo y resultado | Contador | Notifications |
| `whatsapp.deliveries` | Contador | Infrastructure (envío) |
| `imports.records` por destino de cada línea | Contador | Integrations |
| `investments.operations` | Contador | Investments |
| `reporting.dashboard.duration`, `reporting.report.duration` | Histograma (ms) | Reporting |
| `reporting.exports` por formato | Contador | Reporting |
| `auth.failed_logins` por motivo (`invalid_credentials`, `locked_out`) | Contador | Identity |
| `security.forbidden` por endpoint | Contador | Api (todo 403, lo dé quien lo dé) |
| `notifications.deliveries.pending` por canal (`email`, `whatsapp`) | Gauge | Infrastructure: lo que cada job de envío vio pendiente en su última vuelta |
| `scheduler.cycle.duration` | Histograma (ms) | Worker, ciclo de Scheduling |

Las métricas diseñadas en la fase 6 existen todas desde el 17-oct-2026.

Health checks: `/health/live` (el proceso responde) y `/health/ready` (base de datos alcanzable y sin migraciones pendientes).

### U.4 Auditoría financiera

Distinta del log técnico: es dato de negocio, vive en la base, se escribe en la misma transacción que el cambio y no caduca.

| Campo | Contenido |
|---|---|
| Who | `actor_user_id` y `actor_type` (User, System, Integration) |
| What | `module` y `action`, por ejemplo `ledger.operation.voided` |
| When | `occurred_at` |
| Resource | `resource_type`, `resource_id`, `owner_user_id`, `workspace_id`; y `scope_type` con `scope_id` para agrupar por meta o por grupo |
| Previous value / New value | `before` y `after` en `jsonb`, solo los campos que cambian |
| CorrelationId | `correlation_id` y `trace_id` |
| Origen | `source`: Web, WhatsApp, Import, Scheduler, System |

Qué se audita:

| Módulo | Acciones |
|---|---|
| Ledger | Abrir, editar y archivar cuenta; registrar, editar, confirmar y anular operación; ajuste de saldo |
| Goals | Crear y editar meta (objetivo, fecha); participantes; aporte, retiro y anulación; cancelación |
| SharedExpenses | Crear, editar, publicar y cancelar gasto; liquidación, vinculación y anulación; miembros |
| Budgets | Crear, ajustar y terminar |
| Scheduling | Crear y modificar regla; confirmar y omitir ocurrencia; registro automático |
| Integrations | Confirmar importación con totales |
| Workspaces | Invitar, unirse, cambiar rol, expulsar, transferir propiedad, cambiar moneda base |
| Identity | Inicio de sesión fallido repetido, cambio de contraseña, cambio de correo |

La escritura es explícita desde el handler (`IAuditLog.Record(...)`), no un volcado automático de todo cambio de EF, porque lo que interesa es la acción de negocio. El historial de un recurso se consulta con la misma autorización que el recurso. La "Actividad reciente" de un grupo es una lectura de esta tabla filtrada por `scope`.

Diseño pendiente: que el rol de base de datos de la aplicación solo tenga `INSERT` y `SELECT` sobre `audit_log`. Hoy las migraciones no conceden permisos y la aplicación se conecta con un rol dueño de la base, así que la auditoría es de solo inserción por convención del código, no porque la base lo impida ([secrets.md](secrets.md#recomendaciones-pendientes)).

## Operación: qué mirar

| Pregunta | Dónde mirar |
|---|---|
| ¿Está viva y conectada a la base? | `GET /health/ready` (desde la red interna) |
| ¿Por qué falló la petición que reporta alguien? | El `traceId` del `ProblemDetails` (o el `X-Correlation-Id`) en los logs y en las trazas |
| ¿Qué caso de uso es lento? | Histograma `usecase.duration` por `use_case` |
| ¿Se están enviando los correos y WhatsApp? | Logs del Worker (`Sent N email(s)`, avisos de reintento) y `whatsapp.deliveries` |
| ¿Corre el calendario? | Logs del Worker (`Schedule cycle: …`) y `scheduler.occurrences` |
| ¿Quién cambió este dato? | `platform.audit_log` filtrando por `resource_type` y `resource_id` |

En local, todo llega al Aspire Dashboard (`http://localhost:18888`) si `docker compose up` está en marcha. En un servidor sin colector OTLP, los logs quedan en la salida estándar del contenedor (`docker logs finanzas-hub-api-<env>`).
