# Guía de estudio del backend

Conceptos que conviene dominar para trabajar con soltura en `finanzas-hub-api`, por orden de prioridad. Para cada uno: qué saber, dónde se aplica aquí y por dónde empezar a estudiarlo.

## Imprescindible (primera semana)

| Concepto | Qué saber | Dónde se usa aquí | Para estudiar |
|---|---|---|---|
| C# moderno | `record`, constructores primarios, pattern matching (`is { } x`, `is not`), nullable reference types, colecciones `[...]` | Todo el código | [Novedades de C#](https://learn.microsoft.com/dotnet/csharp/whats-new/) |
| async/await | `Task`, `CancellationToken`, por qué no bloquear con `.Result` | Handlers, EF, jobs | [Programación asíncrona](https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/) |
| Inyección de dependencias | Vidas `Scoped`, `Singleton`, `Transient`; por qué un `DbContext` es scoped | `DependencyInjection.cs`, `AddModule` | [DI en .NET](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) |
| ASP.NET Core: pipeline | Middleware, filtros, controllers, model binding, `ProblemDetails` | `Api/Http/HttpSetup.cs`, `RequestContext.cs` | [Fundamentos de ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/) |
| EF Core básico | `DbContext`, change tracker, `AsNoTracking`, LINQ traducido a SQL, migraciones | `AppDbContext`, `IXDb` | [EF Core](https://learn.microsoft.com/ef/core/) |
| Result pattern | Errores esperados como valores, no excepciones | `Result`, `Error` | [patterns.md §4](patterns.md#4-result-en-lugar-de-excepciones) |

## Importante (primer mes)

| Concepto | Qué saber | Dónde se usa aquí | Para estudiar |
|---|---|---|---|
| Monolito modular | Módulos con fronteras, contratos, dependencias dirigidas | Toda la solución | [ADR-001](../adr/ADR-001-monolito-modular.md), [module-communication.md](module-communication.md) |
| DDD táctico | Entidad, value object, agregado, invariante, evento de dominio | `Domain/` de cada módulo | *Domain-Driven Design Distilled* (Vaughn Vernon); [DDD en .NET](https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/) |
| CQRS (versión ligera) | Separar lectura y escritura sin dos bases | Comandos y consultas | [patterns.md §2](patterns.md#2-cqrs-ligero-un-caso-de-uso-una-clase) |
| Decorator | Envolver un componente para añadir comportamiento | `HandlerDecorators.cs` | [Refactoring Guru: Decorator](https://refactoring.guru/design-patterns/decorator) |
| EF Core avanzado | Filtros de consulta globales, owned types, tokens de concurrencia, `FromSql` | `AppDbContext`, configuraciones | [Global query filters](https://learn.microsoft.com/ef/core/querying/filters), [Concurrency](https://learn.microsoft.com/ef/core/saving/concurrency), [Owned types](https://learn.microsoft.com/ef/core/modeling/owned-entities) |
| PostgreSQL | Esquemas, índices compuestos y parciales, `CHECK`, `EXPLAIN ANALYZE` | [database.md](database.md) | [Documentación de PostgreSQL](https://www.postgresql.org/docs/current/) (capítulos *Indexes* y *Performance Tips*) |
| Concurrencia en bases de datos | MVCC, Read Committed, `xmin`, bloqueos de fila, `SKIP LOCKED` | [concurrency.md](concurrency.md) | [Concurrency Control](https://www.postgresql.org/docs/current/mvcc.html), [SELECT … FOR UPDATE](https://www.postgresql.org/docs/current/sql-select.html#SQL-FOR-UPDATE-SHARE) |
| Contabilidad de doble entrada | Asiento, débito y crédito, por qué el saldo se deriva | Módulo Ledger | [ledger.md](ledger.md), [ADR-004](../adr/ADR-004-ledger-financiero.md) |
| Idempotencia en APIs | Claves de idempotencia, reintentos seguros, entrega «al menos una vez» | `IIdempotentCommand`, colas | [Stripe: Idempotent requests](https://docs.stripe.com/api/idempotent_requests) |

## Para profundizar

| Concepto | Qué saber | Dónde se usa aquí | Para estudiar |
|---|---|---|---|
| Autenticación con cookies | Cookie `HttpOnly`, `Secure`, `SameSite`; CSRF; Data Protection | `Api/Http/Session.cs`, `HttpSetup.cs` | [Cookie authentication](https://learn.microsoft.com/aspnet/core/security/authentication/cookie), [Data Protection](https://learn.microsoft.com/aspnet/core/security/data-protection/introduction) |
| ASP.NET Core Identity | Hash de contraseñas, bloqueo, tokens de confirmación | Módulo Identity | [Identity](https://learn.microsoft.com/aspnet/core/security/authentication/identity) |
| OWASP Top 10 | Control de acceso roto, inyección, configuración insegura | [security.md](security.md) | [OWASP Top 10](https://owasp.org/www-project-top-ten/) |
| SignalR | Hubs, grupos, reconexión | `Api/Realtime/`, [realtime.md](realtime.md) | [SignalR](https://learn.microsoft.com/aspnet/core/signalr/introduction) |
| Background services | `BackgroundService`, scopes por ejecución, apagado ordenado | `Worker/Jobs/PeriodicJob.cs` | [Hosted services](https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services) |
| OpenTelemetry | Trazas, spans, métricas, correlación | `Infrastructure/Observability/` | [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/dotnet/) |
| OpenAPI | Contrato como fuente de los tipos del cliente | `docs/api/openapi.v1.json` | [OpenAPI en ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/overview) |
| Testing | xUnit, `WebApplicationFactory`, Testcontainers, tests de arquitectura | `tests/` | [Integration tests](https://learn.microsoft.com/aspnet/core/test/integration-tests), [Testcontainers .NET](https://dotnet.testcontainers.org/), [NetArchTest](https://github.com/BenMorris/NetArchTest) |

## Ejercicio recomendado

Recorre un caso completo con el depurador: pon un punto de ruptura en `BudgetsController.Create`, crea un presupuesto desde la aplicación y sigue la llamada por los tres decoradores, `CreateBudgetHandler`, `Budget.Create`, el `SaveChanges` del dispatcher y el `COMMIT`. Después registra un gasto en esa categoría y mira cómo `EvaluateBudgetThresholds` reacciona al evento `LedgerOperationChanged` dentro de la misma transacción. Con eso habrás visto casi todos los patrones del backend.
