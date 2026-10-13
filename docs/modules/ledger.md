# Módulo Ledger

| | |
|---|---|
| Responsabilidad | Cuentas, asientos, transferencias, tarjetas de crédito, saldos, patrimonio y tipos de cambio de referencia. Única fuente de verdad del dinero |
| Aggregate roots | `Account` (con `CreditCardProfile`), `Operation` (con `Entry` y tags), `InstallmentPlan` (Fase 14) |
| Value objects | `Money`, `Currency`, `OperationKind`, `OperationStatus`, `EntryDirection`, `OperationSource`, `AccountType`, `CreditCardCycle` |
| Servicios de dominio | `BalanceCalculator`, `CreditCardCycleCalculator`, `InstallmentSchedule` |
| Posee | Esquema `ledger` |
| Consulta de otros | `Categories.Contracts` (validar categoría y tags), `Notifications.Contracts` (aviso de movimiento grande y de vencimiento de tarjeta) |
| Expone | `ILedgerPosting` (registrar ingreso, gasto, reembolso y transferencia; anular; asegurar cuenta de sistema), `ILedgerQueries` (cuenta, saldos, patrimonio, gasto por categoría, flujo, movimientos, comercios, vencimientos de tarjeta) |
| Eventos | `OperationPosted` (I), `OperationChanged` (I), `OperationVoided` (I), `AccountArchived` (I), `AccountOpened` (D) |

Diseño completo en la sección K.

| Caso de uso | Autorización | Entrada | Reglas | Efectos y eventos |
|---|---|---|---|---|
| OpenAccount | Dueño | nombre, tipo, moneda, saldo inicial; en tarjeta: línea, día de pago, día de cierre opcional | Nombre único entre activas; la tarjeta exige línea > 0 | Asiento `OpeningBalance` si saldo ≠ 0 |
| UpdateAccount | Dueño | nombre, institución, favorita, datos de tarjeta | Moneda y tipo fijos si hay asientos | Auditoría |
| ArchiveAccount | Dueño | id | El saldo no tiene que ser cero; deja de ofrecerse | `AccountArchived` |
| AdjustAccountBalance | Dueño | saldo real, fecha | Calcula la diferencia contra el ledger | Asiento `Adjustment`; nunca sobrescribe |
| PostTransaction | Dueño | tipo ingreso o gasto, cuenta, monto, categoría, fecha, descripción, comercio, tags, nota | Monto > 0; cuenta activa y propia; el tipo de la categoría coincide; moneda = la de la cuenta | `OperationPosted`; idempotente por clave |
| PostTransfer | Dueño | origen, destino, monto origen, monto destino si la moneda difiere, fecha, comisión | Cuentas distintas, propias y activas; misma moneda implica mismo monto | Una operación con dos asientos; la comisión es un gasto vinculado |
| PayCreditCard | Dueño | tarjeta, cuenta origen, monto, fecha | El destino es tarjeta; el origen no | Transferencia con `source = cards/payment` |
| EditTransaction | Dueño | campos | Descriptivos: libres. Monetarios: prohibidos si viene de otro módulo o está conciliada | Antes y después en auditoría; `OperationChanged` |
| ConfirmTransaction | Dueño | id | Solo desde Pendiente | — |
| VoidTransaction | Dueño | id, motivo | Si viene de otro módulo solo la anula ese módulo | Excluida de toda suma; `OperationVoided` |
| SetTransactionTags | Dueño | tags | Máximo 10 | — |
| GetAccounts, GetAccountSummary, GetTransactions, GetTransaction (queries) | Dueño | filtros, cursor | — | Saldos derivados |
