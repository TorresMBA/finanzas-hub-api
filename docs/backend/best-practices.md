# Buenas prácticas del backend

Cómo se escribe código en `finanzas-hub-api`. No son gustos: cada regla evita un problema concreto. Lo que ya comprueba el compilador o un test se indica con **[test]** o **[build]**.

## C# y estilo

- **Copia el estilo del código que tienes al lado**: nombres, densidad de comentarios, forma de los handlers. Hay muchos ejemplos de cada cosa.
- **Nullable activado y warnings como errores** **[build]** (`Directory.Build.props`). No silencies un aviso con `!` o `#pragma` sin entender por qué aparece.
- **`internal` por defecto.** Solo son `public` las entidades, los comandos, consultas y respuestas, el `IXDb` y los `Contracts` ([structure.md](structure.md#visibilidad)).
- **`sealed`** en las clases que no están pensadas para heredarse (casi todas).
- **`record`** para comandos, consultas, respuestas y eventos: inmutables y con igualdad por valor.
- **Constructores primarios** para la inyección: `internal sealed class XHandler(IXDb db, IClock clock) : ...`.
- **Comentarios que explican el porqué**, en inglés como el resto del código, en `/// <summary>` sobre los tipos públicos. El generador de [database.md](database.md) usa esos comentarios para describir cada tabla.
- **Nombres del dominio**, no técnicos: `Budget.Adjust`, no `BudgetService.Update`.

## Casos de uso

- **Un handler, un caso de uso.** Si un handler crece, extrae un *reader* o un servicio `internal` del módulo (`BudgetReader`, `TransactionReader`), no un handler que llame a otro.
- **Las reglas viven en el dominio.** El handler orquesta: carga, llama al método del agregado, guarda y audita.
- **Devuelve `Result`; no lances excepciones para reglas de negocio.** Cada `Error` lleva un `code` estable con el prefijo del módulo (`budgets.category_invalid`) y un mensaje en inglés para quien desarrolla. El texto que ve la persona lo pone el frontend.
- **No llames a `SaveChanges`** **[test]**: lo hace el decorador.
- **Audita** cada acción de negocio con `IAuditLog.Record`, guardando el estado anterior y el nuevo.
- **Comando que mueve dinero → `IIdempotentCommand`.** Hoy no hay un test que lo exija: lo comprueba la revisión de código.
- **Respuesta 404 para lo ajeno.** Si el filtro global no lo encuentra, es `NotFound`, aunque exista.

## Async

- **`CancellationToken` en toda la cadena**: recíbelo y pásalo a cada llamada de EF y de los contratos.
- **Nunca `.Result`, `.Wait()` ni `async void`.** Bloquean hilos o pierden excepciones.
- **Sin `Task.Run` en handlers.** El trabajo largo va a un job o a una cola.
- **No hagas consultas en paralelo sobre el mismo `DbContext`** (`Task.WhenAll` con dos consultas EF): EF no lo permite y falla en tiempo de ejecución.

## EF Core

- **`AsNoTracking()` en lecturas.** Más rápido y evita cambios accidentales.
- **Proyecta con `Select`** a lo que necesitas en vez de cargar entidades completas para una lista.
- **Evita N+1**: no consultes dentro de un `foreach`. Carga en bloque con `Where(x => ids.Contains(x.Id))` y agrupa en memoria.
- **No uses `IgnoreQueryFilters()`** **[test]**: rompe la privacidad. La única excepción está en la lista blanca de `LayeringTests`.
- **Sin propiedades de navegación entre módulos** **[test]**: guarda el id.
- **Mapeos en `Infrastructure/Persistence/Configurations/<Módulo>/`**, nunca con atributos en el dominio **[test]**.
- **Enumeraciones como texto con `CHECK`**: legibles en SQL y protegidas en la base ([database.md](database.md#convenciones)).
- **Ids con `Entity.NewId()`** (`Guid.CreateVersion7`): ordenados en el tiempo, buenos para los índices.
- **SQL a mano solo con `FromSql` interpolado** (parametrizado), nunca concatenando cadenas.

## Dinero

- **`decimal`, siempre** **[test]**: ninguna propiedad de un módulo puede ser `double` o `float`.
- **Moneda explícita** junto a cada importe. No sumes importes de monedas distintas: convierte antes con el tipo de cambio (`RateTable` en Ledger).
- **Redondeo explícito y en un solo sitio** cuando haya reparto (gastos compartidos): la suma de las partes debe dar el total exacto.
- **El saldo no se guarda**: se calcula de los asientos.
- **Nada financiero se borra**: se anula y se audita.

## Fechas y tiempo

- **`IClock` en vez de `DateTime.UtcNow`**: los tests controlan el reloj.
- **`DateTimeOffset` en UTC** para instantes (`created_at`), **`DateOnly`** para fechas contables (`occurred_on`). Una fecha contable no es un instante.
- **«Hoy» depende de la zona horaria del workspace**, no del servidor: usa los helpers del módulo (`LedgerScope`, `BudgetReader.CurrentMonth()`).
- Los meses viajan como `2026-10` y las fechas como `2026-10-15` ([api-conventions.md](api-conventions.md)).

## API

- **El controller no tiene lógica**: forma del request, comando, handler, `ToActionResult` **[test]**.
- **Ruta completa en el controller** y parámetro `{workspaceId:guid}` exactamente con ese nombre **[test]**: es lo que lee `WorkspaceScopeFilter`.
- **Documenta con XML** (`/// <summary>`) y `[ProducesResponseType]` / `[Problem]`: de ahí sale el OpenAPI que usa el frontend.
- **Regenera el contrato** con `tools/update-openapi.ps1` **[test]**.
- **Request propio del controller** (`CreateBudgetRequest`) con `[Required]` para la forma; la respuesta es el tipo del handler.

## Logging y observabilidad

- **Logs estructurados**: `logger.LogWarning("Email {EmailId} could not be sent", email.Id)`, nunca interpolando la cadena.
- **Sin datos personales ni secretos en logs**: ni correos, ni importes con descripción, ni cadenas de conexión.
- Cada caso de uso ya tiene su span y su métrica gracias al decorador; añade etiquetas (`Activity.Current?.SetTag`) solo si ayudan a investigar.

## Seguridad

- **Privacidad antes que comodidad**: si dudas de si alguien debe ver un dato, no debe.
- **Autorización en tres niveles**: sesión, membresía del workspace (`WorkspaceScopeFilter`) y dueño o participación (filtro global y comprobaciones del handler) ([security.md](security.md)).
- **Los secretos nunca en el repositorio** ([devops/secrets.md](../devops/secrets.md)).
- **Valida en el servidor** todo lo que llega, aunque el frontend ya lo valide.

## Tests

- **Los tests clave, no todos**: invariantes del dominio, el camino feliz del handler contra PostgreSQL real y el caso de seguridad (404 para el recurso ajeno).
- **Sin EF InMemory**: no valida transacciones, índices únicos ni fechas ([testing.md](testing.md)).
- **Cada test crea sus usuarios y workspaces**: no dependas del orden ni limpies datos.
- **Si un test de arquitectura falla, corrige el código.**

## Cambios en la base

- **Una migración por cambio lógico**, con nombre descriptivo (`AddInvestments`, `AllowXlsxImports`).
- **Revisa el código generado** antes de aplicarla: EF puede interpretar un renombre como borrar y crear.
- **Compatible hacia atrás cuando se pueda** (añadir columnas que admiten nulos antes de exigirlas): permite volver a la versión anterior del código sin restaurar la base.
- **Regenera la documentación**: `node tools/update-db-docs.mjs`.
