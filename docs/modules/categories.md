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
