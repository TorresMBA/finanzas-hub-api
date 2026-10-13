# Roadmap por fases

## Y. Roadmap por fases

### Y.1 Corrección del orden propuesto

| Cambio | Motivo |
|---|---|
| Accounts y Transactions se reparten en dos fases de Ledger | Una cuenta sin asientos no muestra saldo; se entregan juntas y luego transferencias y tarjetas |
| Notifications sube de la fase 10 a la 6 | Presupuestos, metas y gastos compartidos emiten avisos; conviene tener el canal antes de construirlos |
| Calendar/Scheduling sube de la fase 9 a la 7 | El aporte automático de metas, "Repetir cada mes" y "Próximos pagos" dependen de él |
| WhatsApp e importación entran antes que Reports e Investments | D4 |
| Se añade una fase final de tarjetas avanzadas y FX | Cuotas, estado de cuenta y conciliación ya existen en V1 y se portan cuando hagan falta |

### Y.2 Hitos

| Hito | Fases | Resultado |
|---|---|---|
| H1 Finanzas personales | 0 a 5 | Un usuario lleva cuentas, movimientos y presupuestos |
| H2 Colaboración | 6 a 9 | Avisos, calendario, metas y gastos compartidos |
| H3 Automatización | 10 y 11 | Importación con reglas y WhatsApp. Cierra la primera versión usable (D4) |
| H4 Análisis y patrimonio | 12 a 14 | Reportes, inversiones, tarjetas avanzadas |

### Y.3 Definition of Done por feature

Está en [development/definition-of-done.md](../development/definition-of-done.md).

### Y.4 Fases

#### Fase 0 — Fundación

| | |
|---|---|
| Goal | Esqueleto ejecutable con las reglas de arquitectura ya vigiladas |
| Functional scope | Ninguno de negocio |
| Domain model | `Entity`, `AggregateRoot`, `Result`, `Error`, `Money`, `Currency`, `IOwned` |
| Use cases | Un caso de uso de ejemplo en un módulo `Sample` que se borra al final |
| Infrastructure | Solución, BuildingBlocks, `PersonalFinance.Infrastructure` con un `AppDbContext` único, decoradores de comando, eventos de dominio en proceso, idempotencia, auditoría, `Result` a `ProblemDetails`, middleware de correlación, OpenTelemetry, health checks, `docker-compose` |
| Database | Esquemas `platform` y `sample` |
| API | `/health/live`, `/health/ready`, documento OpenAPI |
| Frontend | Workspace Angular; tokens del mock; shell responsive con navegación vacía; `shared/ui` base; interceptores; generación del cliente; lint de límites |
| Tests | Los tres proyectos; reglas de arquitectura; fixture de Testcontainers; un E2E de humo |
| Observability | Trazas, métricas y logs visibles en Aspire Dashboard |
| Security | Política por defecto "autenticado"; CORS; cabeceras de seguridad |
| Acceptance | `dotnet build` y `dotnet test` en verde; `docker compose up` levanta todo; `ng build` y `ng test` en verde; romper a propósito una regla de arquitectura hace fallar el test |
| Dependencies | Ninguna |
| Risks | Las fronteras entre módulos dependen de tests y no del compilador; se valida rompiendo cada regla a propósito |
| Out of scope | Cualquier módulo de negocio; CI/CD a Azure |

**Estado: implementada el 12-oct-2026 y rediseñada el 13-oct-2026**, tras la revisión que recoge el [ADR-019](../adr/ADR-019-simplificacion-de-la-plataforma.md). Lo que sigue describe el resultado del rediseño.

Verificado:

| Comprobación | Resultado |
|---|---|
| `dotnet build PersonalFinance.slnx` | Sin errores ni advertencias |
| `dotnet test PersonalFinance.slnx` | 63 tests: 22 unitarios, 19 de arquitectura, 22 de integración contra PostgreSQL 18 |
| Las reglas muerden | Ocho violaciones deliberadas producen ocho fallos. En el módulo: EF Core en el dominio, y el proveedor de base de datos como paquete. En la API: un controller que usa `ISampleDb`, y una ruta de workspace con otro nombre de parámetro. Con un segundo módulo de prueba: módulo sin declarar en el grafo, referencia no permitida, uso del dominio de otro módulo, y controller fuera de la carpeta de su módulo |
| Una tabla fuera del esquema de su módulo | La detecta su test. Si además falta la migración, falla antes la comprobación de cambios pendientes de EF Core |
| El contrato HTTP no cambió al pasar a controllers | `HttpPipelineTests` pasa sin modificar; en `openapi.v1.json` solo cambian el orden de las respuestas y el nombre de un esquema de error |
| Una transacción por comando | Un comando que escribe en `sample` y `platform` y luego falla no deja ninguna fila |
| Eventos en proceso | La reacción a `NoteCreated` se guarda con la nota. Una reacción que lanza revierte la nota, su auditoría y su clave de idempotencia |
| Idempotencia | Misma clave y mismo cuerpo devuelven la primera respuesta; misma clave y otro cuerpo, 422 |
| Privacidad | Un miembro del mismo workspace no ve las notas de otro; quien no es miembro recibe 404 |
| API real | Sobre una base vacía crea `platform` y `sample` con un solo historial; `/health/ready` responde `Healthy`; sin sesión responde 401 como `problem+json` con `code` y `traceId` |
| Worker real | Arranca y el job de purga completa una pasada |
| Frontend | `ng build` correcto; 21 tests unitarios; `ng lint` correcto |
| E2E | 6 tests en Chrome: shell a 1440, 768 y 390 px, tema, y API alcanzable por el proxy |

No verificado, porque la máquina de desarrollo no tiene Docker:

- `docker compose up` (postgres, mailpit, aspire-dashboard) y la build del `Dockerfile` de la API.
- El camino de Testcontainers de los tests de integración. Se usó la alternativa `PERSONALFINANCE_TEST_POSTGRES`.
- La llegada de trazas, métricas y logs a Aspire Dashboard. La instrumentación está configurada; la exportación solo se activa con `OTEL_EXPORTER_OTLP_ENDPOINT`.

Diferencias respecto al diseño original, ya reflejadas en estos documentos:

| Tema | Diseño original | Implementación |
|---|---|---|
| Estructura de módulos, persistencia, eventos, webhooks y procesos en segundo plano | Ver la tabla del ADR-019 | Ver la tabla del ADR-019 |
| Acceso a datos desde un caso de uso | El handler usa el `DbContext` del módulo | El handler usa la interfaz `IXDb` de su módulo, que implementa el único `AppDbContext` |
| Orden de decoradores | logging → idempotencia → transacción | logging → transacción → idempotencia, para que la clave se guarde en la transacción del comando |
| Validación de forma | Validación integrada de Minimal APIs de .NET 10 | Anotaciones de datos comprobadas por `[ApiController]`, con una respuesta propia que mantiene `code` y los campos en camelCase |
| Trazas | Un `ActivitySource` por módulo | Uno común con etiqueta `module` |
| Limpieza de la base entre tests | Respawn | No hace falta: cada test usa usuarios y workspaces nuevos |
| Módulo `Sample` | Se borra al final de la fase | Se conserva hasta que exista el primer módulo real, porque los tests de la plataforma lo usan |
| Ubicación de `docs/` | Dentro de `finanzas-hub-api` | En la raíz del repositorio |
| Formularios del frontend | Reactive Forms | Signal Forms, estables desde Angular 22 |
| Reintento de un job que falla | Al cumplirse su intervalo | Al minuto. Se vio al arrancar el Worker antes de que existiera la base: un job diario habría esperado un día |
| ADR afectados por el rediseño | Reescribirlos | Se marcan como sustituidos por el ADR-019, como pide `adr/README.md` |
| Controller del webhook de WhatsApp (fase 11) | `Controllers/Webhooks` | `Controllers/Notifications`, para cumplir la regla de un controller por carpeta de módulo |

Piezas que se crean con el primer módulo que las use: ver la tabla final de [architecture/backend-structure.md](../architecture/backend-structure.md).

#### Fase 1 — Identity y Workspaces

| | |
|---|---|
| Goal | Registrarse, entrar, tener workspace e invitar |
| Functional scope | Registro, login, logout, confirmación y recuperación; workspace personal automático; onboarding de moneda y zona horaria; invitaciones; roles; perfil y preferencias |
| Domain model | `User`, `UserPreferences`, `Workspace`, `Membership`, `Invitation` |
| Use cases | Los de H.1 y H.2 |
| Infrastructure | ASP.NET Core Identity; cookie; comprobación de origen contra CSRF; cola de correos (`platform.outgoing_emails`) con `SendEmailsJob`; implementación real de `IWorkspaceAccess`; registro de las implementaciones de `Contracts` en `AddInfrastructure` |
| Database | Esquemas `identity` y `workspaces` |
| API | `/auth/*`, `/me`, `/me/profile`, `/me/preferences`, `/workspaces`, `W/onboarding/complete`, `W/members`, `W/invitations`, `/invitations/{token}` |
| Frontend | `auth`, `onboarding`, `settings` (perfil, miembros, preferencias); selector de workspace; guards |
| Tests | Invariantes de workspace (un Owner, reglas de rol); registro crea workspace de forma atómica; invitación caducada y reutilizada; matriz de roles |
| Observability | Métricas de login fallido; auditoría de membresía |
| Security | Límite de peticiones en `/auth`; bloqueo por intentos; token de invitación con hash |
| Acceptance | Brian se registra, completa onboarding con PEN, invita a Juan; Juan acepta y aparece como Member; Juan no puede invitar |
| Dependencies | Fase 0 |
| Risks | Cookie entre SPA y API en local: se resuelve con el proxy. ASP.NET Core Identity se usa con su almacenamiento estándar (`AddEntityFrameworkStores<AppDbContext>`), sin escribir ningún store: `AppDbContext` hereda de `IdentityUserContext` y el módulo usa `UserManager` directamente, lo que pide una excepción en los tests de arquitectura limitada al namespace `Microsoft.AspNetCore.Identity` y a este módulo. Se confirma al planificar la fase |
| Out of scope | 2FA, sesiones activas, foto por subida de archivo, plan |

**Estado: implementada el 13-oct-2026.**

Verificado:

| Comprobación | Resultado |
|---|---|
| `dotnet build PersonalFinance.slnx` | Sin errores ni advertencias |
| `dotnet test PersonalFinance.slnx` | 116 tests: 48 unitarios, 19 de arquitectura, 49 de integración contra PostgreSQL 18 |
| Criterio de aceptación | Brian se registra, completa el onboarding con PEN e invita a Juan; Juan se registra desde el enlace, aparece como Member y recibe 403 al invitar. Probado por API y, en el navegador, con el enlace del correo real |
| Sesión | Los tests de identidad usan la cookie real, no un usuario simulado. Cerrar sesión deja `/me` en 401 |
| Bloqueo | Cinco contraseñas erróneas bloquean la cuenta también para la correcta; restablecer la contraseña la desbloquea |
| Registro atómico | Un registro que falla en la invitación no deja cuenta: el mismo correo se puede registrar después |
| Recuperación | El enlace cambia la contraseña una sola vez; pedirlo para un correo sin cuenta responde igual y no encola nada |
| Segundo nivel de autorización | Quien no es miembro recibe 404 en el workspace, sus miembros y sus invitaciones. Un miembro expulsado lo pierde en la siguiente petición, con la misma cookie |
| Roles | Un Admin invita miembros pero no crea ni degrada Admins; nadie cambia su propio rol; el Owner no sale sin transferir |
| CSRF | Una petición que cambia estado con `Origin` ajeno recibe 403 y no tiene efecto |
| Correos | La cola se entrega una vez y el cuerpo se borra al enviar |
| Frontend | `ng build` correcto; 47 tests unitarios; `ng lint` correcto |
| E2E | 10 tests en Chrome: registro con onboarding, validación del formulario, cerrar y abrir sesión, invitación completa entre dos navegadores, y los del shell ya con sesión |
| Revisión visual | Login, registro, onboarding y ajustes en escritorio y móvil, tema claro y oscuro |

No verificado:

- El envío por SMTP real. Sin servidor configurado los correos se escriben como archivos `.eml`, y eso es lo que se probó.
- El límite de peticiones de `/auth`: está configurado, pero ningún test provoca el 429.
- Lo que depende de Docker, igual que en la fase 0.

Diferencias respecto al diseño de la fase:

| Tema | Diseño | Implementación |
|---|---|---|
| Protección CSRF | Tokens antifalsificación | Cookie `SameSite=Lax` más rechazo de toda petición que cambia estado con un `Origin` que no sea la propia API o un frontend permitido ([ADR-020](../adr/ADR-020-sesion-y-correos.md)) |
| Correos | `IEmailSender` que envía por SMTP | `IEmailSender` encola en la transacción del comando y un job del Worker envía con reintentos |
| `GET /me` | Incluye los workspaces | `GET /me` y `GET /me/workspaces` por separado: un controller sirve a un solo módulo |
| Correo confirmado | No especificado | No se exige para entrar; el perfil muestra si está confirmado |
| Eventos `UserRegistered`, `UserProfileChanged`, `MemberInvited` | Se publican | No se levantan todavía: nadie reacciona a ellos. Los de Workspaces que sí usará la fase 2 (`OnboardingCompleted`, `MemberJoined`, `MemberRemoved`) existen |
| `ChangeBaseCurrency`, archivar workspace, foto de perfil, `IWorkspaceDirectory` | En H.1 y H.2 | Diferidos: cambiar la moneda exige saber si hay asientos (fase 3); el directorio llega con su primer consumidor (fase 8) |
| `ETag` e `If-Match` | En `Workspace` | `Workspace` lleva `xmin` como token de concurrencia sobre sus propios datos (nombre, zona horaria, propietario), pero la API aún no expone `ETag` y ningún test lo ejercita; llega con el ledger (fase 3). Los cambios de miembros no pasan por ese token |
| Índices únicos de Owner y de invitación pendiente | En L.2 | Se validan en el dominio y en el handler. Son índices parciales y EF Core no ordena las sentencias de un cambio de propietario para respetarlos |
| Módulo `Sample` | Se elimina con el primer módulo real | Se conserva hasta la fase 3: los tests de la plataforma necesitan un comando idempotente y una entidad `IOwned`, que aparecen con el ledger |
| Guards del frontend | Uno de sesión y otro de onboarding en la misma ruta | Un guard completo por ruta. El router ejecuta a la vez los guards de una ruta, y el de onboarding decidía antes de que cargaran los workspaces; se vio en el navegador |

#### Fase 2 — Categories

| | |
|---|---|
| Goal | Taxonomía propia lista para clasificar |
| Functional scope | Categorías y subcategorías de ingreso y gasto; tags; tags sugeridos; siembra por defecto; archivar |
| Domain model | `Category`, `Tag` |
| Use cases | Los de H.4 |
| Infrastructure | Catálogo de plantillas; reacción a `OnboardingCompleted` y `MemberJoined`; primer filtro global por dueño |
| Database | Esquema `categories` |
| API | `W/categories`, `W/tags` |
| Frontend | `categories`; selectores `category-select` y `tag-chips` en `shared/forms` |
| Tests | Dos niveles; unicidad; archivar en cascada; siembra idempotente; A no ve las categorías de B |
| Observability | Span por caso de uso |
| Security | Primer uso real de `IOwned`; test del modelo EF |
| Acceptance | Tras el onboarding existen las categorías por defecto; se crea una subcategoría con tags; al archivar deja de ofrecerse |
| Dependencies | Fase 1 |
| Risks | OQ-03 cambia claves y siembra si se elige compartir por workspace |
| Out of scope | Fusionar categorías; montos por categoría (llegan con Ledger) |

**Estado: implementada el 13-oct-2026**, con OQ-03 y OQ-04 en su opción A (categorías y tags privados por usuario; un único `Tag`).

Verificado:

| Comprobación | Resultado |
|---|---|
| `dotnet build PersonalFinance.slnx` | Sin errores ni advertencias |
| `dotnet test PersonalFinance.slnx` | 151 tests: 73 unitarios, 19 de arquitectura, 59 de integración contra PostgreSQL 18 |
| Criterio de aceptación | Tras el onboarding existen las 12 categorías por defecto con sus subcategorías y tags; se crea una categoría con tags y una subcategoría; al archivarla deja de listarse y de contar en sus tags. Probado por API y en el navegador |
| Privacidad | Dos miembros del mismo workspace tienen cada uno sus categorías. Los de uno responden 404 al otro en renombrar, archivar, restaurar, asignar tags y crear subcategoría, y tampoco al Owner. Lo mismo entre dos workspaces del mismo usuario |
| Siembra | Quien se registra desde una invitación recibe su copia sin haber iniciado sesión. Repetirla no añade nada, no recupera lo renombrado ni lo archivado y respeta una categoría propia con el nombre de una por defecto |
| Dos niveles y unicidad | Un tercer nivel se rechaza. El nombre es único entre hermanas activas sin distinguir mayúsculas; el de una archivada queda libre y restaurarla entonces da conflicto |
| Archivar y restaurar | Archivar una categoría archiva sus subcategorías; restaurarla recupera solo las que se archivaron con ella |
| Tags | Se crean al nombrarlos, normalizados; archivar uno lo quita de todas las categorías; volver a nombrarlo recupera el mismo |
| Rechazos | Una petición rechazada no deja nada escrito, tampoco el tag que habría creado |
| Arquitectura | `Categories` solo depende de `Workspaces`; `IgnoreQueryFilters` solo en la siembra, declarada en la lista de excepciones |
| Frontend | `ng build` correcto y sin avisos de presupuesto; 58 tests unitarios; `ng lint` correcto |
| E2E | 13 tests en Chrome, 3 nuevos: sembrar, crear categoría y subcategoría, nombre repetido, filtrar por tag, archivar y restaurar, y que persiste al recargar; tag rápido y archivarlo; móvil sin desbordes y formulario como hoja |
| Revisión visual | Lista, filtros, formulario y etiquetas en escritorio, tablet y móvil, tema claro y oscuro |

No verificado: lo que depende de Docker, igual que en las fases anteriores.

Diferencias respecto al diseño de la fase:

| Tema | Diseño | Implementación |
|---|---|---|
| Tags de una subcategoría | No especificado; en el mock los tags escritos al crear una subcategoría van a su categoría | Solo la categoría de primer nivel sugiere tags. Una subcategoría usa el tipo, el icono, el color y los tags de su categoría, y no guarda ninguno |
| Mover una categoría (`UpdateCategory` con padre) | En H.4 | Diferido: el mock no tiene esa acción. `UpdateCategory` cambia nombre, icono y color |
| `RenameTag`, `CreateTag` (`POST W/tags`) | En H.4 y P.4 | Diferidos a la fase 3. Un tag se crea al nombrarlo en una categoría; el formulario de transacción traerá su creación directa. Renombrar llega cuando haya transacciones que lo referencien |
| `ICategoryLookup`, `ITagLookup` | Contratos del módulo | Llegan con su primer consumidor, el ledger (fase 3) |
| Evento `CategoryArchived` | Se publica | No se levanta todavía: nadie reacciona. Lo declarará la fase 7, cuando Scheduling avise de reglas con una categoría archivada |
| Siembra | Al `OnboardingCompleted` y al `MemberJoined` | Igual, más `POST W/categories/defaults` para pedirla a mano. Lo usa el estado vacío de la pantalla y sirve a las cuentas creadas antes de esta fase |
| Unicidad del nombre | Índice sobre `lower(name)` | Columna `name_key` con el nombre en minúsculas e índice único parcial con `NULLS NOT DISTINCT`; EF Core lo modela sin SQL a mano |
| Montos y movimientos por categoría, conteo de uso de tags en transacciones | En la pantalla del mock | Llegan con el ledger. El contador de cada tag muestra cuántas categorías lo sugieren |
| `category-select` y `tag-chips` en `shared/forms` | En la fase | Llegan con el formulario de transacción (fase 3), que es quien los usa. Esta fase deja en `shared/ui` el diálogo modal, la tarjeta de KPI y el icono con color |
| Archivar un tag | No especificado | Pide confirmación en dos pasos, porque quita el tag de todas las categorías y restaurarlo no las recupera |

#### Fase 3 — Ledger I: cuentas y transacciones

| | |
|---|---|
| Goal | Registrar ingresos y gastos y ver saldos |
| Functional scope | Cuentas de banco, efectivo y billetera; saldo inicial; ingreso y gasto; editar, confirmar y anular; tags; listado con filtros |
| Domain model | `Account`, `Operation`, `Entry`, `OperationKind`, `OperationSource` |
| Use cases | OpenAccount, UpdateAccount, ArchiveAccount, AdjustAccountBalance, PostTransaction, EditTransaction, ConfirmTransaction, VoidTransaction, SetTransactionTags y sus consultas |
| Infrastructure | `ILedgerDb` y sus configuraciones EF; índice de saldo; `IdempotencyDecorator` en uso real; `ILedgerQueries` inicial |
| Database | `accounts`, `operations`, `entries`, `operation_tags` |
| API | `W/accounts`, `W/transactions` |
| Frontend | `accounts` y `transactions` con formulario de gasto e ingreso; detalle; filtros; paginación por cursor |
| Tests | Saldo = suma de asientos; saldo inicial y ajuste como asientos; anular excluye; idempotencia; edición auditada; matriz de seguridad |
| Observability | `ledger.operations.posted`; auditoría de todas las mutaciones |
| Security | Filtro de dueño en cuentas, operaciones y asientos |
| Acceptance | Crear cuenta con saldo 1 000, registrar gasto de 54 y ver 946; anularlo y ver 1 000; reenviar el mismo POST no duplica |
| Dependencies | Fases 1 y 2 |
| Risks | OQ-05 y OQ-22 afectan estados y edición |
| Out of scope | Transferencias, tarjetas, multimoneda en reportes, importación |

**Estado: implementada el 13-oct-2026**, con OQ-05, OQ-06 y OQ-22 en su opción A (un movimiento pendiente cuenta en el saldo; la moneda es la de la cuenta; un movimiento confirmado se edita en sitio con antes y después en auditoría).

Verificado:

| Comprobación | Resultado |
|---|---|
| `dotnet build PersonalFinance.slnx` | Sin errores ni advertencias |
| `dotnet test PersonalFinance.slnx` | 177 tests: 89 unitarios, 19 de arquitectura, 69 de integración contra PostgreSQL 18 |
| Criterio de aceptación | Cuenta con saldo 1 000, gasto de 54 y saldo 946; al anularlo, 1 000; reenviar el mismo POST con la misma clave devuelve el mismo movimiento y no duplica. Probado por API y en el navegador |
| Saldo = suma de asientos | El saldo de la API coincide con la suma de `signed_amount` de los asientos efectivos leída de la base. `Account` no tiene ninguna propiedad ni columna de saldo, y un test lo comprueba |
| Saldo inicial y ajuste | Son operaciones con su asiento. Ajustar al mismo saldo no registra nada. No cuentan como ingreso ni como gasto |
| Pendiente, confirmar, anular | Un pendiente cuenta desde que se registra y se confirma una sola vez. Anular lo saca de saldos, listas y totales y lo conserva con su motivo; anular dos veces no cambia nada |
| Edición | Cambiar monto, cuenta, fecha, tipo y categoría mueve el dinero entre cuentas; la auditoría guarda el antes y el después. Lo que registró la aplicación (saldo inicial, ajuste) y lo anulado no se editan |
| Reglas | Monto mayor que cero y con los decimales de la moneda; la categoría debe ser propia, del tipo del movimiento y no archivada; la cuenta, propia y activa. Una petición rechazada no mueve ningún saldo ni deja etiquetas creadas |
| Idempotencia | Clave obligatoria en abrir cuenta, ajustar saldo y registrar transacción. Misma clave y otro cuerpo, 422. Un comando que falla no retiene su clave |
| Listado | Filtros por tipo, cuenta, categoría con sus subcategorías, etiqueta, texto y fechas; anulados solo si se piden. Totales por moneda sobre todo lo que cumple el filtro. Paginación por cursor sin repetir ni saltar filas |
| Matriz de seguridad | Dos miembros del mismo workspace llevan libros separados: las cuentas y los movimientos de uno responden 404 al otro en leer, editar, archivar, ajustar, confirmar y anular; tampoco puede registrar en la cuenta ni con la categoría del otro; ni el Owner ve los del miembro. Lo mismo entre dos workspaces del mismo usuario |
| Plataforma sin `Sample` | Los tests del pipeline usan módulos reales: transacción única, reacción que falla y revierte, idempotencia, y que una reacción ve lo que su comando escribió |
| Frontend | `ng build` correcto y sin avisos; 101 tests unitarios; `ng lint` correcto |
| E2E | 17 tests en Chrome, 4 nuevos: el criterio de aceptación de punta a punta, pendiente-confirmar-editar-filtrar, ajuste y archivo de cuenta, y móvil sin desbordes con el detalle como hoja. Repetidos tres veces seguidos sin fallos |
| Revisión visual | Cuentas, transacciones, formulario y detalle en escritorio, tablet y móvil, tema claro y oscuro |

No verificado: lo que depende de Docker; y la concurrencia de `xmin`, que ningún test provoca.

Diferencias respecto al diseño de la fase:

| Tema | Diseño | Implementación |
|---|---|---|
| `ETag` e `If-Match` | En `Account` y `Operation` | `Account` y `Operation` llevan `xmin`, que protege cada comando contra una escritura cruzada, pero la API no expone `ETag`. Son datos de un solo dueño: el conflicto real llega con los agregados colaborativos, y ahí se añade (fase 8) |
| `SetTransactionTags` | Caso de uso propio | Las etiquetas van en el cuerpo de registrar y de editar; no hay endpoint aparte |
| `POST W/tags` | Previsto para esta fase | No hace falta: una etiqueta se crea al nombrarla en una transacción, igual que en una categoría |
| Cambiar el tipo al editar | No especificado | Se puede pasar de gasto a ingreso y al revés; la categoría debe corresponder al nuevo tipo |
| Restaurar una cuenta | No especificado | `POST W/accounts/{id}/restore`, simétrico a archivar |
| Eventos `OperationPosted`, `OperationChanged`, `OperationVoided`, `AccountArchived` | Se publican | No se levantan todavía: nadie reacciona. Los declarará la fase 5, cuando Budgets reaccione a un gasto |
| `ILedgerQueries` inicial | En la fase | Llega con su primer consumidor (fase 4, el dashboard) |
| Totales | Un resumen del filtro | Un resumen por moneda: nunca se suman dos monedas. La conversión a moneda base llega con el tipo de cambio (fase 4) |
| Asientos | Privados por su operación | Cada asiento repite `workspace_id` y `owner_user_id` y tiene su propio filtro de dueño, así el saldo es una suma sobre una sola tabla |
| Monto y movimientos por categoría en Categorías | Pendiente de la fase 2 para esta | Pasa a la fase 4, que trae la consulta de gasto por categoría para el Inicio |
| Detalle del movimiento | Panel lateral | Panel lateral en escritorio; por debajo de ese ancho se abre como hoja, porque el panel quedaría fuera de la vista |
| "Repetir cada mes" y "Dividir gasto" | En el formulario y el detalle del mock | Llegan con Scheduling (fase 7) y SharedExpenses (fase 9) |
| Módulo `Sample` | Se elimina aquí | Eliminado, con la migración `RemoveSample` que borra sus dos tablas y su esquema |

#### Fase 4 — Ledger II: transferencias, tarjetas básicas, patrimonio e Inicio

| | |
|---|---|
| Goal | Mover dinero entre cuentas, llevar tarjetas y ver el resumen |
| Functional scope | Transferencia, también multimoneda; tarjeta con línea y día de pago; pago de tarjeta; patrimonio, activos y deudas; tipo de cambio de referencia; Inicio v1 |
| Domain model | `CreditCardProfile`, `ExchangeRate`; operación `Transfer` con dos asientos |
| Use cases | PostTransfer, PayCreditCard, GetAccountSummary, GetDashboard v1 |
| Infrastructure | `ILedgerPosting` completo; módulo `Reporting` con el dashboard; consultas de flujo y de gasto por categoría |
| Database | `credit_card_profiles`, `exchange_rates`; columnas de conversión en `entries` |
| API | `W/transfers`, `W/accounts/{id}/payments`, `W/accounts/summary`, `W/dashboard` |
| Frontend | Modo transferencia en el formulario; tarjetas en `accounts` con utilización; `dashboard` |
| Tests | Transferencia: patrimonio sin cambio y una fila; anulación de ambos asientos; disponible de tarjeta; conversión a moneda base |
| Observability | Métrica de duración del dashboard |
| Security | Ambas cuentas de una transferencia deben ser propias |
| Acceptance | Transferir 500 de BCP a Interbank: −500 y +500, patrimonio igual; una compra con tarjeta reduce el disponible; un pago lo repone |
| Dependencies | Fase 3 |
| Risks | Definir qué cuentas entran en patrimonio |
| Out of scope | Estado de cuenta, cuotas, conciliación (Fase 14); próximos pagos y metas en Inicio (se completan en las fases 7 y 8) |

**Estado: implementada el 13-oct-2026.** El riesgo de la fase quedó decidido por el dueño ese día (OQ-26): entran en el patrimonio todas las cuentas en uso, nunca las archivadas, y cada cuenta lleva una casilla "Incluir en mi patrimonio" con la que la persona la saca o la vuelve a meter. Las cuentas de una meta nacerán fuera (fase 8).

Verificado:

| Comprobación | Resultado |
|---|---|
| `dotnet build PersonalFinance.slnx` | Sin errores ni advertencias |
| `dotnet test PersonalFinance.slnx` | 217 tests: 119 unitarios, 19 de arquitectura, 79 de integración contra PostgreSQL 18 |
| Criterio de aceptación | Transferir 500 de BCP a Interbank deja −500 y +500 y el patrimonio igual; una compra de 200 con tarjeta baja el disponible de 5 000 a 4 800 y el pago lo repone. Probado por API y en el navegador |
| Transferencia | Una operación con dos asientos y una sola fila en el listado. No es ingreso ni gasto: no aparece en ningún total. Anularla saca los dos asientos a la vez. Reenviar el mismo POST no la duplica. No se edita |
| Multimoneda | Entre dos monedas se indican los dos montos y cada asiento queda en la moneda de su cuenta; sin el monto de destino se rechaza. Entre cuentas de la misma moneda, un monto de destino distinto se rechaza |
| Tarjeta | Es una cuenta con línea, día de pago y día de cierre opcional. La deuda es su saldo negativo y el disponible, la línea más el saldo. El pago es una transferencia hacia la tarjeta desde una cuenta que no sea otra tarjeta, también desde otra moneda |
| Patrimonio | Suma lo que tienen y resta lo que deben las cuentas en uso que la persona no ha sacado. Una archivada no cuenta; una con la casilla desmarcada tampoco, y mover dinero a ella baja el total |
| Conversión a moneda base | Cada asiento en otra moneda guarda lo que valía al registrarse, así un mes pasado no cambia cuando cambia el tipo de cambio. Los saldos se valoran al tipo vigente. Una moneda sin tipo de cambio queda fuera del total y la respuesta dice cuál es |
| Tipo de cambio | Lo lleva cada persona, por moneda y por día; darlo otra vez el mismo día lo reemplaza. No mueve dinero |
| Inicio | Patrimonio y su variación, ingresos, gastos y tasa de ahorro del mes; flujo de seis meses; categorías con más gasto, sumando sus subcategorías; últimos movimientos; cuentas de mayor saldo |
| Matriz de seguridad | Un miembro no puede transferir desde ni hacia la cuenta de otro, ni pagar su tarjeta, ni anular su transferencia: todo responde 404. Patrimonio, Inicio, tipos de cambio y totales por categoría solo hablan del dinero propio, también para el Owner |
| Arquitectura | `Reporting` es un módulo sin tablas que solo usa los contratos de `Ledger` y de `Categories`; los 19 tests de arquitectura lo cubren |
| Frontend | `ng build` correcto y sin avisos; 118 tests unitarios; `ng lint` correcto |
| E2E | 21 tests en Chrome, 4 nuevos: la transferencia de punta a punta, compra y pago de tarjeta, otra moneda con su tipo de cambio más la casilla del patrimonio y el Inicio, y móvil sin desbordes. Repetidos tres veces seguidas sin fallos |
| Revisión visual | Inicio, Cuentas con tarjetas, Transacciones y los formularios de transferencia, pago y tarjeta, en escritorio, tablet y móvil, tema claro y oscuro |

No verificado: lo que depende de Docker; y la métrica `reporting.dashboard.duration`, que se registra pero nadie la ha leído en un colector.

Diferencias respecto al diseño de la fase:

| Tema | Diseño | Implementación |
|---|---|---|
| Qué cuentas entran en el patrimonio | Riesgo abierto | OQ-26, decidido por el dueño: todas las cuentas en uso, con una casilla por cuenta para sacarla. Cambia la consecuencia de D3 y del ADR-013: aportar a una meta sí bajará el patrimonio mostrado mientras la cuenta de la meta esté fuera |
| `ExchangeRate` | Tabla de referencia común `(base, quote, rate, as_of, source)` | Privada de cada persona, como todo en el ledger: sin un rol nuevo que decida el tipo de cambio de los demás. Sin columna `source`: solo se mantiene a mano |
| `ILedgerPosting` completo | En la fase | Sigue sin existir: ningún módulo registra todavía en el ledger. Llega con su primer consumidor |
| Pago de tarjeta | Transferencia con `source = cards/payment` | Transferencia sin `source`: marcarla como de otro módulo impediría anularla. Se reconoce porque su destino es una tarjeta |
| Comisión de la transferencia | Gasto vinculado | No está. Se registra como un gasto aparte |
| Edición de una transferencia | No especificada | No se edita: se anula y se registra de nuevo |
| Filtro `type` del listado | Ingreso o gasto | Cualquier tipo de operación, para poder pedir solo transferencias |
| Línea de la tarjeta | No especificado | No bloquea: una compra puede dejar el disponible en negativo, y pagar de más deja saldo a favor |
| Cuentas del Inicio (OQ-24) | Marca de favorita | Las cuatro de mayor saldo. La marca no existe todavía |
| `GET W/exchange-rates` y `PUT W/exchange-rates/{moneda}` | No previstos | Hacen falta para mantener el tipo de cambio desde la pantalla de Cuentas |
| `GET W/transactions/category-totals` | No previsto | Da a la pantalla de Categorías lo que movió cada una en el mes; cierra el pendiente de la fase 2 |
| Aviso de vencimiento de tarjeta | En el MVP de tarjetas | Fase 6, con Notifications. La fecha del próximo pago ya se calcula |
| Cambiar la moneda base | Pendiente de la fase 1 para esta | Sin fecha: obligaría a revalorar los asientos de todos los miembros |

#### Fase 5 — Budgets

| | |
|---|---|
| Goal | Límites mensuales por categoría con ritmo |
| Functional scope | Presupuesto mensual por categoría raíz de gasto; umbral; marca de fijo; ajuste de límite versionado; resumen, estados y gasto diario |
| Domain model | `Budget`, `BudgetProgressCalculator` |
| Use cases | Los de H.7 salvo la notificación del umbral |
| Infrastructure | Consulta de gasto por categoría en `ILedgerQueries`; se portan de V1 `BudgetPeriodRange` y `BudgetProgress` |
| Database | Esquema `budgets` |
| API | `W/budgets` |
| Frontend | `budgets` completo |
| Tests | Qué entra y qué no en el consumo; estados según ritmo; versión por vigencia; el reembolso reduce el consumo |
| Observability | Span del cálculo |
| Security | Dueño |
| Acceptance | Con límite 450 y gasto 498 el estado es Excedido; una transferencia no cambia el consumo |
| Dependencies | Fases 2 a 4 |
| Risks | OQ-16 y OQ-17 |
| Out of scope | Semanal; acumulación del sobrante; presupuesto por tag o cuenta; aviso del umbral (Fase 6) |

**Estado: implementada el 13-oct-2026.** Por pedido del dueño ese día, la fase lleva solo los tests clave: las reglas de dinero, la privacidad y el criterio de aceptación. Los riesgos estaban decididos: la marca de fijo es explícita (OQ-16) y solo hay presupuesto mensual (OQ-17).

Verificado:

| Comprobación | Resultado |
|---|---|
| `dotnet build PersonalFinance.slnx` | Sin errores ni advertencias |
| `dotnet test PersonalFinance.slnx` | 229 tests: 129 unitarios, 19 de arquitectura, 81 de integración contra PostgreSQL 18 |
| Criterio de aceptación | Con límite 450 y gasto 498 el estado es Excedido; una transferencia no cambia el consumo. Probado por API y en el navegador |
| Qué entra en el consumo | Los gastos del mes en la categoría y en sus subcategorías, también los pendientes. No entran un gasto anulado, un ingreso, una transferencia ni el gasto de otra categoría |
| Estados | Excedido si lo gastado pasa del límite; Fijo o Pagado si es fijo; En riesgo si el porcentaje usado supera en más de 10 puntos la parte del mes transcurrida; si no, En ritmo. Un mes cerrado nunca está en riesgo |
| Versión por vigencia | Ajustar el límite en un mes posterior cierra el presupuesto y abre otro desde ese mes: el mes anterior conserva su límite. En el mismo mes se cambia en el sitio |
| Matriz de seguridad | Otro miembro del mismo workspace no ve el presupuesto en su lista, y leerlo, ajustarlo o quitarlo responde 404. Cada uno puede tener el suyo sobre la categoría del mismo nombre |
| Arquitectura | `Budgets` usa solo los contratos de `Ledger` y de `Categories`; `Reporting` lee `IBudgetQueries`. Los 19 tests de arquitectura lo cubren |
| Frontend | `ng build` correcto y sin avisos; 120 tests unitarios; `ng lint` correcto |
| E2E | 22 tests en Chrome, 1 nuevo: crear el presupuesto, excederlo con un gasto, ajustar el límite y verlo en el Inicio |
| Revisión visual | Presupuestos en escritorio y móvil, tema oscuro |

No verificado: lo que depende de Docker; tablet y tema claro de la pantalla nueva; el span del cálculo, que no se añadió.

Diferencias respecto al diseño de la fase:

| Tema | Diseño | Implementación |
|---|---|---|
| Reembolso | Resta del consumo | No hay todavía forma de registrar un reembolso en el ledger, así que nada resta. Se añade con él |
| Evaluación de umbrales y eventos del ledger | En la fase, salvo la notificación | Fase 6: su único efecto es el aviso, y sin Notifications no hay nada que hacer con ellos. El umbral elegido ya se guarda. Tampoco existe `budget_alerts` |
| Vigencia del ajuste | Desde el mes indicado | Siempre desde el mes actual. Crear también empieza en el mes actual |
| Quitar un presupuesto | Fija `ends_on` | Igual, salvo el que empezó este mes: no gobernó ningún mes cerrado y su fila se borra, con la traza en la auditoría. Un presupuesto es un plan, no un hecho financiero |
| Acumular lo no gastado | En el formulario del mock | Fuera de alcance de la fase; el formulario no lo ofrece |
| Sugerencia con el promedio de 3 meses | Inferido del mock | No está |
| "Ver movimientos" | Va a Transacciones filtrado por la categoría | Va a Transacciones sin filtro; el panel ya lista los movimientos del presupuesto |
| `ETag` / `If-Match` | `Budget` en la lista de agregados con concurrencia | `xmin` configurado; la API no lo expone (fase 8) |
| Span del cálculo | En la fase | No se añadió |
| Presupuesto en el Inicio | Pendiente de la fase 4 | Hecho: una quinta cifra con el porcentaje usado, solo para quien tiene presupuestos |
| `ILedgerQueries.GetExpensesAsync` | No previsto | Da al detalle los gastos de la categoría y el gasto por día |

#### Fase 6 — Notifications y tiempo real

| | |
|---|---|
| Goal | Bandeja de avisos, preferencias, correo y push al navegador |
| Functional scope | Bandeja in-app; no leídas; preferencias por categoría y canal; horario silencioso; canal de correo; umbral de presupuesto y movimiento grande como primeros avisos |
| Domain model | `Notification`, `Delivery`, `NotificationSettings`, `ChannelEndpoint` |
| Use cases | Los de H.3 salvo WhatsApp; `EvaluateThresholds` de Budgets |
| Infrastructure | `INotificationPublisher`; `INotificationChannel` con InApp y Email; cola de envíos (`notifications.deliveries`) y `SendDeliveriesJob` en el Worker; hub SignalR en la API; `IRealtimeNotifier`, que envía tras el commit |
| Database | Esquema `notifications`; `budget_alerts` |
| API | `/me/notifications*`, `/me/notification-preferences`, `/me/notification-settings`; `/hubs/realtime` |
| Frontend | `notifications`; badge; `RealtimeService`; toasts |
| Tests | Deduplicación; preferencias; silencio; una alerta por umbral y periodo; el hub solo entrega al destinatario |
| Observability | Métricas de entregas y de envíos pendientes |
| Security | Un usuario solo lee sus avisos; grupo del hub por identidad |
| Acceptance | Cruzar el 90 % de un presupuesto produce un aviso en la bandeja y el badge sube sin recargar; con la preferencia desactivada no llega correo |
| Dependencies | Fases 1 y 5 |
| Risks | Reconexión de SignalR; se cubre con el sondeo de respaldo |
| Out of scope | WhatsApp, push móvil, alertas personalizadas, resumen semanal |

#### Fase 7 — Scheduling y Calendario

| | |
|---|---|
| Goal | Pagos programados, recurrentes y calendario |
| Functional scope | Regla única, mensual o anual (el motor admite más); modo automático o con confirmación; recordatorios; "Repetir cada mes"; calendario con proyección; próximos pagos; saldo proyectado; aviso de vencimiento de tarjeta |
| Domain model | `RecurringRule`, `Occurrence`, `RecurrenceSchedule` |
| Use cases | Los de H.6 |
| Infrastructure | Se porta de V1 `RecurrenceSchedule`, `ScheduleProjection`, `SchedulingRunner` y `OccurrencePoster`; `RunDueOccurrencesJob` en el Worker, que actúa como el dueño de cada regla; `ICalendarItemProvider` e `IScheduledActionHandler` |
| Database | Esquema `scheduling` |
| API | `W/calendar`, `W/recurring-rules` |
| Frontend | `calendar`; tarjeta de próximos pagos en Inicio; interruptor en el formulario de transacción |
| Tests | Recurrencia y fines de semana (tests de V1); unicidad por regla y fecha; sin relleno retroactivo; regla pausada si el dueño pierde acceso |
| Observability | `scheduler.occurrences`; duración del ciclo |
| Security | El proceso actúa como el dueño de la regla |
| Acceptance | Una regla mensual aparece en los meses siguientes; al llegar la fecha se registra una sola vez aunque el proceso corra dos veces; el recordatorio llega un día antes |
| Dependencies | Fases 4 y 6 |
| Risks | Zonas horarias; se trabaja con fechas locales del workspace |
| Out of scope | Festivos; ediciones de serie complejas |

#### Fase 8 — Goals

| | |
|---|---|
| Goal | Metas personales y compartidas con aportes |
| Functional scope | Crear meta; participantes; aporte y retiro; cuenta vinculada o fondo; aporte automático; estado y proyección; avisos; señal de tiempo real |
| Domain model | `Goal`, `GoalParticipant`, `GoalContribution`, `GoalProjection` |
| Use cases | Los de H.8 |
| Infrastructure | `EnsureSystemAccount` en Ledger; transacción entre Goals y Ledger; implementación de `IScheduledActionHandler` e `ICalendarItemProvider`; filtro por participación |
| Database | Esquema `goals` |
| API | `W/goals` y sub-recursos |
| Frontend | `goals` con panel de participantes (sin mock: diseñar); tarjeta de metas en Inicio |
| Tests | Aporte atómico con Ledger; reconciliación aporte ↔ operación; dos aportes simultáneos; meta alcanzada una sola vez; no participante recibe 404; el participante no ve la cuenta ajena |
| Observability | Auditoría con `scope = goal` |
| Security | `GoalParticipant` y `GoalOwner` |
| Acceptance | El caso del encargo: Brian aporta 500 desde BCP; BCP baja 500; aparece la transferencia; el progreso sube para Brian y Juan; Pedro recibe 404 |
| Dependencies | Fases 4, 6 y 7 |
| Risks | OQ-12 y OQ-13; diseño de UI de metas compartidas |
| Out of scope | Moneda distinta entre cuenta y meta; custodia |

#### Fase 9 — SharedExpenses

| | |
|---|---|
| Goal | Dividir gastos y liquidar entre miembros |
| Functional scope | Grupos; gasto con cuatro repartos; publicar; "Dividir gasto" desde una transacción; liquidación por parte o en bloque; vincular cuenta; balances; sugerencia de liquidación; recordar; actividad |
| Domain model | `SharedGroup`, `SharedExpense`, `ExpenseShare`, `Settlement`, `ISplitStrategy`, `DebtSimplifier` |
| Use cases | Los de H.9 |
| Infrastructure | Se porta el cálculo de reparto de `SharedExpense.Create` de V1 a cuatro estrategias; `PostRefund` en Ledger; filtro por membresía del grupo |
| Database | Esquema `shared_expenses` |
| API | `W/shared-groups` y sub-recursos; `W/shared-expenses/summary` |
| Frontend | `shared-expenses` con editor de reparto; acción en el detalle de transacción |
| Tests | Las cuatro estrategias y el redondeo; transición de estados; liquidación que no supera la deuda; reembolso como `Refund`; anulación; concurrencia en edición; matriz de seguridad |
| Observability | Auditoría con `scope = shared-group` |
| Security | `SharedGroupMember`, `SharedExpenseEditor`, `SettlementParty` |
| Acceptance | El caso del encargo: cena de 300 a partes iguales, 50/30/20 y 150/100/50; Juan paga 100 desde su cuenta: su saldo baja, el gasto queda `PartiallyPaid`; al pagar Pedro queda `Settled`; consta quién pagó, cuánto y cuándo |
| Dependencies | Fases 4 y 6 |
| Risks | OQ-07 a OQ-11 |
| Out of scope | Invitados sin usuario; comentarios; compensación automática de deudas cruzadas; grupos multimoneda |

#### Fase 10 — Integrations: importación y reglas

| | |
|---|---|
| Goal | Cargar movimientos desde archivo sin duplicar |
| Functional scope | Subir CSV, XLSX u OFX; staging; duplicados; revisión; confirmación; reglas "si contiene" |
| Domain model | `ImportSession`, `ImportRecord`, `CategorizationRule` |
| Use cases | Los de H.10 salvo chat |
| Infrastructure | Se porta `ImportSession`, `IImportSourceAdapter` y `BluecoinsCsvAdapter` de V1; adaptadores CSV genérico, XLSX y OFX; `IFileStorage`; `ParseImportsJob` en el Worker sobre las sesiones en estado `Uploaded` |
| Database | Esquema `integrations` |
| API | `W/imports`, `W/categorization-rules` |
| Frontend | `integrations` con zona de carga, tabla de revisión y reglas |
| Tests | Cada adaptador con archivos de ejemplo; huella de duplicado; confirmación idempotente; reimportar no duplica |
| Observability | Métricas de registros nuevos, duplicados e inválidos |
| Security | Límite de tamaño y tipo; el archivo nunca se registra en logs |
| Acceptance | Importar un CSV de 200 filas, ver 5 duplicados marcados, confirmar y ver 195 movimientos; repetir la importación no añade nada |
| Dependencies | Fases 2 a 4 |
| Risks | Variedad de formatos por banco; se empieza con un CSV de columnas mapeables |
| Out of scope | Conexión bancaria, lectura de correo, Google Sheets, tokens de API |

#### Fase 11 — WhatsApp

| | |
|---|---|
| Goal | Avisos y consultas por WhatsApp |
| Functional scope | Verificar teléfono; canal de WhatsApp para pagos, vencimientos y recordatorios de deuda; comandos `saldo` y `ayuda`; estados de entrega |
| Domain model | `ChannelEndpoint` verificado; `ChatCommand` |
| Use cases | Verificación; `ReceiveWhatsAppWebhook`; `HandleInboundMessage` |
| Infrastructure | Canal de WhatsApp (se porta el envío de `WhatsAppCloudOutboxEventHandler` y el verificador de `WhatsAppWebhook.cs`); controller del webhook en `Api/Controllers/Notifications`, que procesa dentro de la petición |
| Database | Tabla de eventos recibidos con índice único `(provider, external_id)`; uso de `channel_endpoints` |
| API | `/webhooks/v1/whatsapp`; `/me/channels/whatsapp/*` |
| Frontend | Verificación de teléfono en preferencias de notificación |
| Tests | Firma válida e inválida; evento repetido; teléfono no verificado; comando resuelto al usuario correcto |
| Observability | Métricas de webhooks y de entregas |
| Security | HMAC en tiempo constante; teléfono ligado a usuario |
| Acceptance | Un recordatorio de pago llega por WhatsApp; `saldo` responde solo con cuentas del remitente; reenviar el mismo webhook no produce una segunda respuesta |
| Dependencies | Fases 6, 7 y 10 |
| Risks | Alta de la app en Meta y aprobación de plantillas; conviene iniciarla durante la Fase 6 |
| Out of scope | Registrar gastos por chat; otros canales |

#### Fase 12 — Reporting

| | |
|---|---|
| Goal | Reportes por periodo y exportación |
| Functional scope | Mes, trimestre y año; comparación con el periodo anterior; dona; variación; comercios; tasa de ahorro; exportar XLSX y CSV; búsqueda global |
| Domain model | Solo modelos de lectura |
| Use cases | Los de H.12 |
| Infrastructure | Consultas agregadas en `ILedgerQueries`; normalización de `payee` |
| Database | Índice sobre `payee` normalizado |
| API | `W/reports/summary`, `W/reports/export`, `W/search` |
| Frontend | `reports`; búsqueda en el topbar |
| Tests | Totales contra datos sembrados; periodos equivalentes; exportación |
| Observability | Duración por reporte |
| Security | Dueño |
| Acceptance | Las cifras del reporte mensual coinciden con Transacciones y Presupuestos para el mismo mes |
| Dependencies | Fases 4 y 5 |
| Risks | Rendimiento en el año completo |
| Out of scope | PDF; reportes programados; resumen semanal |

#### Fase 13 — Investments

| | |
|---|---|
| Goal | Portafolio con valor y rendimiento |
| Functional scope | Fondos, depósitos a plazo, acciones, ETF y cripto; aportar y rescatar; valuación manual; distribución; inclusión en patrimonio; aviso de vencimiento |
| Domain model | `Asset`, `Holding`, `InvestmentTransaction`, `PriceSnapshot`, `TermDepositTerms` |
| Use cases | Los de H.11 |
| Infrastructure | Cuenta de sistema `Investment`; `IPortfolioValuation`; `ICalendarItemProvider` |
| Database | Esquema `investments` |
| API | `W/holdings`, `W/investments/summary` |
| Frontend | `investments` |
| Tests | El aporte es transferencia y no gasto; ganancia realizada; valor proyectado de depósito; patrimonio sin doble conteo |
| Observability | Auditoría de operaciones |
| Security | Dueño |
| Acceptance | Registrar un depósito de 15 000 desde una cuenta: la cuenta baja, el patrimonio no; el gasto del mes no cambia |
| Dependencies | Fases 4 y 7 |
| Risks | Evitar doble conteo entre cuenta de inversión y valuación |
| Out of scope | Precios automáticos; trading; impuestos |

#### Fase 14 — Tarjetas avanzadas y FX

| | |
|---|---|
| Goal | Estado de cuenta, cuotas, conciliación y tipo de cambio automático |
| Functional scope | Ciclos y estado de cuenta; compras en cuotas con interés; conciliación contra extracto; fuente automática de tipo de cambio |
| Domain model | `CreditCardCycle`, `InstallmentPlan`; estados `Cleared` y `Reconciled` |
| Use cases | GetStatement, CreateInstallmentPlan, PostInstallmentInterest, ReconcileAccount, RefreshExchangeRates |
| Infrastructure | Se porta de V1 `CreditCardStatementBuilder`, `InstallmentPlan`, `InstallmentService`, `ReconciliationService` y sus tests |
| Database | `installment_plans`, `installment_charges`, `account_reconciliations` |
| API | `W/accounts/{id}/statements`, `W/installment-plans`, `W/accounts/{id}/reconciliations` |
| Frontend | Detalle de tarjeta con estado de cuenta y cuotas (sin mock: diseñar) |
| Tests | Los de V1: `CreditCardStatementTests`, `InstallmentTests`, `ReconciliationTests` |
| Observability | Auditoría de conciliación |
| Security | Dueño |
| Acceptance | Una compra en 6 cuotas reduce el disponible por el total y cada estado de cuenta exige solo su cuota |
| Dependencies | Fases 4 y 7 |
| Risks | Reglas por banco |
| Out of scope | Tarjeta bimoneda como una sola entidad |

### Y.5 Backlog diferido

Conexión bancaria automática; planes, facturación y límites; inicio de sesión con Google y Microsoft (OQ-25); 2FA, sesiones activas y biometría; reportes programados y resumen semanal; PDF; push; alertas personalizadas y detección de cargos duplicados; invitados sin usuario y comentarios en gastos compartidos; lectura de correo; Google Sheets; tokens de API y webhooks salientes; precios automáticos; perfil de riesgo; app móvil.
