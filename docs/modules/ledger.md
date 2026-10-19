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
| Estado | Fases 3 y 4 implementadas; ver abajo |

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

## Estado tras la fase 4

Implementado: abrir, cambiar, archivar y restaurar cuentas de banco, efectivo, billetera y tarjeta de crédito; ajustar el saldo; registrar, editar, confirmar y anular ingresos y gastos, con sus etiquetas; transferir entre cuentas, también de monedas distintas; pagar una tarjeta; el patrimonio; el tipo de cambio de referencia; y las consultas de cuentas, de un movimiento, del listado con filtros, totales y cursor, y de lo que movió cada categoría. Endpoints: `GET` y `POST W/accounts`; `GET W/accounts/summary`; `GET` y `PATCH W/accounts/{id}`; `POST W/accounts/{id}/archive`, `/restore`, `/adjust-balance` y `/payments`; `GET` y `POST W/transactions`; `GET W/transactions/category-totals`; `GET` y `PATCH W/transactions/{id}`; `POST W/transactions/{id}/confirm` y `/void`; `POST W/transfers`; `GET W/exchange-rates` y `PUT W/exchange-rates/{moneda}`.

Expone `ILedgerQueries` (patrimonio a una fecha, flujo por mes, totales por categoría, saldos de las cuentas y últimos movimientos), que usa Reporting. Todo lo que responde es de la persona de la petición: no acepta un dueño como parámetro.

Desde la fase 7 existe `ILedgerPosting` (ingreso o gasto), que usa Scheduling. Desde la fase 8 también abre el fondo de una meta (cuenta `GoalFund`, fuera del patrimonio) y mueve dinero entre una cuenta de la persona y ese fondo; una cuenta `GoalFund` no admite movimientos hechos a mano.

Diferido: el evento `AccountArchived` (con su primer consumidor); el aviso de vencimiento de tarjeta (fase 6); `ETag` (fase 8); la comisión de una transferencia; estado de cuenta, cuotas y tipo de cambio automático (fase 14).

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
- **Transferencia.** Una operación `Transfer` con un asiento de salida y uno de entrada, entre dos cuentas de la misma persona. No es ingreso ni gasto: no entra en ningún total ni tiene categoría. Se lista como una fila; si el listado está filtrado por una cuenta, se cuenta desde ella (entrada o salida). Anularla saca los dos asientos. No se edita.
- Entre dos monedas la persona indica los dos montos; nada se convierte solo. Entre cuentas de la misma moneda, lo que llega es lo que sale.
- **Tarjeta de crédito.** Una cuenta de tipo `CreditCard` con un `CreditCardProfile` (línea, día de pago, día de cierre opcional) en `credit_card_profiles`. La deuda es su saldo, negativo; el disponible es la línea más el saldo. La línea no bloquea una compra. La fecha del próximo pago se calcula del día de pago; en un mes más corto cae en su último día.
- Pagar una tarjeta es una transferencia hacia ella desde una cuenta que no sea otra tarjeta. No lleva `source`: se reconoce por su destino, y así se puede anular como cualquier movimiento.
- **Patrimonio (OQ-26).** Lo que tienen menos lo que deben las cuentas que cuentan: las que no están archivadas y conservan `include_in_net_worth`. Un saldo positivo es algo que se tiene y uno negativo algo que se debe, sea cual sea el tipo de cuenta. Lo calcula un solo lugar, `NetWorthReader`.
- **Tipo de cambio.** Cada persona lleva los suyos hacia la moneda base del workspace: una fila por moneda y día en `exchange_rates`, que vale desde ese día. Sirve para valorar; no mueve dinero.
- **Valor en moneda base.** Al registrar o editar una operación, cada asiento en otra moneda guarda `base_amount`, `fx_rate` y `fx_rate_date` con el tipo de cambio que regía en su fecha, para que el total de un mes pasado no cambie cuando cambia el tipo. Un asiento sin tipo de cambio para su fecha queda sin valor propio y se valora al vigente. Los saldos y el patrimonio se valoran siempre al vigente.
- Una moneda sin tipo de cambio queda fuera de todo total en moneda base, y la respuesta dice cuál es (`missingRates`): un total nunca finge estar completo.

### Divisas en Transacciones (18-oct-2026)

- **Catálogo de monedas.** `Currency` (BuildingBlocks) es la única lista: código, decimales, símbolo y nombre. `GET /api/v1/currencies` la publica y el web la usa para escribir montos (`useCurrencies`) y para elegir moneda en Cuentas y en la configuración inicial. Añadir una moneda de 0 a 2 decimales es añadir una línea ahí; más decimales, cripto como divisa, más decimales en los tipos de cambio y la fuente automática siguen pendientes (fase 14).
- **Filtro por moneda.** `GET W/transactions?currency=USD` trae los movimientos sobre cuentas de esa moneda; una transferencia entra por cualquiera de sus lados. Cada fila de `totals` trae `baseNet`: su neto en la moneda base, cada monto a lo que valía el día que se registró, o null si falta algún tipo.
- **Conversión en la respuesta.** `TransactionResponse.conversion` dice lo que el asiento valía en la moneda base al registrarse (`baseAmount`, `rate`, `rateDate`, `isManual`). Una transferencia entre monedas trae `transfer.marketRate`: lo que valía una unidad del destino en la moneda del origen ese día, por los tipos de la persona, para ver lo que costó el cambio.
- **Precio en otra moneda.** Un ingreso o un gasto puede guardar `originalAmount` y `originalCurrency` (Netflix: US$ 15.49 cobrado como S/ 58.71). Es solo una referencia: lo que cuenta es lo que salió de la cuenta, en su moneda. La respuesta lo devuelve en `originalPrice`, con el tipo de mercado de ese día para mostrar el recargo del banco.
- **Tipo de cambio manual.** `fxRate` al registrar o editar valora el asiento con ese tipo en vez del de la tabla (`operations.manual_fx_rate`). Solo para una cuenta en otra moneda que la base. Editar sin él vuelve al de la tabla.
- **Comisión de una transferencia.** `fee` en `POST W/transfers` registra la comisión que el banco cobró aparte como gasto de la cuenta de origen en «Comisiones» (la categoría se crea si falta), atribuido a la transferencia (`source_module = ledger`, `source_type = transfer_fee`): no se edita ni se anula sola, y anular la transferencia la anula. Lo que costó el cambio en sí no es un gasto: ya está en los montos, y registrarlo aparte bajaría un saldo que en el banco no bajó.

Desde el ADR-021 las cuentas, operaciones y tipos de cambio son de la persona y viven en su espacio personal: `LedgerScope` y `ExchangeRateBook` leen `IPersonalSpace`. La moneda base de sus cifras es la de ese espacio.

Desde la fase 9 el ledger registra reembolsos (`OperationKind.Refund`, por `ILedgerPosting.PostRefundAsync`): dinero que entra a una cuenta por un gasto de una categoría. No es un ingreso: los reportes lo cuentan como gasto en negativo, así que resta de la categoría y de su presupuesto.
