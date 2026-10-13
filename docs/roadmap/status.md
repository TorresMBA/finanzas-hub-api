# Estado de las fases

Tablero de avance. Se actualiza al empezar y al terminar cada fase. El alcance de cada una está en [phases.md](phases.md), junto con lo que se verificó al cerrarla; aquí solo va en qué punto está y qué quedó pendiente.

Última actualización: 13-oct-2026.

| Fase | Contenido | Estado | Cerrada |
|---|---|---|---|
| 0 | Fundación: plataforma, pipeline de comandos, shell del frontend | ✅ Completada | 13-oct-2026 |
| 1 | Identity y Workspaces | ✅ Completada | 13-oct-2026 |
| 2 | Categories | ✅ Completada | 13-oct-2026 |
| 3 | Ledger I: cuentas y transacciones | ✅ Completada | 13-oct-2026 |
| 4 | Ledger II: transferencias, tarjetas básicas, patrimonio e Inicio | ⏳ Pendiente (siguiente) | — |
| 5 | Budgets | ⏳ Pendiente | — |
| 6 | Notifications y tiempo real | ⏳ Pendiente | — |
| 7 | Scheduling y Calendario | ⏳ Pendiente | — |
| 8 | Goals | ⏳ Pendiente | — |
| 9 | SharedExpenses | ⏳ Pendiente | — |
| 10 | Integrations: importación y reglas | ⏳ Pendiente | — |
| 11 | WhatsApp | ⏳ Pendiente | — |
| 12 | Reporting | ⏳ Pendiente | — |
| 13 | Investments | ⏳ Pendiente | — |
| 14 | Tarjetas avanzadas y FX | ⏳ Pendiente | — |

Estados: ⏳ pendiente, 🚧 en curso, ✅ completada.

## Última verificación

13-oct-2026, al cerrar la fase 3.

| Qué | Resultado |
|---|---|
| Backend: build | 0 errores, 0 advertencias |
| Backend: tests | 177 en verde (89 unitarios, 19 de arquitectura, 69 de integración) |
| Frontend: build, tests y lint | Build correcto, 101 tests, lint limpio |
| E2E en Chrome | 17 en verde |

## Pendientes que dejaron las fases cerradas

Lo que una fase cerrada no hizo y sigue debiéndose. Al retomar un punto, se borra de aquí.

### Fase 0

| Pendiente | Se retoma |
|---|---|
| Todo lo que necesita Docker (`docker compose`, Testcontainers) | Sin verificar en esta máquina, que no tiene Docker |

### Fase 1

| Pendiente | Se retoma |
|---|---|
| Cambiar la moneda base de un workspace | Fase 4: ya existe el ledger para saber si hay asientos; va con el tipo de cambio |
| Archivar un workspace | Sin fecha |
| Foto de perfil | Sin fecha |
| Inicio de sesión con Google o Microsoft (OQ-25) | Backlog diferido |
| Envío por SMTP real y respuesta 429 del límite de `/auth` | Sin verificar; se prueban al desplegar |

### Fase 2

| Pendiente | Se retoma |
|---|---|
| Monto y número de movimientos por categoría en la pantalla de Categorías | Fase 4, con la consulta de gasto por categoría del Inicio |
| Renombrar un tag | Sin fecha |
| Mover una categoría a otro padre | Sin fecha: el mock no lo pide |
| Evento `CategoryArchived` | Fase 7: lo usa Scheduling |

### Fase 3

| Pendiente | Se retoma |
|---|---|
| `ILedgerQueries` e `ILedgerPosting` para otros módulos | Fase 4 en adelante, con su primer consumidor |
| Eventos `OperationPosted`, `OperationChanged`, `OperationVoided`, `AccountArchived` | Fase 5: Budgets es el primero que reacciona |
| `ETag` / `If-Match` en la API | Fase 8, con el primer agregado que editan varias personas |
| Totales convertidos a la moneda base | Fase 4, con el tipo de cambio de referencia |
| "Repetir cada mes" en el formulario de transacción | Fase 7 |
| "Dividir gasto" en el detalle del movimiento | Fase 9 |
| Concurrencia por `xmin` en cuentas y operaciones | Configurada; ningún test la provoca todavía |

## Resuelto de fases anteriores

| Qué | Quedó de | Se hizo en |
|---|---|---|
| Eliminar el módulo `Sample` | Fase 0 | Fase 3 |
| `ICategoryLookup` e `ITagLookup` | Fase 2 | Fase 3 |
| Crear un tag sin pasar por una categoría | Fase 2 | Fase 3: se crea al nombrarlo en una transacción |
| Selección de categoría y de tags en un formulario | Fase 2 | Fase 3, dentro del formulario de transacción |
