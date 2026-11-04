# Módulo Workspaces

| | |
|---|---|
| Responsabilidad | Workspaces, membresías, roles, invitaciones, moneda base y zona horaria |
| Aggregate roots | `Workspace` (con `Membership`), `Invitation` |
| Value objects | `WorkspaceRole`, `WorkspaceKind` (Personal, Business, Other), `MembershipStatus`, `InvitationToken` |
| Posee | Esquema `workspaces` |
| Consulta de otros | `Identity.Contracts` (directorio de usuarios) |
| Expone | `IWorkspaceDirectory` (miembros con nombre). Implementa `IWorkspaceAccess`, definida en `BuildingBlocks.Application` y usada por el filtro de rutas, e `IPostRegistrationStep` de Identity |
| Eventos | `WorkspaceCreated` (I), `OnboardingCompleted` (I), `MemberInvited` (I), `MemberJoined` (I), `MemberRoleChanged` (D), `MemberRemoved` (I) |

Relación confirmada: `User 1:N WorkspaceMembership N:1 Workspace`. Clave única `(workspace_id, user_id)`. Exactamente un Owner por workspace.

| Caso de uso | Autorización | Entrada | Reglas | Efectos y eventos |
|---|---|---|---|---|
| CreateWorkspace | Autenticado | nombre, tipo (Personal por defecto) | Queda en `PendingSetup`; el tipo no cambia después | Creador como Owner; `WorkspaceCreated` |
| CompleteOnboarding | Owner | moneda base, zona horaria, sembrar categorías | Solo desde `PendingSetup`; moneda de la lista permitida | Pasa a `Active`; `OnboardingCompleted` |
| RenameWorkspace, ChangeTimeZone | Admin u Owner | — | — | Auditoría |
| ChangeBaseCurrency | Owner | moneda | Solo si el workspace no tiene asientos | Auditoría |
| InviteMember | Admin u Owner | correo, rol | Solo en un espacio Personal (`workspaces.kind_not_shareable`); rol ≠ Owner; no duplicar invitación pendiente; no invitar a un miembro activo | Token con caducidad de 7 días; `MemberInvited` (correo). La respuesta trae el enlace de aceptación (`acceptLink`) una sola vez, para que quien invita lo entregue por su cuenta si el correo no llega; el listado no lo repite porque el token no se guarda |
| RevokeInvitation | Admin u Owner | id | Solo pendientes | — |
| AcceptInvitation | Autenticado cuyo correo coincide | token | Vigente y no usada | Membresía activa; `MemberJoined` |
| ChangeMemberRole | Admin u Owner | usuario, rol | No sobre uno mismo; no sobre el Owner; un Admin no concede Admin | Auditoría |
| RemoveMember / LeaveWorkspace | Admin u Owner / el propio miembro | usuario | El Owner no puede salir sin transferir | Membresía `Removed`; `MemberRemoved` |
| TransferOwnership | Owner | usuario | El destino debe ser miembro activo | Auditoría |
| DeleteWorkspace | Owner | — | Nunca el personal; sin otro miembro activo; ningún módulo guarda datos de él (`IWorkspaceContents`, ADR-024) | Se borran membresías, invitaciones, categorías sugeridas y avisos; auditoría `workspaces.workspace.deleted` |
| GetWorkspaceDeletion (query) | Miembro | — | Las mismas reglas que DeleteWorkspace | `canDelete` y el código de la regla que lo impide |
| ListMembers, ListInvitations, GetWorkspace (queries) | Miembro / Admin / Miembro | — | — | — |

## Estado tras la fase 1

Implementado: crear, completar el onboarding, renombrar y cambiar zona horaria; invitar, revocar, aceptar; cambiar rol, quitar miembro, salir y transferir la propiedad; y sus consultas. Endpoints: `GET /me/workspaces`; `POST /workspaces`; `GET` y `PATCH W`; `POST W/onboarding/complete`; `POST W/ownership-transfer`; `GET W/members`; `PATCH` y `DELETE W/members/{userId}` (con el id propio, salir); `GET` y `POST W/invitations`; `DELETE W/invitations/{id}`; `GET /invitations/{token}` (anónimo) y `POST /invitations/{token}/accept`.

Diferido: `ChangeBaseCurrency` (sin fecha: desde la fase 4 cada asiento en otra moneda guarda su valor en la moneda base, y cambiarla obligaría a revalorar los de todos los miembros), archivar un workspace, e `IWorkspaceDirectory` (llega con Goals, fase 8). El evento `MemberInvited` no existe: el correo de invitación se encola directamente en el caso de uso.

- Quien se registra desde una invitación recibe su workspace personal y además se une al que le invitó, que queda como el que abre por defecto (OQ-21).
- Una invitación caducada sigue en estado pendiente; invitar de nuevo al mismo correo la revoca y crea otra.
- Solo el Owner invita o nombra administradores; un Admin gestiona miembros.
- `Workspace` lleva `xmin` como token de concurrencia sobre sus propios datos (nombre, zona horaria, propietario). Dos cambios simultáneos de miembros no chocan en ese token: afectan a filas de `memberships`.

## Espacio personal (ADR-021)

Cada persona tiene un espacio personal (`is_personal`), el que recibe al registrarse. Ahí viven sus finanzas privadas, que son las mismas en cualquier espacio de tipo Personal que tenga abierto. No se traspasa ni se elimina. `IWorkspaceAccess` lo resuelve en cada petición junto con la membresía, y el resto de la aplicación lo lee de `IBook`. Al aceptar una invitación, un espacio personal aún sin configurar toma la moneda y la zona horaria del espacio al que la persona se une. `GET /me/workspaces` dice cuál es (`isPersonal`) y de quién es cada uno (`ownerName`).

## Tipos de espacio (ADR-023)

Cada espacio tiene un tipo (`kind`), que se elige al crearlo y no cambia:

- **Personal**: el espacio personal de cada persona y cualquiera creado para compartir («Hogar», «Viaje»). No tiene libros propios: lo privado que se ve en él es el del espacio personal de quien mira. Es el único que admite miembros, invitaciones, metas y gastos compartidos.
- **Negocio** y **Otros**: libros propios, que solo usa su dueño. `Join` e `InviteMember` fallan con `workspaces.kind_not_shareable`, y crear una meta o un grupo también (`IWorkspaceContext.RequireCollaboration()`). Un negocio siembra al configurarse un catálogo de categorías propio (`DefaultCatalog.For(kind)`).

`WorkspaceAccess.GetActiveMembershipAsync` resuelve el libro: en un espacio Personal añade el espacio personal de la persona, y en uno Negocio u Otros deja el propio. El resto de la aplicación lo lee de `IBook`. `ActiveMembership.HasOwnBooks` hace que el ciclo de programados corra una vez por cada libro.

## Eliminar (ADR-024)

Un espacio se elimina solo si nunca se registró nada en él: `Workspace.CanDelete` (dueño, no el personal, sin otros miembros activos) y, después, cada módulo que implementa `IWorkspaceContents` dice si guarda algo. Categories, Notifications e Identity no impiden nada; limpian lo suyo (categorías y tags sugeridos, avisos, el espacio que abre por defecto). `GET W/deletion` responde `{ canDelete, reason }` para que la pantalla ofrezca «Eliminar» solo cuando se puede.
