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

## Aplicar

| Entorno | Cómo |
|---|---|
| Local | Automático al arrancar la API (`Database:MigrateOnStartup` es `true` en Development) |
| A mano | `finanzas-hub-api/tools/migrate.ps1`, que ejecuta `dotnet run --project src/PersonalFinance.Api -- migrate` |
| Despliegue | Ejecutar el mismo comando `migrate` como paso previo; `MigrateOnStartup` queda en `false` para que varias réplicas no migren a la vez |

El Worker no migra: espera a que la API o el paso de despliegue lo hagan. Un job que arranca antes de tiempo falla, lo registra y reintenta al minuto.

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
