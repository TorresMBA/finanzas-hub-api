using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Identity.Contracts;
using PersonalFinance.Modules.Workspaces.Application.Abstractions;
using PersonalFinance.Modules.Workspaces.Application.Invitations;
using PersonalFinance.Modules.Workspaces.Domain;

namespace PersonalFinance.Modules.Workspaces.Application.Registration;

/// <summary>
/// Every account gets a personal workspace, inside the registration's own transaction. Someone who registered
/// from an invitation also joins that workspace and opens on it, and still gets a personal one.
/// </summary>
internal sealed class PersonalWorkspaceOnRegistration(IWorkspacesDb db, InvitationAcceptance acceptance, IAuditLog audit, IClock clock) : IPostRegistrationStep
{
    public const string PersonalWorkspaceName = "Personal";

    public async Task<Result<Guid>> OnRegisteredAsync(NewUser user, CancellationToken cancellationToken)
    {
        var created = Workspace.Create(PersonalWorkspaceName, user.Id, clock.UtcNow);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var personal = created.Value;
        db.Workspaces.Add(personal);
        audit.Record(new AuditEntry("workspaces", "workspaces.workspace.created", "workspace", personal.Id)
        {
            WorkspaceId = personal.Id,
            OwnerUserId = user.Id,
            After = new { personal.Name }
        });

        if (string.IsNullOrWhiteSpace(user.InvitationToken))
        {
            return personal.Id;
        }

        var accepted = await acceptance.AcceptAsync(user.InvitationToken, user.Id, user.Email, cancellationToken);
        return accepted.IsFailure ? accepted.Error : accepted.Value.WorkspaceId;
    }
}
