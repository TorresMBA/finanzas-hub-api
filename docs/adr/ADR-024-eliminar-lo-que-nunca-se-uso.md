# ADR-024: Eliminar lo que nunca se usó

- Estado: aceptado
- Fecha: 2026-10-24
- Matiza: [ADR-004](ADR-004-ledger-financiero.md) («nada financiero se borra: se anula»), que sigue en pie

## Contexto

Hasta ahora nada se eliminaba. Las cuentas, categorías, tags, metas y grupos se archivaban, los programados se terminaban, los movimientos se anulaban y los espacios no tenían salida. Esto deja restos cuando alguien crea algo por error, como un negocio de prueba, un grupo vacío o una cuenta con el nombre equivocado.

Eliminar plantea dos riesgos:

1. **Dentro de un módulo** la base de datos se protege sola. Por ejemplo, `ledger.entries → accounts` es `RESTRICT`: PostgreSQL rechaza borrar una cuenta con asientos, la transacción se revierte y no se pierde nada.
2. **Entre módulos no hay claves foráneas**, por diseño ([ADR-003](ADR-003-persistencia.md): «entre módulos se guarda el id»). La base no impide borrar una cuenta a la que apunta un programado ni una categoría que usa un presupuesto. El peligro no es una caída sino **huérfanos silenciosos**: un programado que falla al día siguiente o un reporte con «Sin categoría».

## Decisión

Se puede eliminar **solo lo que nunca tuvo efecto financiero ni nada que dependa de ello**. Lo que ya se usó se archiva, se termina o se anula, como antes. Los movimientos nunca se eliminan.

| Qué | Se elimina si… | Se va con ello |
|---|---|---|
| Espacio Negocio u Otros | ningún módulo guarda datos de él: cuentas, movimientos, tipos de cambio, presupuestos, programados, inversiones, importaciones ni reglas | sus categorías y tags, sus avisos, membresía e invitaciones |
| Espacio Personal compartido | no queda otro miembro activo y no tiene metas ni grupos, tampoco archivados | invitaciones y avisos |
| Espacio personal | nunca (`workspaces.workspace.personal`) | — |
| Grupo de gastos | nunca tuvo un gasto, tampoco cancelado, ni un pago | sus miembros |
| Meta | nunca tuvo un aporte ni un retiro, y nadie tiene fondo en ella | sus participantes |
| Cuenta | no tiene asientos, tampoco anulados, no es de una meta ni de una inversión, y ningún programado ni importación apunta a ella | su perfil de tarjeta |
| Categoría | ni ella ni sus subcategorías las usan movimientos, presupuestos, programados, reglas o importaciones | sus subcategorías y sus tags sugeridos |
| Tag | ningún movimiento lo lleva | deja de sugerirse en las categorías |
| Programado | no registró ningún movimiento (omitir una fecha no cuenta) | sus fechas |

Elimina quien hoy puede archivar: el dueño de lo privado, o quien creó la meta o el grupo. Algo ajeno sigue respondiendo 404.

**Cómo se sabe si algo se usa:**
- **Dentro de un módulo**, lo responde el propio agregado (`CanDelete`, `IsUnused`).
- **Entre módulos**, cada módulo que apunta contesta por un puerto que vive en el módulo del que depende, como `IScheduleCycleStep`:
  - `IAccountUsage`, en Ledger.Contracts, lo implementan Scheduling e Integrations;
  - `ICategoryUsage`, en Categories.Contracts, lo implementan Ledger, Budgets, Scheduling e Integrations;
  - `IWorkspaceContents`, en BuildingBlocks.Application, porque Ledger, Budgets, Integrations e Investments no dependen de Workspaces. Cada módulo dice si guarda algo del espacio y limpia lo suyo cuando se elimina.
- Los puertos responden **en lote** (`UsedAmongAsync`), y así los listados dicen `canDelete` sin consultar elemento por elemento.

**Carrera.** Antes de comprobar y borrar, el handler bloquea la fila padre (`IRowLock`, `SELECT … FOR UPDATE`). Quien añade un hijo en ese momento, como un gasto al grupo o un asiento a la cuenta, toma `FOR KEY SHARE` sobre esa fila por su clave foránea:
- si el borrado bloqueó primero, el insert falla por la clave foránea y el decorador responde 409 `reference_conflict`;
- si el hijo entró primero, la comprobación lo ve y el borrado se rechaza.

Entre módulos no hay clave foránea que bloquear. El riesgo queda en que la misma persona cree, en el mismo instante, un programado sobre la cuenta que está borrando. Se acepta.

**Auditoría.** El registro `x.deleted` guarda el nombre de lo eliminado. Las entradas anteriores que apuntan a ese id siguen siendo historia válida y no se borran.

## Alternativas consideradas

- **Claves foráneas entre esquemas.** Pondrían la regla en la base, pero atan los módulos entre sí y contradicen el ADR-003.
- **Borrado lógico (`deleted_at`) en todo.** Ya existe el archivado. Un segundo estado oculto añade filtros en todas las consultas y no limpia nada.
- **Eliminar en cascada aunque tenga datos.** Rompe «nada financiero se borra».

## Consecuencias

- La API añade `DELETE` junto a cada `…/archive` (o `…/end`), y `GET /workspaces/{id}/deletion` dice si el espacio se puede eliminar y por qué no. Las respuestas de cuentas, categorías, tags, metas, grupos y programados llevan `canDelete`.
- La pantalla muestra «Eliminar» solo cuando se puede, con confirmación. Si no, deja «Archivar» o «Terminar».
- `HandlerDecorators` traduce una violación de clave foránea (`23503`) a 409 `reference_conflict`, en lugar de un 500.
- Un módulo nuevo que apunte a cuentas o categorías de otro tiene que implementar el puerto de uso correspondiente, y uno con datos de un espacio, `IWorkspaceContents`. Si no lo hace, lo que borre otro módulo puede dejarlo apuntando a nada.
