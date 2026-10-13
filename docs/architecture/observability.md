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
| Trabajo diferido | La cola de envíos guarda el `traceparent` de quien encoló; el job crea un span enlazado (fase 6) |
| Instrumentación | ASP.NET Core, HttpClient, Npgsql, y un `ActivitySource` común (`PersonalFinance`) con un span por caso de uso etiquetado con `module` y `use_case` |
| Destino | OTLP. Local: contenedor Aspire Dashboard. Azure: Application Insights |

"Distributed tracing" aquí significa seguir una operación desde la petición en la API hasta el envío externo que hace el Worker.

### U.3 Métricas y salud

| Métrica | Tipo |
|---|---|
| `usecase.duration` por módulo, caso de uso y resultado | Histograma |
| `webhook.received` por proveedor y resultado | Contador |
| `notifications.deliveries` por canal y resultado; `notifications.deliveries.pending` | Contador, gauge |
| `scheduler.occurrences` por resultado | Contador |
| `ledger.operations.posted` por tipo y origen | Contador |
| `auth.failed_logins`, `security.forbidden` | Contador |

Health checks: `/health/live` (el proceso responde) y `/health/ready` (base de datos alcanzable y migraciones aplicadas). Hoy solo existe la métrica `usecase.duration`; las demás llegan con su módulo.

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

El rol de base de datos de la aplicación solo tiene `INSERT` y `SELECT` sobre `audit_log`.
