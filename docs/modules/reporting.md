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

## Estado tras la fase 4

Implementado: `GetDashboard`, en `GET W/dashboard?month=2026-10` (sin `month`, el mes actual del workspace). Devuelve, en la moneda base: patrimonio al cierre del mes y su variación contra el mes anterior, ingresos y gastos del mes y del anterior, tasa de ahorro, flujo de los seis meses que terminan en ese, las cinco categorías con más gasto (cada una con sus subcategorías sumadas), los cinco últimos movimientos y las cuatro cuentas de mayor saldo. Los movimientos y las cuentas conservan su moneda.

El módulo no tiene tablas ni dominio. Lee `ILedgerQueries` e `ICategoryLookup`, que ya limitan todo a la persona de la petición, así que el Inicio de un miembro nunca muestra dinero de otro. Registra la métrica `reporting.dashboard.duration`.

Diferido: el porcentaje del presupuesto (fase 5), los próximos pagos (fase 7) y las metas (fase 8) del Inicio; los reportes, la búsqueda y la exportación (fase 12).
