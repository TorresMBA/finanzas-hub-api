# Arquitectura: visión general

## Contexto y decisiones de partida

**Por qué existe esta arquitectura.** PersonalHub (finanzas personales y colaborativas) se construye sin improvisar arquitectura durante el desarrollo. Antes de programar se analizaron los mocks y la versión anterior, y de ese análisis salió este conjunto de documentos, que es también el backlog técnico.

**De dónde parte.**

| Fuente | Contenido |
|---|---|
| `Mocks_finanzas-hub-angular\FinanzasHub Mocks.dc.html` | Mock interactivo objetivo: 13 pantallas, 8 formularios, 3 viewports, 2 temas. Análisis en [product/mock-analysis.md](../product/mock-analysis.md) |
| `Mocks_finanzas-hub-angular\uploads\*.png` | 3 capturas de la aplicación anterior (Transacciones, Categorías, Inicio) |
| Repositorio `finance-hub-api` (V1), fuera de este | Solución `PersonalFinance.slnx`, .NET 10, 12 módulos de un proyecto, 4 BuildingBlocks, Api, Contracts, Worker, 28 migraciones, ADR 0001–0014. Revisión en [roadmap/v1-review.md](../roadmap/v1-review.md) |
| Repositorio `fiances-hub-web` (V1), fuera de este | Angular 22 sin zonas, standalone, signals, vitest |

**Decisiones de partida (12-oct-2026).**

| # | Decisión |
|---|---|
| D1 | V2 es una solución nueva en `PersonalHubV2`; de V1 se porta selectivamente dominio y tests, no persistencia ni esquema |
| D2 | Privacidad estricta: lo financiero es siempre del usuario; el workspace es directorio de personas y contenedor de lo compartido; los roles solo gobiernan administración |
| D3 | El aporte a una meta es un apartado: −500 en la cuenta origen y +500 en una cuenta de ahorro vinculada o en un fondo de la meta a nombre del participante. No es gasto y el patrimonio no baja |
| D4 | La primera versión usable incluye importación de archivos con reglas y WhatsApp (avisos y comandos). Quedan diferidos: conexión bancaria automática, planes y facturación, 2FA y gestión de sesiones |

**Revisión tras la fase 0 (12-oct-2026).** La primera plataforma resultó más compleja de lo necesario para ~10 usuarios y se simplificó. Qué cambió y por qué está en el [ADR-019](../adr/ADR-019-simplificacion-de-la-plataforma.md); este documento ya describe el resultado.

| # | Decisión |
|---|---|
| R1 | Un proyecto por módulo, controllers en la API por módulo, jobs en el Worker por módulo, una sola `Infrastructure` con un único `DbContext` |
| R2 | Eventos de dominio en proceso, dentro de la transacción del comando. Sin outbox |
| R3 | Sin bandeja genérica de webhooks. El webhook de WhatsApp será un controller (fase 11) y SignalR llega con Notificaciones (fase 6) |

## A. Executive Summary

1. **V1 no cumple el requisito crítico de privacidad.** Todas las entidades tienen solo `WorkspaceId` y la autorización es `WorkspaceMemberships.Any(workspaceId, userId)` repetida en cada repositorio (`TransactionRepository.CanAccessAsync` y equivalentes). Cualquier miembro lee y modifica cuentas, transacciones y presupuestos de los demás. Corregirlo exige añadir dueño a casi todas las tablas, por eso V2 parte de un esquema nuevo.
2. **V1 es modular solo en carpetas.** `BuildingBlocks.Infrastructure` referencia a los 12 módulos y contiene el único `ApplicationDbContext`, los 20 repositorios, las migraciones y el registro DI. Los módulos se referencian entre sí a nivel de dominio (`TransferService` construye `LedgerTransaction` directamente) y todos los tipos son públicos en un namespace plano.
3. **Arquitectura V2: monolito modular de 12 módulos con fronteras probadas.** Cada módulo es un proyecto con carpetas `Domain`, `Application` y `Contracts`. Los controllers están en la API y los jobs en el Worker, ordenados por módulo. Una `Infrastructure` con un único `DbContext` y un esquema PostgreSQL por módulo. Que un módulo solo use el `Contracts` de otro lo vigilan los tests de arquitectura.
4. **Accounts, Transactions, Transfers y CreditCards se fusionan en un módulo `Ledger`.** Son un único límite de consistencia: una transferencia son dos asientos sobre dos cuentas, el saldo es función de los asientos, y el pago de tarjeta es una transferencia.
5. **El ledger es la única fuente de verdad del dinero.** El saldo se deriva siempre de los asientos (no hay columna `Balance`), el saldo inicial es un asiento y un ajuste es un asiento explícito. Para ~10 usuarios una suma indexada cuesta milisegundos.
6. **La taxonomía de transacciones se separa en tres dimensiones** en lugar de un enum gigante: `kind` cerrado de 6 valores (mecánica contable), `source` abierto (qué módulo la originó) y tipo de cuenta. "Compra con tarjeta", "aporte a meta" o "pago de gasto compartido" son vistas derivadas.
7. **Seguridad en tres niveles dentro de la capa de aplicación.** Usuario autenticado, membresía activa del workspace, y propiedad o participación en el recurso. Se materializa con filtros globales de EF por `owner_user_id` y por participación, de modo que un recurso ajeno responde 404. Ningún rol, ni Owner, da acceso a datos privados de otro miembro.
8. **Operaciones entre módulos atómicas.** Aportar a una meta o pagar un gasto compartido escribe en dos módulos dentro de una misma transacción PostgreSQL mediante llamada síncrona al contrato de `Ledger`. Las reacciones (alertas de presupuesto, avisos a otros participantes) son eventos de dominio entregados en proceso dentro de esa misma transacción. Solo los envíos externos (correo, WhatsApp) se encolan y los procesa el Worker con reintentos.
9. **Sin dependencias de moda.** No se usa MediatR, AutoMapper, MassTransit, Hangfire, Quartz, Redis, Service Bus ni NgRx. Handlers por caso de uso con decoradores DI, `Result<T>` con `ProblemDetails`, `BackgroundService` con PostgreSQL como cola.
10. **Roadmap de 15 fases (0 a 14) en 4 hitos.** El orden propuesto en el encargo se corrige en dos puntos: Notifications sube a Fase 6 y Scheduling a Fase 7, porque Goals y SharedExpenses dependen de ambos.
11. **25 preguntas abiertas** documentadas con alternativa A/B, recomendación e impacto (sección E). Las de mayor impacto son el alcance de los datos privados (OQ-01), el alcance de la taxonomía (OQ-03) y el tratamiento contable de los reembolsos (OQ-09).

## F. Arquitectura propuesta

### F.1 Estilo

Monolito modular con dos procesos (API y Worker) que ejecutan la misma aplicación sobre una sola base PostgreSQL. Cada módulo es un bounded context con Clean Architecture interna, organizado por casos de uso (vertical slices) y con DDD pragmático: agregados donde hay invariantes, lecturas directas con proyecciones donde no.

### F.2 Módulos

Doce módulos frente a los quince sugeridos. Detalle en la sección H.

| Sugerido en el encargo | Decisión | Motivo |
|---|---|---|
| Identity | Se mantiene; absorbe perfil y preferencias de usuario | "Profiles" no justifica un módulo |
| Workspaces | Se mantiene | Frontera de membresía |
| Accounts, Transactions, Transfers, CreditCards | **Fusión en `Ledger`** | Un solo límite de consistencia; en V1 su separación produjo `Accounts → Transactions, Transfers` y `Transfers → Transactions` a nivel de dominio |
| Categories | Se mantiene; incluye tags | Contexto pequeño y estable, aguas arriba de casi todo |
| Budgets | Se mantiene | Planes, no hechos |
| Goals | **Separado de Budgets** (en V1 vivía dentro) | Recurso colaborativo con participantes |
| SharedExpenses | Se mantiene | Recurso colaborativo |
| Investments | Se mantiene | Valuación y posiciones propias |
| Scheduling | Se mantiene; incluye la proyección de calendario | Ya está resuelto en V1 |
| Notifications | Se mantiene; pasa a ser capacidad aguas arriba | Los módulos deciden qué avisar; Notifications decide cómo y cuándo |
| Reports | **Renombrado a `Reporting`**; fusiona Dashboard y Analytics | Solo lectura por composición |
| Configuration | **Eliminado** | Preferencias en Identity, ajustes del workspace en Workspaces, preferencias de aviso en Notifications |
| (no estaba) | **Añadido `Integrations`** | Importaciones, reglas, comandos de chat y futuras conexiones externas |
| Audit | No es módulo | Tabla transversal en el esquema `platform` |

### F.3 Estructura física

Se evaluaron tres opciones.

| Opción | Proyectos de módulo | Aislamiento | Veredicto |
|---|---|---|---|
| 4 proyectos por módulo (Domain, Application, Infrastructure, Api) | 48 | Capas por compilación | Excesivo para un equipo de una persona |
| 1 proyecto por módulo sin reglas (V1) | 12 | Ninguno entre módulos | Es el origen de los problemas de V1 |
| 2 proyectos por módulo: `Modules.X` todo `internal` y `Modules.X.Contracts` | 23 | Entre módulos por compilación | Se construyó en la fase 0 y se descartó por complejidad (ADR-019) |
| **1 proyecto por módulo con `Domain`, `Application` y `Contracts`, más tests de arquitectura** | 12 | Entre módulos y entre capas por tests | **Elegida** |

Reglas de la opción elegida:

- Un módulo contiene su dominio y sus casos de uso. No contiene HTTP, jobs ni persistencia concreta: eso está en `PersonalFinance.Api`, `PersonalFinance.Worker` y `PersonalFinance.Infrastructure`.
- De otro módulo solo se puede usar el namespace `Contracts`, y solo siguiendo el grafo de módulos. La carpeta aparece cuando hace falta (un módulo sin consumidores, como `Reporting`, no la tiene).
- Las capas se separan por carpeta y namespace. Todo esto lo verifican los tests de arquitectura; si se desactivan, las fronteras desaparecen.
- La diferencia con V1 no es la forma, que es parecida, sino que las reglas existen, están probadas, y los módulos no se referencian a nivel de dominio.

### F.4 Dónde vive cada cosa

| Elemento | Ubicación | Visibilidad |
|---|---|---|
| Entidades, agregados, value objects, eventos de dominio, servicios de dominio | `Modules.X/Domain` | public (las mapea Infrastructure) |
| Commands, queries y respuestas | `Modules.X/Application/<CasoDeUso>` | public (los usan controllers y jobs) |
| Handlers, políticas de acceso, validación de negocio | `Modules.X/Application/<CasoDeUso>` | internal |
| `IXDb`: las tablas que el módulo puede ver | `Modules.X/Application/Abstractions` | public (la implementa `AppDbContext`) |
| Interfaces de llamada síncrona entre módulos, sus DTO y los eventos que otros módulos pueden escuchar | `Modules.X/Contracts` | public |
| Controllers y sus requests HTTP | `PersonalFinance.Api/Controllers/<Módulo>` | public |
| Jobs | `PersonalFinance.Worker/Jobs/<Módulo>` | internal |
| `AppDbContext`, configuraciones EF, migraciones, adaptadores externos | `PersonalFinance.Infrastructure` | internal |
| Mapeo dominio ↔ DTO | Métodos estáticos junto al handler | internal |
| Contrato con el frontend | `openapi.v1.json` generado y versionado; tipos TypeScript generados a partir de él | — |

El proyecto global `PersonalFinance.Contracts` de V1 desaparece. Contenía solo requests HTTP en un namespace plano; las respuestas eran modelos de aplicación expuestos directamente.

**Acceso a persistencia.** Cada módulo declara en `Application/Abstractions` una interfaz `IXDb` que expone los `DbSet` de sus tablas; el único `AppDbContext` las implementa todas. Así el módulo no referencia infraestructura ni ve las tablas de otro, y un test de arquitectura lo comprueba. Los handlers no llaman a `SaveChanges`: lo hace el decorador de transacción al terminar el comando. Guardar por cuenta propia con eventos de dominio pendientes lanza una excepción.

**Validación.** La forma del request (obligatorios, rangos, formatos) se valida en el controller con las anotaciones de datos del request, que `[ApiController]` comprueba antes de ejecutar la acción, y devuelve 400 con mensajes por campo. Las reglas de negocio se validan en dominio y aplicación y devuelven `Result`. FluentValidation queda como opción futura si las reglas de forma se complican.

### F.5 Evaluación de patrones

| Patrón | Clasificación | Uso concreto |
|---|---|---|
| CQRS ligero | NECESARIO AHORA | Handlers de comando y de consulta separados sobre la misma base. Las consultas proyectan con `AsNoTracking`. Sin almacén de lectura aparte |
| Mediator (librería) | INNECESARIO | `ICommandHandler<TCommand,TResult>` e `IQueryHandler<TQuery,TResult>` resueltos por DI. Lo transversal se aplica con decoradores. MediatR además pasó a licencia comercial |
| Domain Events | NECESARIO AHORA | El agregado acumula eventos; el decorador de transacción los entrega en proceso a sus handlers, dentro de la transacción del comando. Sirven también entre módulos cuando el evento se declara en `Contracts` |
| Integration Events | INNECESARIO | Un solo tipo de evento basta mientras todo corre en un proceso y una base |
| Outbox | ÚTIL MÁS ADELANTE | Se construyó en la fase 0 y se retiró. La entrega diferida solo hace falta para envíos externos, que usan la cola de Notifications. Volvería si una reacción lenta empieza a bloquear comandos |
| Inbox | ÚTIL MÁS ADELANTE | La deduplicación de webhooks se resuelve con un índice único |
| Strategy | NECESARIO AHORA | Estrategias de reparto, canales de notificación, adaptadores de importación. En los tres hay variación real |
| Factory | NECESARIO AHORA como método estático | `Account.Open(...)`, `Operation.PostExpense(...)` devuelven `Result<T>`. Sin fábricas abstractas |
| Specification | INNECESARIO | Extensiones de `IQueryable` (`.Effective()`, `.InPeriod()`) cubren la reutilización de filtros |
| Result | NECESARIO AHORA | Sustituye a `DomainException`, que V1 mapeaba siempre a 409 |
| Repository | INNECESARIO como capa genérica; ÚTIL puntualmente | El handler usa la interfaz `IXDb` de su módulo, que expone los `DbSet` del contexto. Solo se crea repositorio cuando cargar un agregado tiene lógica propia (`Operation` con asientos, `SharedExpense` con partes) |
| Unit of Work | INNECESARIO como interfaz | El `DbContext` ya lo es, y al ser uno solo cubre también el caso entre módulos |
| Policy | NECESARIO AHORA | Políticas de acceso a recurso como objetos de aplicación (sección J) |
| Domain Services | NECESARIO AHORA, puntual | Funciones puras que cruzan agregados: cálculo de reparto, simplificación de deudas, recurrencia, ritmo de presupuesto, ciclo de tarjeta |
| Application Services | SUSTITUIDO | Un handler por caso de uso en lugar de clases `XService` de 10 KB como las de V1 |
| Background Workers | NECESARIO AHORA | Jobs del Worker: envío de notificaciones, scheduler, análisis de importaciones y limpieza |
| IDs fuertemente tipados | ÚTIL MÁS ADELANTE | `Guid` versión 7 por ahora |
| Row-Level Security de PostgreSQL | ÚTIL MÁS ADELANTE | Segunda barrera de aislamiento si crece el número de usuarios |
| Event Sourcing, Saga, CQRS con proyecciones | INNECESARIO | No hay problema que lo pida |

### F.6 Qué se abstrae y qué no

| Frontera | ¿Abstraer? | Motivo |
|---|---|---|
| Correo (`IEmailSender`) | Sí | SMTP local, proveedor gestionado en producción |
| Archivos (`IFileStorage`) | Sí | Disco local, Blob Storage después |
| Bus de eventos | No | Los eventos se entregan en proceso. Se abstraería al separar un servicio |
| Canales (`INotificationChannel`) | Sí | Tres implementaciones reales |
| Tiempo real (`IRealtimeNotifier`) | Sí | Para que los módulos no dependan de SignalR |
| Reloj (`IClock`), usuario actual (`ICurrentUser`) | Sí | Tests y jobs |
| Fuentes externas (`IExchangeRateSource`, `IPriceSource`, `IImportSourceAdapter`) | Sí | Proveedores intercambiables |
| Motor de base de datos | No | EF Core es la abstracción. Se usan a propósito `xmin`, `jsonb`, `SKIP LOCKED`, columnas generadas e índices parciales |
| Repositorio y UoW genéricos | No | Duplican EF |
| ASP.NET Core Identity | No | Se usa directo dentro de `Identity`; el resto ve `ICurrentUser` e `IUserDirectory` |
| Caché | No | No hay necesidad medida |
| Logging | No | `ILogger` basta |

### F.7 Persistencia

Un `AppDbContext` en `PersonalFinance.Infrastructure`, un esquema PostgreSQL por módulo (más `platform`), una base de datos y un solo historial de migraciones.

- Un comando es una transacción sobre ese contexto. Un caso de uso que toca dos módulos confirma o revierte entero sin ninguna fontanería adicional.
- Cada módulo accede solo a sus tablas, a través de su `IXDb`. Las configuraciones EF están en `Infrastructure/Persistence/Configurations/<Módulo>` y declaran el esquema del módulo; un test comprueba que ninguna tabla queda fuera del suyo.
- **Entre módulos se referencia por id, sin propiedad de navegación.** La FK en base de datos sí puede declararse cuando protege un enlace de dinero (un aporte y su operación). Lo referenciado nunca se borra físicamente: se anula o se archiva.
- No se habilita la estrategia de reintento automático de Npgsql porque es incompatible con transacciones iniciadas por la aplicación.

Alternativa descartada: un `DbContext` y un historial de migraciones por módulo sobre una conexión compartida. Aísla más y deja cada módulo listo para extraerse, pero añade maquinaria que a esta escala no se usa. Se construyó en la fase 0 y se retiró (ADR-019).

Para la fase 1: se usa el almacenamiento estándar de ASP.NET Core Identity (`AddEntityFrameworkStores<AppDbContext>`), sin escribir ningún store. `AppDbContext` hereda de `IdentityUserContext` (usuarios, sin tablas de roles) y sus tablas van al esquema `identity`. El módulo `Identity` usa `UserManager` directamente; como ese tipo está en el namespace `Microsoft.AspNetCore.Identity`, los tests de arquitectura llevarán una excepción limitada a ese namespace y a ese módulo. Se confirma al planificar la fase.

### F.8 Flujo de una petición

```text
HTTP → middleware (correlación, autenticación, actor)
     → filtro global: si la ruta lleva {workspaceId}, membresía activa → IWorkspaceContext (404 si no)
     → controller: valida forma, construye el comando
     → decoradores: logging → transacción → idempotencia (la clave se guarda dentro de la transacción del comando)
     → handler: carga con filtro de dueño → dominio (Result) → auditoría → contrato de otro módulo si aplica
     → guardar → eventos de dominio a sus handlers, en proceso → guardar → commit
     → Result → respuesta o ProblemDetails
     → (Worker) envíos externos encolados durante el comando
```

Un job del Worker entra por el mismo sitio: abre un scope, fija el actor y llama al handler decorado.

## G. Diagrama general

### G.1 Contexto del sistema

Diagrama: [Contexto del sistema](../diagrams/system-context.md).

### G.2 Contenedores

Diagrama: [Contenedores](../diagrams/containers.md).

La API y el Worker son dos procesos que solo se comunican a través de la base de datos (sección R).
