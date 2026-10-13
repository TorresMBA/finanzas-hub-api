# Estado de las fases

Tablero de avance. Se actualiza al empezar y al terminar cada fase. El alcance de cada una está en [phases.md](phases.md), junto con lo que se verificó al cerrarla; aquí solo va en qué punto está y qué quedó pendiente.

Última actualización: 13-oct-2026.

| Fase | Contenido | Estado | Cerrada |
|---|---|---|---|
| 0 | Fundación: plataforma, pipeline de comandos, shell del frontend | ✅ Completada | 13-oct-2026 |
| 1 | Identity y Workspaces | ✅ Completada | 13-oct-2026 |
| 2 | Categories | ✅ Completada | 13-oct-2026 |
| 3 | Ledger I: cuentas y transacciones | ✅ Completada | 13-oct-2026 |
| 4 | Ledger II: transferencias, tarjetas básicas, patrimonio e Inicio | ✅ Completada | 13-oct-2026 |
| 5 | Budgets | ✅ Completada | 13-oct-2026 |
| 6 | Notifications y tiempo real | ✅ Completada | 13-oct-2026 |
| 7 | Scheduling y Calendario | ✅ Completada | 13-oct-2026 |
| 8 | Goals | ⏳ Pendiente (siguiente) | — |
| 9 | SharedExpenses | ⏳ Pendiente | — |
| 10 | Integrations: importación y reglas | ⏳ Pendiente | — |
| 11 | WhatsApp | ⏳ Pendiente | — |
| 12 | Reporting | ⏳ Pendiente | — |
| 13 | Investments | ⏳ Pendiente | — |
| 14 | Tarjetas avanzadas y FX | ⏳ Pendiente | — |

Estados: ⏳ pendiente, 🚧 en curso, ✅ completada.

## Última verificación

13-oct-2026, al cerrar la fase 7.

| Qué | Resultado |
|---|---|
| Backend: build | 0 errores, 0 advertencias |
| Backend: tests | 240 en verde (138 unitarios, 19 de arquitectura, 83 de integración) |
| Frontend: build, tests y lint | Build correcto y sin avisos, 122 tests, lint limpio |
| E2E en Chrome | 24 en verde |

## Pendientes que dejaron las fases cerradas

Lo que una fase cerrada no hizo y sigue debiéndose. Al retomar un punto, se borra de aquí.

### Fase 0

| Pendiente | Se retoma |
|---|---|
| Todo lo que necesita Docker (`docker compose`, Testcontainers) | Sin verificar en esta máquina, que no tiene Docker |

### Fase 1

| Pendiente | Se retoma |
|---|---|
| Cambiar la moneda base de un workspace | Sin fecha. Con el ledger en marcha ya no es solo un dato: cada movimiento en otra moneda guarda lo que valía en la moneda base, y cambiarla obligaría a revalorarlos todos, los de cada miembro |
| Archivar un workspace | Sin fecha |
| Foto de perfil | Sin fecha |
| Inicio de sesión con Google o Microsoft (OQ-25) | Backlog diferido |
| Envío por SMTP real y respuesta 429 del límite de `/auth` | Sin verificar; se prueban al desplegar |

### Fase 2

| Pendiente | Se retoma |
|---|---|
| Renombrar un tag | Sin fecha |
| Mover una categoría a otro padre | Sin fecha: el mock no lo pide |
| Evento `CategoryArchived` | Sin fecha. Scheduling no lo necesitó: una regla cuya categoría se archivó falla al registrarse, queda en pausa y avisa |

### Fase 3

| Pendiente | Se retoma |
|---|---|
| Evento `AccountArchived` | Con su primer consumidor |
| `ETag` / `If-Match` en la API | Fase 8, con el primer agregado que editan varias personas |
| "Dividir gasto" en el detalle del movimiento | Fase 9 |
| Concurrencia por `xmin` en cuentas y operaciones | Configurada; ningún test la provoca todavía |

### Fase 4

| Pendiente | Se retoma |
|---|---|
| Comisión de una transferencia como gasto vinculado | Sin fecha. Hoy se registra aparte, como un gasto más |
| Editar una transferencia | Sin fecha. Se anula y se registra de nuevo |
| Marca de "cuenta favorita" para las cuentas del Inicio (OQ-24) | Sin fecha. El Inicio muestra las cuatro de mayor saldo |
| Metas en el Inicio | Fase 8 |
| Historial de tipos de cambio en pantalla | Fase 14. Se guarda una fila por día; la pantalla solo muestra y cambia el vigente |
| Cuentas de meta (`GoalFund`) fuera del patrimonio por defecto | Fase 8. La casilla "Incluir en mi patrimonio" ya existe en toda cuenta; Goals abrirá las suyas con ella desmarcada |

### Fase 5

| Pendiente | Se retoma |
|---|---|
| El reembolso resta del consumo | Cuando el ledger permita registrar un reembolso |
| Elegir desde qué mes vale un ajuste | Sin fecha. Hoy vale desde el mes actual |
| Presupuesto semanal y acumular lo no gastado | Sin fecha (OQ-17) |
| Sugerir el límite con el promedio de 3 meses | Sin fecha |
| "Ver movimientos" abre Transacciones ya filtrado por la categoría | Sin fecha. Transacciones no lee filtros de la URL todavía |
| Span del cálculo del presupuesto | Sin fecha |
| Tablet y tema claro de la pantalla de Presupuestos | Sin revisar a ojo; se revisó escritorio y móvil en tema oscuro |

### Fase 6

| Pendiente | Se retoma |
|---|---|
| Test de que el hub solo entrega al destinatario | Sin fecha. La señal se dirige por el id de la persona de la cookie y el E2E prueba que llega a su dueño; ningún test prueba que no llegue a otro |
| Badge de avisos en móvil | Sin fecha. En móvil la campana no está en la barra superior; el aviso emergente sí aparece |
| Filtros Alertas y Hogar, y paginación de la bandeja | Cuando haya más categorías. La bandeja muestra los 100 últimos |
| Purga de avisos a los 90 días | Sin fecha |
| Desactivar la bandeja por categoría | Sin fecha. Todo aviso llega a la bandeja; la preferencia decide solo el correo |
| Métrica de envíos pendientes | Sin fecha. Se cuenta lo publicado (`notifications.published`) |
| WhatsApp, push móvil, resumen semanal, "Crear alerta" | Fase 11 y backlog |
| Tablet y tema claro de la pantalla de Notificaciones | Sin revisar a ojo |

### Fase 7

| Pendiente | Se retoma |
|---|---|
| Editar una regla | Sin fecha. Se termina y se programa otra |
| Programar una transferencia o un pago de tarjeta | Sin fecha. Una regla es un ingreso o un gasto |
| Frecuencias semanal y quincenal, intervalos y política de fin de semana | Sin fecha. Hay una vez, cada mes y cada año |
| `IScheduledActionHandler` e `ICalendarItemProvider` | Fase 8, con su primer consumidor: el aporte automático a una meta |
| Pausar las reglas de quien pierde el acceso y avisarle | Sin fecha. Hoy esas reglas simplemente dejan de correr, y vuelven a correr si la persona recupera el acceso |
| Ver el job del Worker registrar un movimiento | Sin verificar. El test corre el mismo caso de uso que el job, no la clase del job; en el E2E el Worker arrancó sin errores pero no tenía nada automático que registrar |
| Métrica de la duración del ciclo | Sin fecha. Se cuenta lo registrado (`scheduler.occurrences`) |
| Tablet y tema claro del Calendario | Sin revisar a ojo |

## Resuelto de fases anteriores

| Qué | Quedó de | Se hizo en |
|---|---|---|
| Eliminar el módulo `Sample` | Fase 0 | Fase 3 |
| `ICategoryLookup` e `ITagLookup` | Fase 2 | Fase 3 |
| Crear un tag sin pasar por una categoría | Fase 2 | Fase 3: se crea al nombrarlo en una transacción |
| Selección de categoría y de tags en un formulario | Fase 2 | Fase 3, dentro del formulario de transacción |
| Monto por categoría en la pantalla de Categorías | Fase 2 | Fase 4: cada categoría dice lo que movió en el mes de la barra superior |
| Aviso al llegar al umbral del presupuesto y eventos del ledger | Fases 3 y 5 | Fase 6: `LedgerOperationChanged` y `EvaluateBudgetThresholds` |
| `ILedgerPosting` para otros módulos | Fase 3 | Fase 7: Scheduling registra por él |
| "Repetir cada mes" en el formulario de transacción | Fase 3 | Fase 7 |
| Aviso de vencimiento de tarjeta y próximos pagos en el Inicio | Fase 4 | Fase 7 |
| Presupuesto en el Inicio | Fase 4 | Fase 5: una cifra con el porcentaje usado |
| `ILedgerQueries` para otros módulos | Fase 3 | Fase 4: lo usa Reporting para el Inicio |
| Totales convertidos a la moneda base | Fase 3 | Fase 4, con el tipo de cambio de referencia |
