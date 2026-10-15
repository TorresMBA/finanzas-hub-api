# Módulo Investments

| | |
|---|---|
| Responsabilidad | Instrumentos, posiciones, operaciones de inversión, precios y valuación |
| Aggregate roots | `Asset`, `Holding` (con `InvestmentTransaction`), `PriceSnapshot` |
| Value objects | `AssetType`, `Quantity`, `UnitPrice`, `TermDepositTerms` (TEA, plazo, vencimiento), `HoldingValuation` |
| Posee | Esquema `investments` |
| Consulta de otros | `Ledger.Contracts` (movimiento de efectivo y tipo de cambio), `Categories.Contracts` (la categoría «Inversiones» de lo realizado), `Scheduling.Contracts` (vencimientos), `Notifications.Contracts` |
| Expone | `IPortfolioValuation` (valor de mercado por dueño, para patrimonio y reportes) |
| Eventos | `TermDepositMaturing` (D, publica notificación) |

Distinción pedida: `Asset` es el instrumento (ticker, fondo, producto de depósito); `Holding` es la posición de un usuario en un activo dentro de una institución; la cuenta de inversión es una cuenta de sistema en Ledger que refleja el capital neto aportado; `InvestmentTransaction` es cada operación; `PriceSnapshot` es el precio a una fecha.

| Caso de uso | Autorización | Entrada | Reglas | Efectos y eventos |
|---|---|---|---|---|
| OpenHolding | Dueño | tipo, activo o nombre, institución, monto, cantidad, fecha, cuenta origen, términos de depósito | — | Transferencia en Ledger hacia la cuenta de inversión |
| Contribute / Withdraw | Dueño | monto, cantidad, cuenta | No retirar más que la posición | Transferencia; la ganancia realizada se registra como ingreso |
| RecordDividend, RecordFee | Dueño | monto | — | Ingreso o gasto en Ledger |
| RecordValuation | Dueño | valor o precio, fecha | Para fondos y depósitos sin precio de mercado | — |
| RefreshPrices (Sistema, futuro) | — | — | — | `PriceSnapshot` |
| GetPortfolio, GetHolding (queries) | Dueño | tipo | — | Valor, ganancia no realizada, distribución, serie de 12 meses |

## Estado tras la fase 13

| | |
|---|---|
| Dominio | `Holding` (`IOwned`) con `HoldingMovement` (aporte o rescate, con su capital y lo realizado), `HoldingValuation` y `TermDepositTerms` (TEA, días, vencimiento). El instrumento va en la posición: no hay `Asset` ni `PriceSnapshot` aparte |
| Casos de uso | `OpenHolding`, `ContributeToHolding`, `WithdrawFromHolding` (idempotentes), `RecordValuation`, `GetInvestmentSummary`, `GetHolding` |
| Ledger | `ILedgerPosting.OpenInvestmentAccountAsync` y `MoveInvestmentMoneyAsync`; lo realizado, con `PostTransactionAsync` en la categoría «Inversiones» (`ICategoryDirectory.EnsureAsync`) |
| Expone | `IPortfolioValuation` (lo usa el Inicio) |
| Ciclo diario | `DepositMaturityStep : IScheduleCycleStep`: aviso a 7 días del vencimiento, con clave `deposit-maturing:{id}:{fecha}` |
| API | `W/investments/summary`, `W/holdings` (POST), `W/holdings/{id}` y `/contributions`, `/withdrawals`, `/valuations` |
| Esquema | `investments`: `holdings`, `holding_movements`, `holding_valuations` (migración `AddInvestments`) |
| Métricas | `investments.operations` por tipo |
