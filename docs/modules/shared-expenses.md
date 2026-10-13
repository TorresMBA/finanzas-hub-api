# Módulo SharedExpenses

| | |
|---|---|
| Responsabilidad | Grupos, gastos, reparto, liquidaciones, balances entre personas y actividad del grupo |
| Aggregate roots | `SharedGroup` (con `GroupMember`), `SharedExpense` (con `ExpenseShare`), `Settlement` (con asignaciones) |
| Value objects | `SplitType`, `SplitInput`, `ExpenseStatus`, `PairBalance` |
| Servicios de dominio | `ISplitStrategy` ×4, `DebtSimplifier` |
| Posee | Esquema `shared_expenses` |
| Consulta de otros | `Ledger.Contracts`, `Workspaces.Contracts`, `Notifications.Contracts` |
| Eventos | `SharedExpensePublished` (I), `SharedExpenseCancelled` (I), `SettlementRecorded` (I), `SharedExpenseSettled` (D) |

**Estrategias de reparto.** Se soportan las cuatro: `Equal`, `Percentage` (suma 100), `FixedAmount` (suma el total) y `Shares` (proporcional a partes). `Shares` se incluye porque V1 ya lo implementa y es el mismo cálculo proporcional que `Percentage`; la UI puede añadirlo después. El redondeo reparte los céntimos sobrantes de forma determinista para que la suma sea exacta.

**Estados.** `Draft` (editable, sin efecto) → `Pending` (publicado, partes por cobrar) → `PartiallyPaid` → `Settled`; `Cancelled` desde `Draft` o `Pending`, o tras anular los pagos. Los tres estados de cobro los mantiene el propio agregado al registrar o anular pagos. La parte de quien pagó nace pagada.

| Caso de uso | Autorización | Entrada | Reglas | Efectos y eventos |
|---|---|---|---|---|
| CreateGroup | Miembro | nombre, moneda, miembros | Miembros activos del workspace | El creador es Admin del grupo |
| AddGroupMember / RemoveGroupMember | Admin del grupo | usuario | No se quita a quien tiene saldo pendiente | — |
| ArchiveGroup | Admin del grupo | — | Todo liquidado | — |
| CreateExpense | Miembro del grupo | descripción, total, pagador, fecha, tipo de reparto, participantes y valores, transacción origen opcional, cuenta del pagador opcional | Pagador y participantes pertenecen al grupo; la suma de partes es el total; moneda = la del grupo | Nace `Draft` o se publica directo |
| PublishExpense | Creador o pagador | — | — | Si el pagador indicó cuenta y no hay transacción origen, registra su gasto en Ledger; `SharedExpensePublished` con la parte de cada uno |
| EditExpense | Creador o pagador | campos | Solo sin pagos registrados; con control de concurrencia | Recalcula partes |
| CancelExpense | Creador o pagador | motivo | Sin pagos vigentes | `SharedExpenseCancelled` |
| RecordSettlement | Deudor o acreedor implicado | contraparte, monto, partes a cubrir opcional, cuenta propia opcional, método, fecha | No supera lo adeudado; sin partes indicadas asigna de la más antigua a la más nueva | Asignaciones; estado de los gastos; si hay cuenta, asiento en el ledger de quien registra; `SettlementRecorded` |
| LinkSettlementToAccount | La otra parte | cuenta propia | Una sola vez por lado | Deudor: `Expense`. Acreedor: `Refund` contra la categoría original |
| VoidSettlement | Quien la registró | motivo | — | Revierte asignaciones y anula los asientos vinculados |
| RemindMember | Acreedor | miembro | Un recordatorio por día y par | Publica notificación, con WhatsApp si el destinatario lo tiene activo |
| GetGroups, GetGroupDetail, GetExpenses, GetSummary (queries) | Miembro del grupo / propio | — | — | Balances derivados y sugerencia de liquidación |
