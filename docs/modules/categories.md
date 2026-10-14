# Módulo Categories

| | |
|---|---|
| Responsabilidad | Categorías, subcategorías, tags y tags sugeridos por categoría |
| Aggregate roots | `Category`, `Tag` |
| Value objects | `CategoryKind` (Income, Expense), `TagName` normalizado, `IconKey`, `ColorKey` |
| Posee | Esquema `categories`, más el catálogo de plantillas por defecto |
| Consulta de otros | `Workspaces.Contracts` (reacciona a `OnboardingCompleted` y `MemberJoined` para sembrar las categorías) |
| Expone | `ICategoryLookup` (existe, tipo, raíz, árbol), `ITagLookup` |
| Eventos | `CategoryArchived` (I); el resto (D) |

| Caso de uso | Autorización | Entrada | Reglas | Efectos y eventos |
|---|---|---|---|---|
| SeedDefaultCategories (Sistema, al `OnboardingCompleted` y al `MemberJoined`) | — | workspace, usuario | Idempotente por `system_key` | Copia el catálogo al usuario |
| CreateCategory | Dueño | tipo, nombre, padre opcional, icono, color, tags | Máximo 2 niveles; el hijo hereda tipo; nombre único entre activas del mismo padre | — |
| UpdateCategory | Dueño | nombre, icono, color, padre | El tipo no cambia; mover solo entre padres del mismo tipo | — |
| ArchiveCategory / RestoreCategory | Dueño | id | Nunca borrado físico; archivar una raíz archiva sus hijas | `CategoryArchived` |
| CreateTag, RenameTag, ArchiveTag | Dueño | nombre | Normaliza a minúsculas con guiones; único por dueño | — |
| SetCategoryTags | Dueño | categoría, tags | Sin repetidos | — |
| ListCategories, ListTags (queries) | Dueño | tipo, incluir archivadas | — | — |

## Estado tras la fase 2

Implementado: sembrar las categorías por defecto, crear categoría y subcategoría, editar (nombre, icono, color), archivar y restaurar, asignar los tags de una categoría, archivar un tag, y listar categorías y tags. Endpoints: `GET` y `POST W/categories`; `PATCH W/categories/{id}`; `POST W/categories/{id}/archive` y `/restore`; `PUT W/categories/{id}/tags`; `POST W/categories/defaults`; `GET W/tags`; `POST W/tags/{id}/archive`.

Diferido: mover una categoría a otro padre, `RenameTag` y el evento `CategoryArchived` (fase 7, con Scheduling).

Desde la fase 3 el módulo expone `ICategoryLookup` (una categoría, varias, y una con sus subcategorías) e `ITagLookup` (resolver nombres creando los que falten, nombres por id, y buscar por nombre). Los usa el ledger; ambos pasan por el filtro de dueño.

- Categorías y tags son privados: llevan `workspace_id` y `owner_user_id` y el filtro global los aísla. Dos miembros del mismo workspace tienen cada uno los suyos, y ningún rol ve los de otro.
- Solo hay dos niveles. Una subcategoría toma el tipo, el icono, el color y los tags sugeridos de su categoría y no guarda ninguno; por eso los tags se asignan solo a categorías de primer nivel.
- El nombre es único entre hermanas activas, sin distinguir mayúsculas. El de una archivada queda libre, así que restaurarla puede dar conflicto.
- Archivar una categoría archiva sus subcategorías con el mismo instante; restaurarla recupera las que tienen ese instante y deja archivadas las que lo estaban de antes. Una subcategoría no se restaura mientras su categoría siga archivada.
- Un tag se crea al nombrarlo en una categoría. Se guarda normalizado: minúsculas, sin `#` inicial y con guiones en lugar de espacios. Nombrar uno archivado lo recupera.
- Archivar un tag lo quita de todas las categorías que lo sugerían.
- La siembra copia el catálogo de `Domain/DefaultCatalog.cs` y se puede repetir: salta lo que la persona ya recibió (por `system_key`, aunque lo haya renombrado o archivado) y lo que choca con un nombre que ya usa. Corre al completar el onboarding (para el Owner) y al unirse un miembro; como quien se une desde una invitación aún no tiene sesión, es el único código que usa `IgnoreQueryFilters`, con el workspace y el usuario explícitos.
- Cambiar el catálogo no cambia lo que alguien ya tiene. Una clave del catálogo no se reutiliza para otra cosa.

## Tras el ADR-021

Las categorías y los tags son de la persona y viven en su espacio personal. El catálogo sugerido se siembra solo al configurar ese espacio; unirse a otro espacio ya no siembra nada.
