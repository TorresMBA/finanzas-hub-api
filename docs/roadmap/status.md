# Estado de las fases

Tablero de avance. Se actualiza al empezar y al terminar cada fase. El alcance de cada una está en [phases.md](phases.md), junto con lo que se verificó al cerrarla; aquí solo va en qué punto está y qué quedó pendiente.

Última actualización: 15-oct-2026.

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
| 8 | Goals | ✅ Completada | 13-oct-2026 |
| 9 | SharedExpenses | ✅ Completada | 13-oct-2026 |
| 10 | Integrations: importación y reglas | ✅ Completada | 13-oct-2026 |
| 11 | WhatsApp | ✅ Completada | 14-oct-2026 |
| 12 | Reporting | ✅ Completada | 15-oct-2026 |
| 13 | Investments | ⏳ Pendiente (siguiente) | — |
| 14 | Tarjetas avanzadas y FX | ⏳ Pendiente | — |

Estados: ⏳ pendiente, 🚧 en curso, ✅ completada.

## Última verificación

15-oct-2026, al cerrar la fase 12.

| Qué | Resultado |
|---|---|
| Backend: build | 0 errores, 0 advertencias |
| Backend: tests | 278 en verde (166 unitarios, 19 de arquitectura, 93 de integración) |
| Frontend: build, tests y lint | Build correcto y sin avisos, 128 tests, lint limpio |
| E2E en Chrome | 30 en verde |

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
| Envío por SMTP real y respuesta 429 del límite de `/auth` | Sin verificar; se prueban al desplegar. Sin SMTP configurado los correos se escriben como `.eml` en `%TEMP%\personal-hub-mail`, y solo con el Worker en marcha. Invitar ya no depende de eso: desde el 13-oct-2026 la pantalla muestra el enlace de la invitación al crearla |

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
| `ETag` / `If-Match` en la API | Sin fecha. La meta lleva `xmin`: dos cambios simultáneos sobre la misma fila responden 409, pero la API no expone la versión |
| "Dividir gasto" en el detalle del movimiento | Sin fecha. La fase 9 no lo incluyó: un gasto compartido se registra desde Gastos compartidos, y quien pagó puede llevarlo a su cuenta ahí mismo |
| Concurrencia por `xmin` en cuentas y operaciones | Configurada; ningún test la provoca todavía |

### Fase 4

| Pendiente | Se retoma |
|---|---|
| Comisión de una transferencia como gasto vinculado | Sin fecha. Hoy se registra aparte, como un gasto más |
| Editar una transferencia | Sin fecha. Se anula y se registra de nuevo |
| Marca de "cuenta favorita" para las cuentas del Inicio (OQ-24) | Sin fecha. El Inicio muestra las cuatro de mayor saldo |
| Historial de tipos de cambio en pantalla | Fase 14. Se guarda una fila por día; la pantalla solo muestra y cambia el vigente |

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
| Push móvil, resumen semanal, "Crear alerta" | Backlog |
| Tablet y tema claro de la pantalla de Notificaciones | Sin revisar a ojo |

### Fase 7

| Pendiente | Se retoma |
|---|---|
| Editar una regla | Sin fecha. Se termina y se programa otra |
| Programar una transferencia o un pago de tarjeta | Sin fecha. Una regla es un ingreso o un gasto |
| Frecuencias semanal y quincenal, intervalos y política de fin de semana | Sin fecha. Hay una vez, cada mes y cada año |
| `IScheduledActionHandler` e `ICalendarItemProvider` | Con el aporte automático a una meta, que la fase 8 dejó pendiente |
| Pausar las reglas de quien pierde el acceso y avisarle | Sin fecha. Hoy esas reglas simplemente dejan de correr, y vuelven a correr si la persona recupera el acceso |
| Ver el job del Worker registrar un movimiento | Sin verificar. El test corre el mismo caso de uso que el job, no la clase del job; en el E2E el Worker arrancó sin errores pero no tenía nada automático que registrar |
| Métrica de la duración del ciclo | Sin fecha. Se cuenta lo registrado (`scheduler.occurrences`) |
| Tablet y tema claro del Calendario | Sin revisar a ojo |

### Fase 8

| Pendiente | Se retoma |
|---|---|
| Aporte automático mensual a una meta | Sin fecha. Necesita que Scheduling programe acciones de otros módulos (`IScheduledActionHandler`) |
| Fecha objetivo de la meta en el Calendario | Sin fecha, con `ICalendarItemProvider` |
| Cuenta de ahorro propia como destino del aporte ("cuenta vinculada") | Sin fecha. Hoy todo aporte va al fondo de la meta, una cuenta que la aplicación abre a nombre de quien aporta |
| Anular un aporte | Sin fecha. Un aporte equivocado se retira |
| Traspasar la propiedad de una meta | Sin fecha. Quien la creó no puede salir: la archiva o la cancela |
| Meta en otra moneda que la cuenta | Fuera de alcance: la cuenta debe tener la moneda de la meta |
| Test de dos aportes simultáneos | Sin fecha. Dos aportes a la vez no chocan (cada uno es una fila); que crucen el objetivo a la vez sí, y uno responde 409 |
| Ver la señal en tiempo real entre dos navegadores | Sin verificar. El aporte de otro participante envía `resource.changed`; ningún test lo observa |
| Tablet, móvil a ojo y tema claro de Metas | Solo se revisó escritorio en tema oscuro; en móvil se comprobó que no desborda |

### Fase 9

| Pendiente | Se retoma |
|---|---|
| Editar un gasto compartido | Sin fecha. Se cancela (si nadie pagó aún) y se registra de nuevo |
| Borrador de un gasto y publicación aparte | Sin fecha. Un gasto se registra ya publicado |
| Elegir a qué gastos va un pago | Sin fecha. Un pago cubre los gastos más antiguos primero |
| Anular el movimiento del otro lado al anular un pago o cancelar un gasto | Sin fecha. Quien anula se lleva su propio movimiento; al otro lado se le avisa para que anule el suyo, porque nadie escribe en el ledger de otro |
| Sugerencia de liquidación con menos pagos | Retirada el 13-oct-2026: cada miembro ve solo sus deudas, y la sugerencia exponía las de los demás |
| Resumen aparte (`W/shared-expenses/summary`) y gastos compartidos en el Inicio | Sin fecha. Los totales vienen en el listado de grupos |
| Refunds en la pantalla de Transacciones: registrar uno a mano | Sin fecha. Hoy un reembolso solo nace de un cobro de gastos compartidos |
| Tests de concurrencia y de la señal en tiempo real | Sin fecha |
| Tablet y tema claro de Gastos compartidos | Solo se revisó escritorio en tema oscuro; en móvil, que no desborda |

### ADR-021: finanzas privadas de la persona (13-oct-2026)

| Pendiente | Se retoma |
|---|---|
| Lo privado registrado antes dentro del espacio de otra persona | No se retoma: eran datos de prueba. Siguen en la base, pero no se ven porque no están en el espacio personal de su dueño |
| Un fondo de meta abierto antes en un espacio ajeno | No se retoma. Esa cuenta ya no se ve, así que ese aporte no se puede retirar |
| Libros propios de un espacio compartido ("cuentas del hogar") | No existen por decisión. Lo común se lleva con metas y, desde la fase 9, gastos compartidos |
| Una persona que ya no posee ningún espacio (traspasó el único que tenía antes de este cambio) | Sin fecha. La migración no le marca espacio personal y no vería sus finanzas |

## Resuelto de fases anteriores

| Qué | Quedó de | Se hizo en |
|---|---|---|
| Eliminar el módulo `Sample` | Fase 0 | Fase 3 |
| `ICategoryLookup` e `ITagLookup` | Fase 2 | Fase 3 |
| Crear un tag sin pasar por una categoría | Fase 2 | Fase 3: se crea al nombrarlo en una transacción |
| Selección de categoría y de tags en un formulario | Fase 2 | Fase 3, dentro del formulario de transacción |
| Monto por categoría en la pantalla de Categorías | Fase 2 | Fase 4: cada categoría dice lo que movió en el mes de la barra superior |
| Aviso al llegar al umbral del presupuesto y eventos del ledger | Fases 3 y 5 | Fase 6: `LedgerOperationChanged` y `EvaluateBudgetThresholds` |
| El reembolso resta del consumo de un presupuesto | Fase 5 | Fase 9: el ledger registra reembolsos y los reportes los cuentan como gasto en negativo |
| Cuentas de meta fuera del patrimonio por defecto y metas en el Inicio | Fase 4 | Fase 8 |
| `ILedgerPosting` para otros módulos | Fase 3 | Fase 7: Scheduling registra por él |
| "Repetir cada mes" en el formulario de transacción | Fase 3 | Fase 7 |
| Aviso de vencimiento de tarjeta y próximos pagos en el Inicio | Fase 4 | Fase 7 |
| Presupuesto en el Inicio | Fase 4 | Fase 5: una cifra con el porcentaje usado |
| `ILedgerQueries` para otros módulos | Fase 3 | Fase 4: lo usa Reporting para el Inicio |
| Totales convertidos a la moneda base | Fase 3 | Fase 4, con el tipo de cambio de referencia |
| Avisos por WhatsApp | Fase 6 | Fase 11: interruptor por categoría, junto al del correo |
| Recordatorio de deuda por WhatsApp | Fase 9 | Fase 11: el recordatorio es un aviso de Gastos compartidos y va por WhatsApp si la persona lo activó |
| Importar XLSX | Fase 10 | Fase 12: la primera hoja del libro se lee como la tabla de un CSV de banco |

### Fase 10

| Pendiente | Se retoma |
|---|---|
| Mapear a mano las columnas de un CSV | Sin fecha. Las columnas se reconocen por su nombre (fecha, descripción, monto o cargo y abono, categoría, moneda, en español o inglés); un banco con otros nombres da `imports.format_not_recognized` |
| Análisis en segundo plano (`ParseImportsJob`, `IFileStorage`) | No hizo falta: con 10 MB y 10 000 líneas como máximo el archivo se lee en la misma petición y no se guarda. Se retoma si un archivo tarda demasiado |
| Movimientos en otra moneda que la de su cuenta (Bluecoins) | Sin fecha. Quedan como «Otra moneda» y se excluyen; no se convierten con el tipo de cambio del archivo |
| Volver a aplicar las reglas a una importación en revisión | Sin fecha. Las reglas se aplican al leer el archivo; después se clasifica línea por línea |
| Formato real de la exportación de Bluecoins | Probado con las dos cabeceras conocidas (la de exportar y la de la plantilla de importación, con columnas numeradas). Sin verificar con un archivo real del dueño |
| Evento `ImportCommitted` y aviso al terminar | Sin fecha: la importación se confirma con la pantalla abierta |

### Fase 11

| Pendiente | Se retoma |
|---|---|
| Cuenta de WhatsApp Cloud API (Meta): número, token, App Secret y plantillas aprobadas | Cuando el dueño la cree. Sin token, los mensajes se escriben en una carpeta (`WhatsApp:OutboxDirectory`, por defecto `%TEMP%/personal-hub-whatsapp`) y en Development la pantalla muestra el código de verificación |
| Probar con Meta de verdad | Con la cuenta: envío de texto y de plantilla, el saludo del webhook (`hub.challenge`), la firma y los estados de entrega. Hoy se probaron con eventos firmados por los tests y a mano contra la API local |
| Registrar gastos por chat | Fuera de alcance (OQ-23): el canal solo consulta (`saldo`, `ayuda`) |
| Ver en la pantalla el estado de entrega de cada mensaje | Sin fecha. Se guarda (enviado, entregado, leído, fallido) y se mide (`whatsapp.deliveries`), pero no se muestra |
| Purga de `received_webhook_events` y de entregas antiguas | Sin fecha |
| Tablet y tema claro de la sección WhatsApp | Solo se revisó escritorio y móvil en tema oscuro |

### Fase 12

| Pendiente | Se retoma |
|---|---|
| Exportar en PDF | Sin fecha. Hay Excel (resumen y movimientos) y CSV (movimientos) |
| Reportes programados y resumen semanal | Backlog. El botón «Programar reporte» del mock no existe |
| Índice sobre el comercio normalizado | Sin fecha. Los comercios se agrupan en memoria con `MerchantName.KeyOf` sobre los gastos del periodo; basta para el volumen de una persona. Se añade una columna con índice si un año tarda |
| Meta de ahorro propia («Meta del hogar: 25 %» del mock) | Sin fecha. La gráfica muestra la tasa de cada mes sin una meta |
| Búsqueda de categorías, grupos compartidos y notas de metas | Sin fecha. Busca movimientos (descripción, comercio, nota), cuentas y metas abiertas |
| Abrir un movimiento desde la búsqueda | Sin fecha. Lleva a Transacciones filtradas por el texto; una cuenta, a sus movimientos |
| Tablet y tema claro de Reportes y del buscador | Solo se revisó escritorio y móvil en tema oscuro |
