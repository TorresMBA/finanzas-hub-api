using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Workspaces.Application.Abstractions;
using PersonalFinance.Modules.Workspaces.Domain;

namespace PersonalFinance.Modules.Workspaces.Application;

/// <summary>Answers the second authorization level: is this user an active member of this workspace?</summary>
internal sealed class WorkspaceAccess(IWorkspacesDb db) : IWorkspaceAccess
{
    public async Task<WorkspaceMembershipInfo?> GetActiveMembershipAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
        await db.Memberships.AsNoTracking()
            .Where(membership => membership.WorkspaceId == workspaceId && membership.UserId == userId && membership.Status == MembershipStatus.Active)
            .Join(
                db.Workspaces.AsNoTracking().Where(workspace => workspace.Status != WorkspaceStatus.Archived),
                membership => membership.WorkspaceId,
                workspace => workspace.Id,
                (membership, workspace) => new WorkspaceMembershipInfo(workspace.Id, membership.UserId, membership.Role, workspace.BaseCurrency, workspace.TimeZone))
            .SingleOrDefaultAsync(cancellationToken);
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
