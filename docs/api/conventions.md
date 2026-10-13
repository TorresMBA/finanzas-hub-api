# Convenciones de API

## P. API conventions

### P.1 Workspace en la URL

| | `/api/v1/workspaces/{workspaceId}/goals` | `/api/v1/goals` con cabecera o claim |
|---|---|---|
| Contexto | Explícito en cada petición y en cada log | Oculto |
| Varias pestañas con workspaces distintos | Correcto | Propenso a errores |
| Autorización | Un filtro global sobre toda ruta con `{workspaceId}` | Hay que leer cabecera en cada sitio |
| Enlaces y caché | Estables | Dependen de estado externo |
| Verbosidad | Mayor | Menor |

**Decisión: workspace en la URL** para todo recurso que pertenece a un workspace. Lo personal va bajo `/api/v1/me`. La verbosidad la absorbe el cliente generado.

### P.2 Convenciones

| Tema | Convención |
|---|---|
| Versión | Prefijo `/api/v1` |
| Recursos | Sustantivos en plural y kebab-case. El término de API es `transactions`; internamente es `Operation` |
| Anidamiento | Solo composición real: `shared-groups/{id}/expenses` |
| Acciones no CRUD | `POST` a un sub-recurso verbal: `/void`, `/confirm`, `/archive`, `/publish`, `/commit` |
| Verbos | `GET` lee; `POST` crea o ejecuta; `PATCH` modifica parcialmente; `PUT` reemplaza una colección o un singleton; `DELETE` solo donde el dominio borra o termina |
| Paginación | Cursor (`?cursor=&limit=`) en transacciones, notificaciones y actividad; sin paginar en listas pequeñas |
| Filtros | Query string; fechas `YYYY-MM-DD`; meses `YYYY-MM` |
| JSON | Propiedades en camelCase; enumerados como texto |
| Concurrencia | `ETag` en la respuesta, `If-Match` en `PATCH`, `PUT` y acciones sobre agregados con `xmin` |
| Idempotencia | `Idempotency-Key` en POST con efecto financiero |
| Correlación | `X-Correlation-Id` aceptado y devuelto |
| Creación | 201 con `Location` y el recurso |
| Documentación | OpenAPI generado; `openapi.v1.json` versionado en `docs/api/`; un test falla si cambia sin actualizarlo |

### P.3 Errores

`Result<T>` en dominio y aplicación; una única función lo traduce a `application/problem+json` (RFC 9457).

| `ErrorType` | HTTP | Ejemplo de `code` |
|---|---|---|
| Validation (forma) | 400, con `errors` por campo | `validation.required` |
| Unauthorized | 401 | `auth.unauthenticated` |
| Forbidden | 403 | `goals.not_owner` |
| NotFound | 404 | `ledger.account.not_found` |
| Conflict | 409 | `concurrency_conflict`, `idempotency.in_progress`, `categories.name_taken` |
| BusinessRule | 422 | `ledger.transfer.same_account`, `shared.settlement.exceeds_debt` |
| Inesperado (excepción) | 500, sin detalle interno | `unexpected` |

Todo `ProblemDetails` lleva `code` estable y `traceId`. Las excepciones se reservan para errores de programación e infraestructura; una regla de negocio nunca lanza. En V1 toda `DomainException` salía como 409, incluidos "no encontrado" y "sin acceso".

La matriz pantalla → API está en [product/screen-api-matrix.md](../product/screen-api-matrix.md).
