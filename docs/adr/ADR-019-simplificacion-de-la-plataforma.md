# ADR-019: Simplificación de la plataforma

- Estado: aceptado
- Fecha: 2026-10-12
- Sustituye a: ADR-003, ADR-006 y ADR-008; y en parte a ADR-001, ADR-002 y ADR-009

## Contexto

La fase 0 se construyó según los ADR 001 a 018 y quedó verificada. Al revisarla, el dueño del proyecto la consideró más compleja de lo necesario para una persona que desarrolla y unas 10 que usan: dos proyectos y un `DbContext` por módulo, una conexión compartida entre contextos, outbox con consumidores idempotentes y una bandeja genérica de webhooks.

Quiso conservar la división en módulos y recuperar la forma de la versión anterior: la API con todos los controllers ordenados por módulo y el flujo `controller / worker → application → domain` a la vista.

## Decisión

| Tema | Antes | Ahora |
|---|---|---|
| Módulo | Dos proyectos (`Modules.X` todo `internal` y `Modules.X.Contracts`) | Un proyecto con `Domain/`, `Application/` y `Contracts/`; tipos públicos salvo los handlers |
| Fronteras entre módulos | Compilador y tests | Tests de arquitectura |
| Entrada HTTP | Minimal APIs dentro de cada módulo | Controllers en `PersonalFinance.Api/Controllers/<Módulo>` |
| Procesos en segundo plano | Hosted services alojados por la API; Worker opcional | Jobs en `PersonalFinance.Worker/Jobs/<Módulo>`; la API no ejecuta ninguno |
| Persistencia | Un `DbContext` y un historial de migraciones por módulo, sobre una conexión compartida | Un `AppDbContext` en `PersonalFinance.Infrastructure`, un esquema por módulo, un historial |
| Acceso a datos de un módulo | Su propio `DbContext` | Su interfaz `IXDb`, que `AppDbContext` implementa |
| FKs entre esquemas | Prohibidas | Permitidas en base de datos, sin propiedad de navegación |
| Eventos | Dominio → mapeador → integración → outbox → despachador → inbox por consumidor | Evento de dominio entregado en proceso, dentro de la transacción del comando |
| Entrega diferida | Outbox genérico | Solo donde hay una llamada externa: cola de envíos de Notifications (fase 6) |
| Webhooks | Bandeja genérica con tabla, verificador y procesador | Un controller por proveedor que procesa en la petición (fase 11) |
| Composición | `IModule` y una lista compilada en los dos hosts | `AddInfrastructure()` |

No cambia: `Result` y errores estables, `Money`, filtro global por dueño (`IOwned`), los tres niveles de seguridad, un handler por caso de uso con decoradores, idempotencia por `Idempotency-Key`, auditoría en la misma transacción, `ProblemDetails`, OpenTelemetry y el contrato OpenAPI.

## Alternativas consideradas

- **Mantener la fase 0 como estaba.** Más robusta ante el crecimiento, pero con maquinaria que hay que entender cada vez que algo falla.
- **Mover solo los endpoints a controllers.** Poco cambio, y casi toda la complejidad seguía.
- **El mismo rediseño con los jobs alojados en la API**, para tener un solo proceso. Se descartó para ver el Worker separado, como en la versión anterior.

## Consecuencias

A favor:

- 20 proyectos al final del roadmap en lugar de 31.
- Una petición es una transacción sobre un contexto. Lo que pasa, pasa en la misma petición; un fallo se depura leyendo un stack trace.
- Una reacción entre módulos son dos piezas (evento y handler) en lugar de cinco.
- Un solo historial de migraciones.

En contra:

1. **Las fronteras entre módulos dependen de los tests de arquitectura.** Si se desactivan, nada impide que un módulo use las entidades de otro.
2. **`Infrastructure` referencia a todos los módulos.** Es aceptable porque es una capa exterior que ningún módulo referencia, pero crece con cada módulo.
3. **Una reacción que falla hace fallar el comando.** Un error en la evaluación de un presupuesto impide registrar el gasto hasta corregirlo.
4. **Dos procesos.** Un aviso creado por un job llega al navegador por el sondeo de respaldo, porque el hub de SignalR vive en la API.
5. **Un webhook depende de que el proveedor reintente** si el procesamiento falla.
6. **Extraer un módulo a un servicio propio costará más**: comparte contexto, historial de migraciones y, quizá, FKs.

## Cuándo reconsiderar

| Señal | Qué recuperar |
|---|---|
| Una reacción lenta o poco fiable empieza a bloquear comandos de usuario | Outbox para ese evento. Los módulos solo levantan eventos y escriben handlers, así que el cambio queda en Infrastructure |
| Un proveedor de webhooks no reintenta, o su procesamiento tarda más de lo que espera | Guardar el evento y procesarlo en un job |
| Más de un desarrollador, o violaciones repetidas de las fronteras | Proyecto `Contracts` separado y tipos `internal` |
| Un módulo necesita desplegarse o escalar por separado | `DbContext` e historial propios para ese módulo |
| Se quiere un solo contenedor | Mover los jobs a una biblioteca que también aloje la API |
