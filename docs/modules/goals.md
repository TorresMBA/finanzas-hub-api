# Módulo Goals

| | |
|---|---|
| Responsabilidad | Metas personales y compartidas, participantes, aportes y retiros, progreso y proyección |
| Aggregate roots | `Goal` (con `GoalParticipant`), `GoalContribution` |
| Value objects | `GoalStatus`, `ParticipantRole`, `ContributionType`, `GoalProgress`, `GoalHealth` |
| Servicios de dominio | `GoalProjection` (función pura) |
| Posee | Esquema `goals` |
| Consulta de otros | `Ledger.Contracts`, `Workspaces.Contracts`, `Scheduling.Contracts`, `Notifications.Contracts` |
| Eventos | `GoalContributionAdded` (I), `GoalTargetReached` (I), `GoalParticipantAdded` (I), `GoalCancelled` (I) |

Una meta personal es una meta con un solo participante. No hay dos modelos.

| Caso de uso | Autorización | Entrada | Reglas | Efectos y eventos |
|---|---|---|---|---|
| CreateGoal | Miembro | nombre, objetivo, moneda, fecha, cuenta vinculada opcional, participantes | Objetivo > 0; los participantes son miembros activos | El creador es Owner de la meta; `GoalParticipantAdded` |
| UpdateGoal | Owner de la meta | nombre, objetivo, fecha | Con control de concurrencia | Recalcula estado |
| AddParticipant / RemoveParticipant | Owner de la meta | usuario | Miembro activo; no se quita a quien tiene aportes: pasa a `Left` | — |
| LeaveGoal | Participante | — | El Owner debe transferir antes | — |
| ContributeToGoal | Participante | monto, cuenta origen, fecha | Cuenta propia y activa; moneda = la de la meta | Aporte y transferencia en Ledger en una transacción; `GoalContributionAdded`; `GoalTargetReached` si cruza el objetivo |
| WithdrawFromGoal | Participante, sobre lo suyo | monto, cuenta destino | No más que su saldo aportado | Transferencia inversa |
| VoidContribution | Autor del aporte | id | — | Anula aporte y operación |
| SetAutoContribution | Participante | monto, día, cuenta origen | — | Crea o actualiza regla en Scheduling |
| ArchiveGoal / CancelGoal | Owner de la meta | — | Cancelar no mueve dinero; cada uno retira lo suyo | `GoalCancelled` |
| GetGoals, GetGoalDetail (queries) | Participante | estado | El detalle muestra aportes de todos, nunca la cuenta origen ajena | — |
