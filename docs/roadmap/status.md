# Estado de las fases

Tablero de avance. Se actualiza al empezar y al terminar cada fase. El alcance de cada una está en [phases.md](phases.md), junto con lo que se verificó al cerrarla; aquí solo va en qué punto está y qué quedó pendiente.

Última actualización: 13-oct-2026.

| Fase | Contenido | Estado | Cerrada |
|---|---|---|---|
| 0 | Fundación: plataforma, pipeline de comandos, shell del frontend | ✅ Completada | 13-oct-2026 |
| 1 | Identity y Workspaces | ✅ Completada | 13-oct-2026 |
| 2 | Categories | ✅ Completada | 13-oct-2026 |
| 3 | Ledger I: cuentas y transacciones | ⏳ Pendiente (siguiente) | — |
| 4 | Ledger II: transferencias, tarjetas básicas, patrimonio e Inicio | ⏳ Pendiente | — |
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

13-oct-2026, al cerrar la fase 2.

| Qué | Resultado |
|---|---|
| Backend: build | 0 errores, 0 advertencias |
| Backend: tests | 151 en verde (73 unitarios, 19 de arquitectura, 59 de integración) |
| Frontend: build, tests y lint | Build correcto, 58 tests, lint limpio |
| E2E en Chrome | 13 en verde |

## Pendientes que dejaron las fases cerradas

Lo que una fase cerrada no hizo y sigue debiéndose. Al retomar un punto, se borra de aquí.

### Fase 0

| Pendiente | Se retoma |
|---|---|
| Eliminar el módulo `Sample` | Fase 3, cuando el ledger aporte un comando idempotente y una entidad privada a los tests de la plataforma |
| Todo lo que necesita Docker (`docker compose`, Testcontainers) | Sin verificar en esta máquina, que no tiene Docker |

### Fase 1

| Pendiente | Se retoma |
|---|---|
| Cambiar la moneda base de un workspace | Fase 3: necesita el ledger |
| Archivar un workspace | Sin fecha |
| Foto de perfil | Sin fecha |
| `ETag` / `If-Match` en la API | Fase 3, con el ledger |
| Inicio de sesión con Google o Microsoft (OQ-25) | Backlog diferido |
| Envío por SMTP real y respuesta 429 del límite de `/auth` | Sin verificar; se prueban al desplegar |

### Fase 2

| Pendiente | Se retoma |
|---|---|
| `ICategoryLookup` e `ITagLookup` para otros módulos | Fase 3: el ledger es su primer consumidor |
| Crear un tag suelto (`POST W/tags`) y renombrarlo | Fase 3, con el formulario de transacción |
| Selectores `category-select` y `tag-chips` | Fase 3, con el formulario de transacción |
| Monto y número de movimientos por categoría en la pantalla | Fase 3, cuando existan transacciones |
| Mover una categoría a otro padre | Sin fecha: el mock no lo pide |
| Evento `CategoryArchived` | Fase 7: lo usa Scheduling |
