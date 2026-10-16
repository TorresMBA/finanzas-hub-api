# Backend

Todo lo necesario para cambiar `finanzas-hub-api/`: una API .NET 10 y un Worker que comparten un monolito modular sobre PostgreSQL. Si es tu primer día, lee antes [getting-started/](../getting-started/README.md).

## Mapa mental en 30 segundos

```text
HTTP ──► Controller ──► ICommandHandler / IQueryHandler ──► Dominio (reglas)
              │                 │ (decoradores: log → transacción → idempotencia)
              │                 └──► IXDb ──► AppDbContext ──► PostgreSQL (un esquema por módulo)
              └── solo forma del request y Result → HTTP

Worker ──► PeriodicJob ──► el mismo handler, actuando en nombre de una persona
Módulo A ──► Contracts de B (consulta síncrona)   |   Evento de dominio ──► IDomainEventHandler en B
```

Tres ideas que lo explican casi todo:

1. **Un caso de uso es una clase.** Comando o consulta + handler que devuelve `Result<T>`. Lo transversal (transacción, idempotencia, trazas) lo ponen decoradores.
2. **Un módulo no toca lo de otro.** Usa su `Contracts` o reacciona a sus eventos. Lo verifican los tests de arquitectura.
3. **Lo privado es invisible por defecto.** El filtro global de EF deja ver solo las filas de la persona; un recurso ajeno no existe (404).

## Orden de lectura

| # | Documento | Para qué |
|---|---|---|
| 1 | [structure.md](structure.md) | Proyectos, carpetas y el flujo por entrada |
| 2 | [patterns.md](patterns.md) | Los patrones del código, con ejemplos reales |
| 3 | [module-communication.md](module-communication.md) y [modules.md](modules.md) | Fronteras entre módulos, contratos y eventos |
| 4 | [security.md](security.md) | Autenticación, los tres niveles de autorización, privacidad |
| 5 | [ledger.md](ledger.md) | El modelo financiero: operaciones, asientos, saldos, monedas |
| 6 | [concurrency.md](concurrency.md) | Transacciones, `xmin`, idempotencia, colas y jobs sin carreras |
| 7 | [best-practices.md](best-practices.md) | Cómo escribir C#, EF Core y handlers aquí |
| 8 | [common-bugs.md](common-bugs.md) | Errores que ya pasaron o que es fácil cometer, y cómo evitarlos |
| 9 | [recipes.md](recipes.md) | Paso a paso: caso de uso, endpoint, evento, job, módulo, migración |
| — | [study-guide.md](study-guide.md) | Conceptos que conviene estudiar, por prioridad |

## Referencia

| Tema | Documento |
|---|---|
| Base de datos: tablas, índices, relaciones, ER | [database.md](database.md) |
| Modelo de datos (diseño y razones) | [data-model.md](data-model.md) |
| Convenciones de la API (rutas, errores, paginación) | [api-conventions.md](api-conventions.md) |
| Migraciones | [migrations.md](migrations.md) |
| Tests: pirámide, arquitectura, seguridad | [testing.md](testing.md) |
| Trabajo en segundo plano (Worker, colas, webhooks) | [background-processing.md](background-processing.md) |
| Tiempo real (SignalR) | [realtime.md](realtime.md) |
| Cada módulo de negocio | [modules/](../modules/) |
| Por qué es así | [adr/](../adr/README.md), sobre todo [ADR-001](../adr/ADR-001-monolito-modular.md), [ADR-019](../adr/ADR-019-simplificacion-de-la-plataforma.md) y [ADR-021](../adr/ADR-021-finanzas-privadas-de-la-persona.md) |
| Observabilidad (logs, trazas, métricas) | [devops/observability.md](../devops/observability.md) |
| Configuración y secretos | [devops/configuration.md](../devops/configuration.md), [devops/secrets.md](../devops/secrets.md) |

## Si vas a…

| Tarea | Lee | Y no olvides |
|---|---|---|
| Añadir un caso de uso a un módulo | [recipes.md](recipes.md#1-un-caso-de-uso) | Test de integración y auditoría |
| Mover dinero | [ledger.md](ledger.md), [concurrency.md](concurrency.md#3-idempotencia) | `IIdempotentCommand` y `decimal` |
| Leer datos de otro módulo | [module-communication.md](module-communication.md), [recipes.md](recipes.md#5-leer-datos-de-otro-módulo) | Solo su `Contracts` y una flecha permitida en el grafo |
| Cambiar una tabla | [database.md](database.md), [migrations.md](migrations.md) | `node tools/update-db-docs.mjs` |
| Exponer un endpoint | [api-conventions.md](api-conventions.md) | `tools/update-openapi.ps1` y `npm run api:generate` |
| Programar algo periódico | [background-processing.md](background-processing.md), [recipes.md](recipes.md#7-un-job-del-worker) | Una sola instancia, tolerar reintentos |
| Tocar permisos | [security.md](security.md) | Caso de 404 para el recurso ajeno |

## Comandos

```powershell
# desde finanzas-hub-api
dotnet build PersonalFinance.slnx                   # warnings = errores
dotnet test PersonalFinance.slnx                    # integración: Docker o PERSONALFINANCE_TEST_POSTGRES
tools/add-migration.ps1 -Name Nombre
tools/migrate.ps1
tools/update-openapi.ps1                            # tras cambiar la API
node tools/update-db-docs.mjs                       # tras una migración
dotnet run --project src/PersonalFinance.Api        # http://localhost:5166/scalar
dotnet run --project src/PersonalFinance.Worker
```
