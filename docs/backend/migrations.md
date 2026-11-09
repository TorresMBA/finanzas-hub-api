# Migraciones

Hay un solo `DbContext` (`AppDbContext`, en `PersonalFinance.Infrastructure`), un solo historial de migraciones y un esquema de PostgreSQL por módulo. Las tablas que no son de ningún módulo (claves de idempotencia, auditoría) están en el esquema `platform`.

| Qué | Dónde |
|---|---|
| Contexto | `src/PersonalFinance.Infrastructure/Persistence/AppDbContext.cs` |
| Mapeos de un módulo | `src/PersonalFinance.Infrastructure/Persistence/Configurations/<Módulo>/` |
| Migraciones | `src/PersonalFinance.Infrastructure/Persistence/Migrations/` |
| Historial | Tabla `platform.__ef_migrations_history` |

## Crear una migración

```powershell
finanzas-hub-api/tools/add-migration.ps1 -Name AddLedgerAccounts
```

No se conecta a la base de datos, pero construye el host, así que la cadena de conexión tiene que estar configurada.

Revisa siempre el archivo generado: nombres en `snake_case`, cada tabla en el esquema de su módulo, y que solo aparezca lo que querías cambiar.

## Cuándo se aplican

Las migraciones solo las aplica la API, de dos formas: con el argumento `migrate` (aplica las pendientes y termina, sin servir peticiones) o al arrancar si `Database:MigrateOnStartup=true`. Las dos llaman a `MigrateDatabaseAsync` (`PersonalFinance.Infrastructure/DependencyInjection.cs`). **El Worker nunca migra**: si arranca con migraciones pendientes, sus jobs fallan, lo registran y reintentan al minuto.

| Dónde | Cuándo | Cómo |
|---|---|---|
| Tu máquina | Cada vez que arranca la API | `Database:MigrateOnStartup` es `true` en `appsettings.Development.json` |
| Tu máquina, a mano | Cuando quieras, sin arrancar la API | `tools/migrate.ps1` (= `dotnet run --project src/PersonalFinance.Api -- migrate`) |
| Tests de integración | Al empezar la ejecución, sobre su base desechable | `ApiFixture` llama a `MigrateDatabaseAsync` (y fuerza `MigrateOnStartup=false`) |
| Jenkins, **dev** | En cada build, en «Deploy dev», justo después de publicar las imágenes | `deployAll('dev')` del `Jenkinsfile` |
| Jenkins, **prod** | En «Deploy prod», solo después de aprobar «Aprobar prod» | `deployAll('prod')`, el mismo paso que en dev |
| Producción, a mano | Solo si el pipeline no puede | [operations.md](../devops/operations.md#migraciones-a-mano) |

En producción `MigrateOnStartup` es `false` (`appsettings.json`): la API no migra al arrancar. Así nunca hay dos procesos migrando a la vez y una migración que falla no deja la API a medio arrancar.

### Qué hace el pipeline en cada despliegue

`deployAll(<env>)`, en este orden, y se para en el primer error:

1. Comprueba que existen `/srv/mercury/apps/<env>/finanzas-hub-api.env` y `finanzas-hub-worker.env`.
2. Descarga la imagen **nueva** de la API.
3. **Migra**: `docker compose run --rm app migrate` con el compose de despliegue del servidor, es decir, un contenedor de un solo uso con la imagen nueva, el `.env` del ambiente y su red de base de datos. No toca la API en marcha. Si existe `finanzas-hub-migrate.env`, usa su cadena de conexión (la del dueño de las tablas, cuando la app corre con el rol de `tools/db-roles.sql`); si no, la de la API.
4. Despliega la API y después el Worker con la imagen nueva.

Si la migración falla, no se despliega nada: siguen la API y el Worker anteriores. Cada migración corre en su transacción, así que la que falla se revierte; si había varias pendientes, las anteriores a ella quedan aplicadas, y por eso una migración no debe romper el código de la versión anterior (añadir antes de quitar). Como dev migra antes de pedir la aprobación, una migración que falla en dev nunca llega a prod.

Las migraciones solo van hacia delante; el pipeline no hace copias de la base, que son cosa del servidor ([operations.md](../devops/operations.md#copias-de-seguridad)). Una migración delicada (que transforma datos o borra columnas) se acompaña de una copia a mano antes de aprobar prod.

`/health/ready` responde no saludable mientras haya migraciones pendientes. Si el modelo cambió y falta la migración, `migrate` falla con `PendingModelChangesWarning`, y con él todos los tests de integración.

## Reglas

1. **Cada tabla en el esquema de su módulo.** La configuración lo declara (`builder.ToTable("accounts", Schemas.Ledger)`) y un test lo comprueba.
2. **Entre módulos, id sin propiedad de navegación.** Un módulo no conoce las entidades de otro. La FK en base de datos sí se puede declarar desde la configuración (`HasOne<Operation>().WithMany().HasForeignKey(...)`) cuando protege un enlace de dinero, con `DeleteBehavior.Restrict`.
3. **Lo referenciado no se borra**: se anula o se archiva.
4. **Dinero** en `numeric(18,2)`, moneda en `char(3)`, fecha de negocio en `date`, instantes en `timestamptz`, enumerados como texto con `CHECK`.
5. **Una migración que transforma datos** se prueba contra PostgreSQL real, también su `Down`, antes de fusionarla.
6. **No se edita una migración ya aplicada** en algún entorno. Se añade otra.

## Entidades privadas

Una entidad que implementa `IOwned` recibe automáticamente el filtro global por usuario y workspace; no hay que configurarlo. Lo que sí hay que añadir es un índice que empiece por `(workspace_id, owner_user_id)` para sus consultas habituales.

## Quitar un módulo

1. Quitar sus dos líneas de `DependencyInjection.cs`, su `IXDb` de `AppDbContext`, su carpeta de configuraciones y la referencia al proyecto.
2. Quitar sus controllers y sus jobs.
3. Crear la migración, que elimina sus tablas y las FKs que apuntaban a ellas.
4. Borrar el proyecto y su entrada en `ModuleBoundaryTests.AllowedDependencies`.

## Base local creada antes del rediseño

La fase 0 tuvo primero un historial de migraciones por esquema. Una base local de entonces no es compatible: bórrala (`DROP DATABASE personal_hub`) y deja que la API la cree de nuevo.
