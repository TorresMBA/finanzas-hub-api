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

## Estado tras la fase 9

Implementado, todo bajo `W/shared-groups`: crear grupo, sumar y quitar miembros (o salir), archivar; registrar un gasto con cualquiera de los cuatro repartos y cancelarlo; registrar un pago, llevarlo a una cuenta propia (`link-account`) y anularlo; recordar a quien debe; listado con totales por moneda y detalle con saldos, deudas, sugerencia de liquidación, gastos y pagos.

`SharedGroup` contiene miembros, gastos y pagos. El filtro global de `AppDbContext` solo deja cargar un grupo del workspace actual en el que la persona actual es miembro activo: para cualquier otro no existe (404). El grupo no guarda dinero ni saldos: guarda quién pagó qué por quién y quién devolvió, y todo saldo se calcula de ahí.

Cada lado registra en su propio ledger, que desde el ADR-021 es el de su espacio personal: quien pagó, el gasto completo; el deudor, su pago como gasto; el acreedor, el cobro como `Refund` de la categoría original. Nadie escribe en el ledger de otro.

Diferido: borrador y edición de un gasto, elegir a qué gastos va un pago, "Dividir gasto" desde una transacción, WhatsApp. Las diferencias con el diseño están en [phases.md](../roadmap/phases.md).

### Ajustes del 13-oct-2026

- **Visibilidad dentro del grupo.** Ser miembro deja ver el grupo, no todo lo que hay en él: `SharedGroupReader` devuelve a cada quien solo los gastos, pagos y deudas en que participa.
- **Anular un pago** anula el movimiento propio en el ledger y avisa al otro lado. Solo dentro de un día, una vez por pago y tres al día por persona y grupo.
- **Desde el 18-oct-2026**, anular un pago o cancelar un gasto anula también el movimiento que el otro lado había registrado por él (`ILedgerPosting.VoidLinkedAsync`): el dinero no se movió, y dejarlo creaba saldo que no existía. Es la única excepción a que nadie escribe en el ledger de otro: solo anula, solo ese movimiento, y no devuelve nada de él.
- La sugerencia de liquidación se retiró.

### Pantalla

Lista de grupos (qué hay por saldar en cada uno) y detalle (con quién, y gasto a gasto quién pagó su parte). El resumen de cada grupo lo calcula el servidor para quien pregunta; la pantalla no suma deudas por su cuenta.
