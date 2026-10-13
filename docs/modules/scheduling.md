# Módulo Scheduling

| | |
|---|---|
| Responsabilidad | Reglas recurrentes y pagos programados, ocurrencias, recordatorios y la proyección de calendario |
| Aggregate roots | `RecurringRule`, `Occurrence` |
| Value objects | `RecurrenceSchedule`, `Frequency`, `WeekendPolicy`, `PostingMode`, `ReminderOffset` |
| Servicios de dominio | `RecurrenceCalculator`, `CalendarProjection` |
| Posee | Esquema `scheduling` |
| Consulta de otros | `Ledger.Contracts` (registrar y leer movimientos, vencimientos de tarjeta), `Notifications.Contracts`, `Workspaces.Contracts` (reacciona a `MemberRemoved`), `Categories.Contracts` (reacciona a `CategoryArchived`) |
| Expone | `IRecurringRules` (crear regla con acción extensible), `IScheduledActionHandler` e `ICalendarItemProvider` (los implementan módulos aguas abajo) |
| Eventos | `ReminderDue` (D, publica notificación), `OccurrencePosted` (D), `OccurrenceFailed` (D, publica notificación) |

Qué es propio y qué se proyecta:

| Concepto | Dueño | En el calendario |
|---|---|---|
| Regla recurrente o pago programado | Scheduling | Ocurrencias futuras calculadas, no almacenadas |
| Ocurrencia registrada u omitida | Scheduling | Fila por `(regla, fecha)` |
| Recordatorio | Scheduling define el aviso, Notifications lo entrega | No es un ítem aparte |
| Movimiento ya registrado | Ledger | Leído por contrato |
| Vencimiento de tarjeta y cuota | Ledger | Leído por contrato |
| Fecha objetivo y aporte automático de meta | Goals | Vía `ICalendarItemProvider` e `IScheduledActionHandler` |
| Vencimiento de depósito a plazo | Investments | Vía `ICalendarItemProvider` |

| Caso de uso | Autorización | Entrada | Reglas | Efectos y eventos |
|---|---|---|---|---|
| CreateRecurringRule | Dueño | plantilla (transacción o transferencia), frecuencia, intervalo, inicio, fin, modo, recordatorio | Las cuentas y la categoría son propias; un gasto exige categoría | — |
| UpdateRule | Dueño | plantilla o calendario | No reescribe ocurrencias registradas; cambiar condiciones desde una fecha termina la regla y crea otra | — |
| PauseRule, ResumeRule, EndRule | Dueño | id | — | — |
| ConfirmOccurrence | Dueño | regla, fecha, monto real opcional | Única por `(regla, fecha)` | Registra en Ledger en la misma transacción |
| SkipOccurrence | Dueño | regla, fecha | Única por `(regla, fecha)` | — |
| RunDueOccurrences (Sistema) | Actúa como el dueño de la regla | — | Sin relleno retroactivo; si el dueño perdió acceso, pausa la regla y avisa | Registra las de modo automático; emite recordatorios deduplicados |
| GetCalendar, GetUpcoming (queries) | Dueño | rango, filtro | — | Composición de propios y proyectados; saldo proyectado |
