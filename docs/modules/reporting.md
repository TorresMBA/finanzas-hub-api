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
