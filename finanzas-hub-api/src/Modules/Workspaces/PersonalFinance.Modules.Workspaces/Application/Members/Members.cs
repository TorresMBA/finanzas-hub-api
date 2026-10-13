using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Identity.Contracts;
using PersonalFinance.Modules.Workspaces.Application.Abstractions;
using PersonalFinance.Modules.Workspaces.Domain;

namespace PersonalFinance.Modules.Workspaces.Application.Members;

public sealed record MemberResponse(Guid UserId, string DisplayName, string Email, WorkspaceRole Role, DateTimeOffset JoinedAt, bool IsYou);

public sealed record ListMembersQuery : IQuery<IReadOnlyList<MemberResponse>>;

internal sealed class ListMembersHandler(IWorkspacesDb db, IWorkspaceContext context, ICurrentUser currentUser, IUserDirectory users)
    : IQueryHandler<ListMembersQuery, IReadOnlyList<MemberResponse>>
{
    public async Task<Result<IReadOnlyList<MemberResponse>>> HandleAsync(ListMembersQuery query, CancellationToken cancellationToken)
    {
        if (await db.CurrentAsync(context, cancellationToken) is not { } workspace)
        {
            return WorkspaceErrors.NotFound;
        }

        var active = workspace.Memberships.Where(membership => membership.Status == MembershipStatus.Active).ToList();
        var people = await users.GetAsync(active.Select(membership => membership.UserId).ToList(), cancellationToken);
        return active
            .Where(membership => people.ContainsKey(membership.UserId))
            .OrderByDescending(membership => membership.Role)
            .ThenBy(membership => membership.JoinedAt)
            .Select(membership => new MemberResponse(
                membership.UserId, people[membership.UserId].DisplayName, people[membership.UserId].Email,
                membership.Role, membership.JoinedAt, membership.UserId == currentUser.UserId))
            .ToList();
    }
}

public sealed record ChangeMemberRoleCommand(Guid UserId, WorkspaceRole Role) : ICommand<Unit>;

internal sealed class ChangeMemberRoleHandler(IWorkspacesDb db, IWorkspaceContext context, ICurrentUser currentUser, IAuditLog audit)
    : ICommandHandler<ChangeMemberRoleCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ChangeMemberRoleCommand command, CancellationToken cancellationToken)
    {
        var allowed = context.Require(WorkspaceRole.Admin);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        if (currentUser.UserId is not { } actor || await db.CurrentAsync(context, cancellationToken) is not { } workspace)
        {
            return WorkspaceErrors.NotFound;
        }

        var before = workspace.ActiveMembershipOf(command.UserId)?.Role;
        var changed = workspace.ChangeRole(actor, command.UserId, command.Role);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        audit.Record(new AuditEntry("workspaces", "workspaces.member.role_changed", "membership", command.UserId)
        {
            Before = new { Role = before?.ToString() },
            After = new { Role = command.Role.ToString() }
        });
        return Unit.Value;
    }
}

/// <summary>Removes a member. When the target is the caller, the caller leaves the workspace.</summary>
public sealed record RemoveMemberCommand(Guid UserId) : ICommand<Unit>;

internal sealed class RemoveMemberHandler(IWorkspacesDb db, IWorkspaceContext context, ICurrentUser currentUser, IAuditLog audit, IClock clock)
    : ICommandHandler<RemoveMemberCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RemoveMemberCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor || await db.CurrentAsync(context, cancellationToken) is not { } workspace)
        {
            return WorkspaceErrors.NotFound;
        }

        var removed = workspace.Remove(actor, command.UserId, clock.UtcNow);
        if (removed.IsFailure)
        {
            return removed.Error;
        }

        audit.Record(new AuditEntry("workspaces", actor == command.UserId ? "workspaces.member.left" : "workspaces.member.removed", "membership", command.UserId));
        return Unit.Value;
    }
}

public sealed record TransferOwnershipCommand(Guid UserId) : ICommand<Unit>;

internal sealed class TransferOwnershipHandler(IWorkspacesDb db, IWorkspaceContext context, ICurrentUser currentUser, IAuditLog audit)
    : ICommandHandler<TransferOwnershipCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(TransferOwnershipCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor || await db.CurrentAsync(context, cancellationToken) is not { } workspace)
        {
            return WorkspaceErrors.NotFound;
        }

        var transferred = workspace.TransferOwnership(actor, command.UserId);
        if (transferred.IsFailure)
        {
            return transferred.Error;
        }

        audit.Record(new AuditEntry("workspaces", "workspaces.workspace.ownership_transferred", "workspace", workspace.Id)
        {
            Before = new { OwnerUserId = actor },
            After = new { OwnerUserId = command.UserId }
        });
        return Unit.Value;
    }
}
