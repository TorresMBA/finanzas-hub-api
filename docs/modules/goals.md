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
| ArchiveGoal / CancelGoal | Owner de la meta | — | Archivar no mueve dinero. Cancelar devuelve lo de cada participante a las cuentas de donde salió (desde el 18-oct-2026) | `GoalCancelled` |
| GetGoals, GetGoalDetail (queries) | Participante | estado | El detalle muestra aportes de todos, nunca la cuenta origen ajena | — |

## Estado tras la fase 8

Implementado: `CreateGoal`, `UpdateGoal`, `AddParticipant`, `RemoveParticipant` (que también sirve para salir), `ContributeToGoal` (`POST W/goals/{id}/contributions`), `WithdrawFromGoal` (`POST W/goals/{id}/withdrawals`), archivar y cancelar, `GetGoals` y `GetGoalDetail`. `IGoalQueries` da a Reporting las metas del Inicio.

`Goal` contiene a sus participantes y sus aportes. El filtro global de `AppDbContext` solo deja cargar una meta del workspace actual en la que la persona actual es participante activa: para cualquier otro, también el Owner del workspace, no existe (404). Dentro de la meta, lo que solo puede hacer quien la creó lo decide el agregado y responde 403.

El dinero de cada participante está en su propio fondo, una cuenta `GoalFund` del ledger que abre `ILedgerPosting.OpenGoalFundAsync` con su primer aporte, fuera del patrimonio. Aportar y retirar son transferencias entre una cuenta suya y ese fondo (`MoveGoalMoneyAsync`), con `source = goals`. Lo ahorrado en la meta es la suma de los aportes, nunca una columna.

Diferido: aporte automático, cuenta vinculada, anular un aporte, traspasar la propiedad. Las diferencias con el diseño están en [phases.md](../roadmap/phases.md).

### Ajustes del 18-oct-2026

- **El objetivo se respeta.** Un aporte es como máximo lo que falta (`goals.contribution.exceeds_target`, y `goals.contribution.target_reached` una vez alcanzada), y el objetivo no baja de lo ya ahorrado (`goals.target_below_saved`). Cada aporte toca la fila de la meta, así que dos aportes a la vez no pasan el objetivo juntos: el segundo responde 409.
- **Cancelar devuelve el dinero.** Lo que cada participante tiene en la meta vuelve a las cuentas de donde salió, empezando por el aporte más reciente (lo retirado o gastado salió de lo más antiguo). Es una transferencia del fondo a esa cuenta en el ledger de cada participante (`ILedgerPosting.ReturnGoalMoneyAsync`) y un retiro en la meta. Si esa cuenta está archivada, ese importe se queda en su fondo y se le avisa para que lo retire.
- **Anular el gasto pagado con la meta lo devuelve a la meta.** Al anular en Transacciones un gasto registrado con "Usar el dinero", `ReturnSpentMoneyToGoal` (reacción a `LedgerOperationChanged`) anula también el retiro del fondo: el dinero vuelve a la meta y lo que falta crece con él. En una meta cancelada no: ahí el dinero se queda en la cuenta. El retiro guarda el gasto que pagó (`expense_operation_id`); los anteriores a este cambio se enlazaron desde la auditoría en la migración.
