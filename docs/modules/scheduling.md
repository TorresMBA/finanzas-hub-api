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

## Estado tras la fase 7

Implementado: `CreateRecurringRule` (`POST W/recurring-rules`), pausar, reanudar y terminar (`POST W/recurring-rules/{id}/pause|resume|end`), `ConfirmOccurrence` y `SkipOccurrence` (`POST W/recurring-rules/{id}/occurrences/{fecha}/confirm|skip`), `ListRecurringRules`, `GetCalendar` (`GET W/calendar?from&to`, 92 días como máximo) y `RunSchedule`, el ciclo que el Worker corre cada 15 minutos como cada persona en cada uno de sus workspaces (`RunDueOccurrencesJob`). `IScheduleQueries` da a Reporting los próximos pagos del Inicio.

Una regla es un ingreso o un gasto, una vez, cada mes o cada año, automática o con confirmación, con recordatorio opcional. Sus ocurrencias son una colección propia con clave `(rule_id, scheduled_on)`: solo existe la fila de lo registrado u omitido. El registro pasa por `ILedgerPosting` con la clave `scheduling:{regla}:{fecha}`.

Diferido: editar una regla, transferencias programadas, más frecuencias, `IScheduledActionHandler` e `ICalendarItemProvider` (fase 8). Las diferencias con el diseño están en [phases.md](../roadmap/phases.md).

Desde el ADR-021 las reglas viven en el espacio personal de su dueño y el ciclo corre una vez por persona, no una por membresía.
