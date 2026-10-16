# Estrategia de pruebas

## V. Testing strategy

### V.1 Pirámide

| Nivel | Proyecto | Qué prueba | Infraestructura |
|---|---|---|---|
| Dominio | `UnitTests` | Invariantes de agregados y value objects; funciones puras (reparto, recurrencia, ritmo de presupuesto, proyección de meta, ciclo de tarjeta) con valores `decimal` exactos | Ninguna |
| Aplicación | `IntegrationTests` | Handlers de punta a punta: filtros de dueño, una transacción por comando, reacciones a eventos, idempotencia, concurrencia | PostgreSQL real: contenedor de Testcontainers, o un servidor existente indicado en `PERSONALFINANCE_TEST_POSTGRES`. Cada test usa usuarios y workspaces nuevos, así que no hace falta limpiar entre tests |
| Arquitectura | `ArchitectureTests` | Reglas de la sección I y de J.2 | Reflexión |
| API | `IntegrationTests` | Autenticación, matriz de autorización, forma de `ProblemDetails`, `Idempotency-Key`, contrato OpenAPI | `WebApplicationFactory` y Testcontainers |
| E2E | `finanzas-hub-web/e2e` | Recorridos críticos en 3 viewports | Playwright contra el stack local |

No se usa EF InMemory. V1 lo usaba y su propia documentación reconoce que no valida transacciones, índices únicos ni semántica de fechas.

### V.2 Tests de arquitectura

Un módulo es un solo proyecto con tipos públicos, así que estas reglas no las impone el compilador: las imponen estos tests. Si uno falla se corrige el código, no el test.

Los módulos, controllers y jobs se descubren a partir de los dos hosts, así que lo nuevo queda cubierto sin tocar los tests. Lo único que se edita a mano es el grafo permitido (`AllowedDependencies` en `ModuleBoundaryTests`).

| Regla | Cómo | Test |
|---|---|---|
| `X/Domain` solo depende de `BuildingBlocks.Domain` y de su propio `Contracts`: ni EF Core, ni ASP.NET, ni `X/Application`. Única excepción: el módulo Identity puede usar el namespace `Microsoft.AspNetCore.Identity` (la parte de biblioteca de ASP.NET Core Identity), y nada más de ASP.NET | Dependencias por namespace (NetArchTest) | `LayeringTests` |
| `X/Application` no referencia ASP.NET, Npgsql ni `Infrastructure` | Dependencias por namespace | `LayeringTests` |
| `X/Contracts` solo depende de `BuildingBlocks.Domain` | Dependencias por namespace | `LayeringTests` |
| Un módulo usa de otro solo su `Contracts` | Dependencias por namespace | `ModuleBoundaryTests` |
| Un proyecto de módulo solo referencia BuildingBlocks y los módulos que permite el grafo de I.1 | `ProjectReference` de cada `.csproj` | `ModuleBoundaryTests` |
| Un proyecto de módulo no trae proveedor de base de datos ni framework web | Paquetes y `FrameworkReference` del `.csproj` | `ModuleBoundaryTests` |
| `BuildingBlocks.*` no referencia módulos, `Infrastructure` ni hosts | `.csproj` | `ModuleBoundaryTests` |
| `Infrastructure` solo la referencian los hosts, y los hosts no se referencian entre sí | `.csproj` | `ModuleBoundaryTests` |
| Controllers y jobs no tocan persistencia: ni EF Core, ni Npgsql, ni el `IXDb` de un módulo | Dependencias por namespace | `LayeringTests` |
| Un controller o un job está en la carpeta de su módulo y solo usa ese módulo | Namespace y dependencias | `LayeringTests` |
| Toda ruta de workspace nombra el parámetro `{workspaceId:guid}`, que es lo que lee el filtro de membresía | Plantillas de ruta por reflexión | `LayeringTests` |
| `IgnoreQueryFilters` solo en la lista blanca | Análisis de IL (Mono.Cecil) | `LayeringTests` |
| Ninguna propiedad o campo de un módulo es `double` o `float` | Reflexión | `LayeringTests` |
| Toda entidad `IOwned` tiene filtro global | Modelo EF | `CommandPipelineTests` (integración) |
| Cada tabla está en el esquema de su módulo | Modelo EF | `CommandPipelineTests` (integración) |
| Todo comando con efecto financiero implementa `IIdempotentCommand` | Recorrido de tipos con marca | Sin hacer: hoy se revisa en la revisión de código ([common-bugs.md](common-bugs.md)) |

Además, `AppDbContext` es `internal`: que la API o el Worker lo usen directamente no compila.

Los ejemplos del encargo quedan cubiertos: "Domain no puede referenciar Infrastructure" es la primera fila, y "Accounts no puede acceder al DbContext de Transactions" lo impiden la cuarta fila (no puede usar sus entidades) y que cada módulo solo recibe su propio `IXDb`.

En el frontend, los límites entre `core`, `shared`, `layout` y `features` los comprueba ESLint (ver [frontend/architecture.md](../frontend/architecture.md)).

### Cómo ejecutar

```powershell
# Backend. Los tests de integración necesitan PostgreSQL:
#   con Docker en marcha no hay que configurar nada (Testcontainers);
#   sin Docker, apuntar a un servidor existente. Se crea una base temporal y se borra al terminar.
$env:PERSONALFINANCE_TEST_POSTGRES = 'Host=localhost;Port=5432;Username=postgres;Password=...;Database=postgres'
dotnet test finanzas-hub-api/PersonalFinance.slnx

# Frontend
cd finanzas-hub-web
npm test -- --watch=false
npm run lint
npm run e2e          # requiere la API (http://localhost:5166) y el Worker en marcha
```

### V.3 Tests de seguridad obligatorios

Por cada endpoint que recibe un id: el usuario B, miembro del mismo workspace, recibe 404 sobre un recurso privado de A. Por cada recurso colaborativo: un miembro del workspace que no participa recibe 404, y un participante sin permiso recibe 403. Se generan a partir de una tabla de endpoints para que añadir un endpoint sin su caso haga fallar la build.

### V.4 Recorridos E2E

1. Registro, onboarding, crear cuenta, registrar gasto, verlo en Inicio.
2. Transferencia entre cuentas: una fila, patrimonio sin cambio.
3. Presupuesto que cruza el umbral y genera notificación.
4. Dos usuarios: meta compartida, aporte de uno, el otro lo ve; un tercero no ve la meta.
5. Dos usuarios: gasto compartido, pago de una parte, estado `PartiallyPaid` y luego `Settled`.
6. Importar CSV, revisar, confirmar, sin duplicar al reimportar.
