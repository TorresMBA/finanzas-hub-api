# Módulo Workspaces

| | |
|---|---|
| Responsabilidad | Workspaces, membresías, roles, invitaciones, moneda base y zona horaria |
| Aggregate roots | `Workspace` (con `Membership`), `Invitation` |
| Value objects | `WorkspaceRole`, `MembershipStatus`, `InvitationToken` |
| Posee | Esquema `workspaces` |
| Consulta de otros | `Identity.Contracts` (directorio de usuarios) |
| Expone | `IWorkspaceDirectory` (miembros con nombre). Implementa `IWorkspaceAccess`, definida en `BuildingBlocks.Application` y usada por el filtro de rutas, e `IPostRegistrationStep` de Identity |
| Eventos | `WorkspaceCreated` (I), `OnboardingCompleted` (I), `MemberInvited` (I), `MemberJoined` (I), `MemberRoleChanged` (D), `MemberRemoved` (I) |

Relación confirmada: `User 1:N WorkspaceMembership N:1 Workspace`. Clave única `(workspace_id, user_id)`. Exactamente un Owner por workspace.

| Caso de uso | Autorización | Entrada | Reglas | Efectos y eventos |
|---|---|---|---|---|
| CreateWorkspace | Autenticado | nombre | Queda en `PendingSetup` | Creador como Owner; `WorkspaceCreated` |
| CompleteOnboarding | Owner | moneda base, zona horaria, sembrar categorías | Solo desde `PendingSetup`; moneda de la lista permitida | Pasa a `Active`; `OnboardingCompleted` |
| RenameWorkspace, ChangeTimeZone | Admin u Owner | — | — | Auditoría |
| ChangeBaseCurrency | Owner | moneda | Solo si el workspace no tiene asientos | Auditoría |
| InviteMember | Admin u Owner | correo, rol | Rol ≠ Owner; no duplicar invitación pendiente; no invitar a un miembro activo | Token con caducidad de 7 días; `MemberInvited` (correo) |
| RevokeInvitation | Admin u Owner | id | Solo pendientes | — |
| AcceptInvitation | Autenticado cuyo correo coincide | token | Vigente y no usada | Membresía activa; `MemberJoined` |
| ChangeMemberRole | Admin u Owner | usuario, rol | No sobre uno mismo; no sobre el Owner; un Admin no concede Admin | Auditoría |
| RemoveMember / LeaveWorkspace | Admin u Owner / el propio miembro | usuario | El Owner no puede salir sin transferir | Membresía `Removed`; `MemberRemoved` |
| TransferOwnership | Owner | usuario | El destino debe ser miembro activo | Auditoría |
| ListMembers, ListInvitations, GetWorkspace (queries) | Miembro / Admin / Miembro | — | — | — |

## Estado tras la fase 1

Implementado: crear, completar el onboarding, renombrar y cambiar zona horaria; invitar, revocar, aceptar; cambiar rol, quitar miembro, salir y transferir la propiedad; y sus consultas. Endpoints: `GET /me/workspaces`; `POST /workspaces`; `GET` y `PATCH W`; `POST W/onboarding/complete`; `POST W/ownership-transfer`; `GET W/members`; `PATCH` y `DELETE W/members/{userId}` (con el id propio, salir); `GET` y `POST W/invitations`; `DELETE W/invitations/{id}`; `GET /invitations/{token}` (anónimo) y `POST /invitations/{token}/accept`.

Diferido: `ChangeBaseCurrency` (sin fecha: desde la fase 4 cada asiento en otra moneda guarda su valor en la moneda base, y cambiarla obligaría a revalorar los de todos los miembros), archivar un workspace, e `IWorkspaceDirectory` (llega con Goals, fase 8). El evento `MemberInvited` no existe: el correo de invitación se encola directamente en el caso de uso.

- Quien se registra desde una invitación recibe su workspace personal y además se une al que le invitó, que queda como el que abre por defecto (OQ-21).
- Una invitación caducada sigue en estado pendiente; invitar de nuevo al mismo correo la revoca y crea otra.
- Solo el Owner invita o nombra administradores; un Admin gestiona miembros.
- `Workspace` lleva `xmin` como token de concurrencia sobre sus propios datos (nombre, zona horaria, propietario). Dos cambios simultáneos de miembros no chocan en ese token: afectan a filas de `memberships`.
