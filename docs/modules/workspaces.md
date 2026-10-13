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
