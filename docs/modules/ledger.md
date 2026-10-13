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

## Estado tras la fase 3

Implementado: abrir, renombrar, archivar y restaurar cuentas de banco, efectivo y billetera; ajustar el saldo; registrar, editar, confirmar y anular ingresos y gastos, con sus etiquetas; y las consultas de cuentas, de un movimiento y del listado con filtros, totales por moneda y cursor. Endpoints: `GET` y `POST W/accounts`; `GET` y `PATCH W/accounts/{id}`; `POST W/accounts/{id}/archive`, `/restore` y `/adjust-balance`; `GET` y `POST W/transactions`; `GET` y `PATCH W/transactions/{id}`; `POST W/transactions/{id}/confirm` y `/void`.

Diferido: transferencias, tarjetas, tipo de cambio, `GetAccountSummary` e `ILedgerQueries` (fase 4); `ILedgerPosting` para otros módulos (fase 4 en adelante); los eventos del módulo (fase 5, con Budgets); `ETag` (fase 8).

- **No existe una columna de saldo.** El saldo de una cuenta es la suma de `signed_amount` de sus asientos efectivos, calculada en cada respuesta (`AccountBalances`). Un test falla si `Account` gana una propiedad con "Balance" en el nombre.
- El saldo inicial es una operación `OpeningBalance` y corregir un saldo es una operación `Adjustment` por la diferencia. Ninguna de las dos es ingreso ni gasto, y ninguna se edita: se anula y se registra de nuevo.
- Son efectivos los movimientos pendientes y los confirmados (OQ-05). Anular marca la operación y sus asientos como no efectivos; nada se borra.
- Editar reescribe el asiento en sitio (OQ-22): monto, cuenta, fecha, tipo y categoría. La auditoría guarda la versión anterior y la nueva. Un movimiento puede seguir en una cuenta archivada después, pero no moverse a una.
- La moneda de un movimiento es la de su cuenta (OQ-06), fija desde que se abre. Los montos tienen como máximo los decimales de la moneda; uno con más se rechaza, no se redondea.
- Los totales del listado se dan por moneda y cubren todo lo que cumple el filtro, no solo la página.
- El listado va de más nuevo a más antiguo, por fecha y luego por id (que es temporal). El cursor es la fecha y el id de la última fila, opaco para el cliente.
- La categoría y las etiquetas son de Categories: el ledger guarda sus ids y las valida y describe a través de `ICategoryLookup` e `ITagLookup`, que ya aplican el filtro de dueño. Una categoría ajena responde como si no existiera.
- Cuentas, operaciones y asientos son `IOwned`. El asiento repite el workspace, el dueño, la fecha y si es efectivo de su operación, para que el saldo sea una suma indexada sobre una sola tabla.
- Abrir cuenta, ajustar saldo y registrar transacción exigen `Idempotency-Key`. El frontend crea la clave al abrir el formulario, no al enviarlo.
- Las columnas `base_amount`, `fx_rate` y `fx_rate_date` de `entries` existen desde esta migración y nadie las escribe todavía.
