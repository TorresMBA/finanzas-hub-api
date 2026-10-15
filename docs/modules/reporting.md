# Módulo Reporting

| | |
|---|---|
| Responsabilidad | Dashboard, reportes por periodo, búsqueda global y exportación. No posee verdad financiera |
| Posee | Nada en MVP |
| Consulta de otros | Contratos de Ledger, Budgets, Goals, Scheduling, Investments, Categories |
| Eventos | Ninguno |

| Caso de uso | Autorización | Entrada | Efectos |
|---|---|---|---|
| GetDashboard (query) | Dueño | mes | KPI, flujo de 6 meses, top de categorías, recientes, cuentas destacadas, metas, próximos pagos |
| GetReportSummary (query) | Dueño | periodo y ancla | KPI con delta, dona, variación por categoría, comercios, tasa de ahorro |
| Search (query) | Dueño | texto | Transacciones, cuentas y metas propias |
| ExportReport (query) | Dueño | formato | Archivo XLSX o CSV |

## Estado tras la fase 8

Implementado: `GetDashboard`, en `GET W/dashboard?month=2026-10` (sin `month`, el mes actual del workspace). Devuelve, en la moneda base: patrimonio al cierre del mes y su variación contra el mes anterior, ingresos y gastos del mes y del anterior, tasa de ahorro, flujo de los seis meses que terminan en ese, las cinco categorías con más gasto (cada una con sus subcategorías sumadas), los cinco últimos movimientos las cuatro cuentas de mayor saldo y, si la persona tiene presupuestos, lo presupuestado y lo gastado contra ellos. Desde la fase 7, también los próximos pagos de los treinta días siguientes: gastos programados y tarjetas por vencer. Desde la fase 8, las tres metas abiertas más cerca de su objetivo. Los movimientos y las cuentas conservan su moneda.

El módulo no tiene tablas ni dominio. Lee `ILedgerQueries`, `IBudgetQueries`, `IScheduleQueries`, `IGoalQueries` e `ICategoryLookup`, que ya limitan todo a la persona de la petición, así que el Inicio de un miembro nunca muestra dinero de otro. Registra la métrica `reporting.dashboard.duration`.

Diferido hasta la fase 12: los reportes, la búsqueda y la exportación.

## Estado tras la fase 12

| | |
|---|---|
| Casos de uso | `GetReportSummary` (`W/reports/summary`), `ExportReport` (`W/reports/export`, Excel o CSV) y `Search` (`W/search`) |
| Periodo | `ReportPeriod`: mes, trimestre o año completos que contienen el mes ancla; se comparan con el anterior del mismo tamaño |
| Lee | `ILedgerQueries` (flujo, totales por categoría y, nuevos, `GetMerchantTotalsAsync`, `GetMovementsAsync`, `SearchMovementsAsync`, `SearchAccountsAsync`), `ICategoryLookup`, `IGoalQueries` |
| Escribe archivos con | `ISpreadsheets` (BuildingBlocks), implementado con ClosedXML en Infrastructure |
| Métricas | `reporting.report.duration` (por periodo), `reporting.exports` (por formato) |
| Privacidad | Nada se filtra aquí: las consultas del ledger ya se limitan a la persona; un miembro del mismo espacio ve sus propios reportes y su búsqueda no encuentra nada ajeno (test de integración) |
