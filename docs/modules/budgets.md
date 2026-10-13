# Módulo Budgets

| | |
|---|---|
| Responsabilidad | Límites de gasto por categoría y periodo, umbrales de aviso y cálculo de ritmo |
| Aggregate roots | `Budget` |
| Value objects | `BudgetPeriod`, `AlertThreshold`, `BudgetStatus`, `Pace` |
| Servicios de dominio | `BudgetProgressCalculator` (función pura) |
| Posee | Esquema `budgets`: presupuestos versionados por vigencia y registro de alertas emitidas |
| Consulta de otros | `Ledger.Contracts` (gasto por categoría y movimientos), `Categories.Contracts`, `Notifications.Contracts` |
| Eventos | `BudgetThresholdReached` (D, publica notificación) |

**Qué entra en el consumo.** Asientos no anulados, del dueño, con fecha dentro del periodo, en la categoría o sus subcategorías: suman `Expense`, restan `Refund`. No entran `Transfer`, `Adjustment`, `OpeningBalance` ni `Income`; por tanto quedan fuera los aportes a metas, los movimientos de inversión y los pagos de tarjeta. La compra con tarjeta sí entra, en la fecha de compra. El consumo nunca se almacena.

| Caso de uso | Autorización | Entrada | Reglas | Efectos y eventos |
|---|---|---|---|---|
| CreateBudget | Dueño | categoría raíz de gasto, monto, umbral, acumular, fijo | Uno vigente por categoría; moneda = base del workspace | — |
| AdjustBudgetLimit | Dueño | monto, vigencia | Crea una versión desde el mes indicado; no reescribe meses cerrados | — |
| EndBudget | Dueño | id | Fija `ends_on` | — |
| EvaluateThresholds (Sistema, consume `OperationPosted`, `OperationChanged` y `OperationVoided`) | — | dueño, categoría, fecha | Una alerta por `(presupuesto, periodo, umbral)` | Publica notificación |
| GetBudgets, GetBudgetDetail (queries) | Dueño | mes | — | Resumen, filas con estado, movimientos y gasto diario |

## Estado tras la fase 5

Implementado: `GetBudgets` (`GET W/budgets?month=2026-10`), `GetBudgetDetail` (`GET W/budgets/{id}?month=`), `CreateBudget` (`POST`), `AdjustBudgetLimit` (`PATCH`) y `EndBudget` (`DELETE`). Un presupuesto es una fila por vigencia: ajustar el límite en un mes posterior cierra la fila y abre otra, así que el id cambia y los meses pasados conservan su límite. Crear y ajustar valen siempre desde el mes actual del workspace.

El consumo se pide a `ILedgerQueries` en cada lectura y nunca se guarda. Cuentan los gastos no anulados del mes en la categoría y sus subcategorías, en la moneda base. `BudgetProgress` y `MonthPace` son el cálculo puro del estado y del ritmo. `IBudgetQueries` da a Reporting el total del mes para el Inicio.

Diferido: `EvaluateThresholds`, `budget_alerts` y el evento `BudgetThresholdReached` (fase 6); que el reembolso reste, cuando el ledger lo registre; presupuesto semanal y acumulación del sobrante. Las diferencias con el diseño están en [phases.md](../roadmap/phases.md).
