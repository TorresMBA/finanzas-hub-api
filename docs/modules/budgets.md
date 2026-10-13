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
