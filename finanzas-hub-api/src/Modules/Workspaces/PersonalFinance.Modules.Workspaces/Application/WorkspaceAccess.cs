using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Identity.Contracts;
using PersonalFinance.Modules.Workspaces.Application.Abstractions;
using PersonalFinance.Modules.Workspaces.Contracts;
using PersonalFinance.Modules.Workspaces.Domain;

namespace PersonalFinance.Modules.Workspaces.Application;

/// <summary>Answers the second authorization level: is this user an active member of this workspace?</summary>
internal sealed class WorkspaceAccess(IWorkspacesDb db) : IWorkspaceAccess
{
    public async Task<WorkspaceMembershipInfo?> GetActiveMembershipAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken)
    {
        var membership = await MembershipAsync(workspaceId, userId, cancellationToken);
        if (membership is null)
        {
            return null;
        }

        // Whichever workspace is open, the private finances of the person are those of their personal one.
        var personal = await db.Workspaces.AsNoTracking()
            .Where(workspace => workspace.IsPersonal && workspace.OwnerUserId == userId && workspace.Status != WorkspaceStatus.Archived)
            .Select(workspace => new PersonalSpaceInfo(workspace.Id, workspace.BaseCurrency, workspace.TimeZone))
            .FirstOrDefaultAsync(cancellationToken);
        return membership with { Personal = personal };
    }

    public async Task<WorkspaceMembershipInfo?> GetPersonalMembershipAsync(Guid userId, CancellationToken cancellationToken)
    {
        var personalId = await db.Workspaces.AsNoTracking()
            .Where(workspace => workspace.IsPersonal && workspace.OwnerUserId == userId && workspace.Status != WorkspaceStatus.Archived)
            .Select(workspace => (Guid?)workspace.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return personalId is { } id ? await GetActiveMembershipAsync(id, userId, cancellationToken) : null;
    }

    private async Task<WorkspaceMembershipInfo?> MembershipAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
        await db.Memberships.AsNoTracking()
            .Where(membership => membership.WorkspaceId == workspaceId && membership.UserId == userId && membership.Status == MembershipStatus.Active)
            .Join(
                db.Workspaces.AsNoTracking().Where(workspace => workspace.Status != WorkspaceStatus.Archived),
                membership => membership.WorkspaceId,
                workspace => workspace.Id,
                (membership, workspace) => new WorkspaceMembershipInfo(workspace.Id, membership.UserId, membership.Role, workspace.BaseCurrency, workspace.TimeZone))
            .SingleOrDefaultAsync(cancellationToken);
}

internal sealed class WorkspaceDirectory(IWorkspacesDb db, IUserDirectory users) : IWorkspaceDirectory
{
    public async Task<IReadOnlyList<WorkspaceMember>> ListMembersAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var userIds = await db.Memberships.AsNoTracking()
            .Where(membership => membership.WorkspaceId == workspaceId && membership.Status == MembershipStatus.Active)
            .Select(membership => membership.UserId)
            .ToListAsync(cancellationToken);
        var known = await users.GetAsync(userIds, cancellationToken);
        return userIds
            .Select(userId => new WorkspaceMember(userId, known.GetValueOrDefault(userId)?.DisplayName ?? string.Empty))
            .OrderBy(member => member.DisplayName, StringComparer.InvariantCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<ActiveMembership>> ListActiveMembershipsAsync(CancellationToken cancellationToken) =>
        await db.Memberships.AsNoTracking()
            .Where(membership => membership.Status == MembershipStatus.Active)
            .Join(
                db.Workspaces.AsNoTracking().Where(workspace => workspace.Status != WorkspaceStatus.Archived),
                membership => membership.WorkspaceId,
                workspace => workspace.Id,
                (membership, workspace) => new ActiveMembership(
                    workspace.Id, membership.UserId, membership.Role, workspace.BaseCurrency, workspace.TimeZone,
                    workspace.IsPersonal && workspace.OwnerUserId == membership.UserId))
            .ToListAsync(cancellationToken);
}

internal static class WorkspaceErrors
{
    public static readonly Error Unauthenticated = Error.Unauthorized("auth.unauthenticated", "Authentication is required.");

    public static readonly Error NotFound = Error.NotFound("workspace.not_found", "The workspace was not found.");
}

internal static class WorkspaceQueries
{
    /// <summary>The workspace of the current request, with its members. The route filter already proved membership.</summary>
    public static Task<Workspace?> CurrentAsync(this IWorkspacesDb db, IWorkspaceContext context, CancellationToken cancellationToken) =>
        db.Workspaces.Include(workspace => workspace.Memberships)
            .SingleOrDefaultAsync(workspace => workspace.Id == context.WorkspaceId, cancellationToken);
}
