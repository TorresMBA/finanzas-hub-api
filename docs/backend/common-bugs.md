# Bugs frecuentes en el backend

Errores que es fácil cometer en esta base de código, con el síntoma, la causa y qué los detecta. Repásalo antes de abrir un pull request.

| # | Síntoma | Causa | Cómo evitarlo | Lo detecta |
|---|---|---|---|---|
| 1 | `InvalidOperationException: … was saved outside the command pipeline with domain events pending` | El handler llamó a `SaveChanges` | No guardes en el handler: lo hace el decorador | `AppDbContext.EnsureDomainEventsWereDispatched` |
| 2 | Una reacción a un evento no se ejecuta | Se guardó fuera del pipeline, o el evento se levantó en una entidad que no es `AggregateRoot` | Usa `Raise` en el agregado y deja que el decorador guarde | Test de integración del caso |
| 3 | Una persona ve datos de otra del mismo workspace | `IgnoreQueryFilters()`, SQL a mano sin filtro o entidad privada sin `IOwned` | Implementa `IOwned`; no ignores filtros; filtra a mano en `FromSql` | `LayeringTests` (lista blanca) y `CommandPipelineTests` (todo `IOwned` filtrado) |
| 4 | Un recurso ajeno responde 403 y delata que existe | Se comprobó el permiso después de encontrarlo | Si el filtro no lo ve, `NotFound`. 403 solo para quien participa sin permiso | Casos de seguridad en los tests de integración |
| 5 | Gasto registrado dos veces tras un doble clic | Comando financiero sin `IIdempotentCommand`, o el frontend genera la clave al enviar | Implementa la interfaz y lee `Idempotency-Key`; en el frontend, clave al abrir el formulario | Revisión de código (no hay test de arquitectura todavía) |
| 6 | Céntimos que aparecen o desaparecen | `double`/`float`, o redondear en varios sitios | `decimal` y un único punto de redondeo | `LayeringTests` (prohíbe `double` y `float`) |
| 7 | Totales absurdos en un reporte | Sumar importes de monedas distintas | Convierte a la moneda base con el tipo de cambio antes de sumar | Tests de reportes con datos en dos monedas |
| 8 | Transferencias contadas como gasto | Filtrar por signo del asiento en vez de por tipo de operación | Ingreso y gasto se definen por `kind`, no por el signo | `ReportsTests`, `LedgerTransfersTests` |
| 9 | Un movimiento cae en el mes equivocado | Usar UTC del servidor como «hoy» o convertir `DateOnly` a instante | «Hoy» en la zona del workspace; `DateOnly` para fechas contables | Tests con `IClock` cerca de medianoche |
| 10 | El comando falla porque «no se pudo enviar el correo» | Llamada externa dentro de una reacción o un handler | Encola (`IEmailSender`, publicador de Notifications); el Worker envía | Revisión; el contrato `IDomainEventHandler` lo documenta |
| 11 | Un aviso se repite | Publicar sin `dedupe_key` | Clave con lo que lo hace único (`budget:{categoría}:{mes}:over`) | Índice `ix_notifications_user_id_dedupe_key` |
| 12 | Duplicado bajo carga aunque hay un `AnyAsync` previo | Carrera en Read Committed | Índice único y, si es plausible, capturar la violación como `Conflict` | [concurrency.md](concurrency.md#4-índices-únicos-la-última-barrera) |
| 13 | Error de compilación o test rojo de fronteras al usar algo de otro módulo | Se usó su entidad, su `IXDb` o una flecha no permitida | Usa su `Contracts`; si de verdad hace falta la flecha, añádela al grafo con justificación | `ModuleBoundaryTests` |
| 14 | El frontend no compila tras cambiar la API, o el test de contrato falla | No se regeneró `openapi.v1.json` | `tools/update-openapi.ps1` y `npm run api:generate` | `OpenApiContractTests` |
| 15 | Se pierden datos al migrar | EF interpretó un renombre como borrar y crear la columna | Revisa la migración; usa `RenameColumn` a mano si hace falta | Aplicar la migración sobre una copia con datos |
| 16 | La migración falla en producción pero no en local | Datos existentes que no cumplen un `CHECK` o un `NOT NULL` nuevo | Migra los datos en la misma migración antes de la restricción | Aplicarla sobre una copia de la base anterior |
| 17 | Fallo intermitente en un handler que lee de dos sitios a la vez | `Task.WhenAll` con dos consultas sobre el mismo `DbContext` | Consultas secuenciales | Error en tiempo de ejecución de EF |
| 18 | El job se ejecuta pero «no hace nada» para nadie | No fijó el actor (`ActAs`) y el filtro global no ve filas | Fija para quién actúa antes de llamar al handler | Test del job o logs del Worker |
| 19 | Los cambios de una persona no llegan en tiempo real a otra | Se notificó al usuario equivocado o antes del commit | `IRealtimeNotifier.ToUser` con cada destinatario; se envía tras el commit | Probar con dos sesiones |
| 20 | 500 en vez de 400 con un body inválido | Falta `[Required]` o el tipo no es anulable en el request | Propiedades anulables con `[Required]` en el request del controller | Test de integración con body incompleto |
| 21 | En local, una migración o `dotnet run` muere a la mitad | Filtrar la salida con `\| Select-Object -First N` en PowerShell cierra la tubería y mata el proceso | Captura toda la salida (`\| Out-String` o a un fichero) y filtra después | — |

## Lista de comprobación antes de un pull request

- [ ] El handler devuelve `Result` y no llama a `SaveChanges`.
- [ ] Lo ajeno responde 404 y hay un test que lo comprueba.
- [ ] Si mueve dinero: `IIdempotentCommand`, `decimal`, moneda explícita y auditoría.
- [ ] Nada externo dentro de la transacción.
- [ ] La unicidad la garantiza un índice.
- [ ] Migración revisada y aplicada sobre la base anterior; `update-db-docs.mjs` ejecutado.
- [ ] `update-openapi.ps1` ejecutado si cambió la API.
- [ ] `dotnet build` sin avisos y `dotnet test` en verde, también los de arquitectura.
