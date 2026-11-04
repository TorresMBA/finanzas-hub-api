using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Identity.Contracts;
using PersonalFinance.Modules.Workspaces.Application.Abstractions;
using PersonalFinance.Modules.Workspaces.Domain;

namespace PersonalFinance.Modules.Workspaces.Application.Workspaces;

/// <param name="Role">The role of the person asking, not a property of the workspace.</param>
/// <param name="IsPersonal">
/// The workspace the person got with their account. The private finances of its owner live there and are the
/// same whichever workspace is open; the others only add what is shared.
/// </param>
/// <param name="Kind">
/// What the workspace is for. Only a Personal one has goals, shared expenses and other members; a Business or Other
/// one keeps its own books, which only its owner uses.
/// </param>
/// <param name="OwnerName">
/// Who owns the workspace. Only in the list of the workspaces of a person, where two called the same ("Personal",
/// theirs and the one they were invited to) have to be told apart; null elsewhere.
/// </param>
public sealed record WorkspaceResponse(
    Guid Id, string Name, string? BaseCurrency, string TimeZone, WorkspaceStatus Status, WorkspaceRole Role, int MemberCount, bool IsPersonal,
    WorkspaceKind Kind, string? OwnerName = null)
{
    internal static WorkspaceResponse From(Workspace workspace, WorkspaceRole role, string? ownerName = null) => new(
        workspace.Id, workspace.Name, workspace.BaseCurrency, workspace.TimeZone, workspace.Status, role,
        workspace.Memberships.Count(membership => membership.Status == MembershipStatus.Active), workspace.IsPersonal, workspace.Kind, ownerName);
}

public sealed record GetMyWorkspacesQuery : IQuery<IReadOnlyList<WorkspaceResponse>>;

internal sealed class GetMyWorkspacesHandler(IWorkspacesDb db, ICurrentUser currentUser, IUserDirectory users) : IQueryHandler<GetMyWorkspacesQuery, IReadOnlyList<WorkspaceResponse>>
{
    public async Task<Result<IReadOnlyList<WorkspaceResponse>>> HandleAsync(GetMyWorkspacesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return WorkspaceErrors.Unauthenticated;
        }

        var workspaces = await db.Workspaces.AsNoTracking()
            .Include(workspace => workspace.Memberships)
            .Where(workspace => workspace.Status != WorkspaceStatus.Archived
                && workspace.Memberships.Any(membership => membership.UserId == userId && membership.Status == MembershipStatus.Active))
            .OrderBy(workspace => workspace.CreatedAt)
            .ToListAsync(cancellationToken);
        var owners = await users.GetAsync(workspaces.Select(workspace => workspace.OwnerUserId).Distinct().ToList(), cancellationToken);
        return workspaces
            .Select(workspace => WorkspaceResponse.From(
                workspace, workspace.ActiveMembershipOf(userId)!.Role, owners.GetValueOrDefault(workspace.OwnerUserId)?.DisplayName))
            .ToList();
    }
}

public sealed record GetWorkspaceQuery : IQuery<WorkspaceResponse>;

internal sealed class GetWorkspaceHandler(IWorkspacesDb db, IWorkspaceContext context) : IQueryHandler<GetWorkspaceQuery, WorkspaceResponse>
{
    public async Task<Result<WorkspaceResponse>> HandleAsync(GetWorkspaceQuery query, CancellationToken cancellationToken)
    {
        var workspace = await db.Workspaces.AsNoTracking().Include(row => row.Memberships)
            .SingleOrDefaultAsync(row => row.Id == context.WorkspaceId, cancellationToken);
        return workspace is null || context.Role is not { } role ? WorkspaceErrors.NotFound : WorkspaceResponse.From(workspace, role);
    }
}

public sealed record CreateWorkspaceCommand(string Name, WorkspaceKind Kind = WorkspaceKind.Personal) : ICommand<WorkspaceResponse>;

internal sealed class CreateWorkspaceHandler(IWorkspacesDb db, ICurrentUser currentUser, IAuditLog audit, IClock clock) : ICommandHandler<CreateWorkspaceCommand, WorkspaceResponse>
{
    public Task<Result<WorkspaceResponse>> HandleAsync(CreateWorkspaceCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Task.FromResult<Result<WorkspaceResponse>>(WorkspaceErrors.Unauthenticated);
        }

        var created = Workspace.Create(command.Name, userId, clock.UtcNow, command.Kind);
        if (created.IsFailure)
        {
            return Task.FromResult<Result<WorkspaceResponse>>(created.Error);
        }

        var workspace = created.Value;
        db.Workspaces.Add(workspace);
        audit.Record(new AuditEntry("workspaces", "workspaces.workspace.created", "workspace", workspace.Id) { WorkspaceId = workspace.Id, After = new { workspace.Name, Kind = workspace.Kind.ToString() } });
        return Task.FromResult<Result<WorkspaceResponse>>(WorkspaceResponse.From(workspace, WorkspaceRole.Owner));
    }
}

public sealed record CompleteOnboardingCommand(string BaseCurrency, string TimeZone) : ICommand<WorkspaceResponse>;

internal sealed class CompleteOnboardingHandler(IWorkspacesDb db, IWorkspaceContext context, IAuditLog audit) : ICommandHandler<CompleteOnboardingCommand, WorkspaceResponse>
{
    public async Task<Result<WorkspaceResponse>> HandleAsync(CompleteOnboardingCommand command, CancellationToken cancellationToken)
    {
        var allowed = context.Require(WorkspaceRole.Owner);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        if (await db.CurrentAsync(context, cancellationToken) is not { } workspace)
        {
            return WorkspaceErrors.NotFound;
        }

        var completed = workspace.CompleteOnboarding(command.BaseCurrency, command.TimeZone);
        if (completed.IsFailure)
        {
            return completed.Error;
        }

        audit.Record(new AuditEntry("workspaces", "workspaces.workspace.onboarding_completed", "workspace", workspace.Id)
        {
            After = new { workspace.BaseCurrency, workspace.TimeZone }
        });
        return WorkspaceResponse.From(workspace, WorkspaceRole.Owner);
    }
}

public sealed record UpdateWorkspaceCommand(string Name, string TimeZone) : ICommand<WorkspaceResponse>;

internal sealed class UpdateWorkspaceHandler(IWorkspacesDb db, IWorkspaceContext context, IAuditLog audit) : ICommandHandler<UpdateWorkspaceCommand, WorkspaceResponse>
{
    public async Task<Result<WorkspaceResponse>> HandleAsync(UpdateWorkspaceCommand command, CancellationToken cancellationToken)
    {
        var allowed = context.Require(WorkspaceRole.Admin);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        if (await db.CurrentAsync(context, cancellationToken) is not { } workspace)
        {
            return WorkspaceErrors.NotFound;
        }

        var before = new { workspace.Name, workspace.TimeZone };
        var updated = workspace.Update(command.Name, command.TimeZone);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        audit.Record(new AuditEntry("workspaces", "workspaces.workspace.updated", "workspace", workspace.Id)
        {
            Before = before,
            After = new { workspace.Name, workspace.TimeZone }
        });
        return WorkspaceResponse.From(workspace, context.Role!.Value);
    }
}

/// <summary>Whether the person asking can delete the workspace now, and why not when they cannot.</summary>
/// <param name="Reason">The code of the rule that stops it, the same one deleting would answer. Null when it can be deleted.</param>
public sealed record WorkspaceDeletionResponse(bool CanDelete, string? Reason);

/// <summary>
/// Decides whether a workspace can go (ADR-024): the workspace's own rules first, then every module that keeps
/// something of it. Categories, notices and invitations do not count: they go with it.
/// </summary>
internal sealed class WorkspaceDeletion(IEnumerable<IWorkspaceContents> modules)
{
    public static readonly Error NotEmpty =
        Error.BusinessRule("workspaces.workspace.not_empty", "The workspace has accounts, movements, budgets, goals or other data. Archive it instead.");

    public async Task<Result> CheckAsync(Workspace workspace, Guid actorUserId, CancellationToken cancellationToken)
    {
        var allowed = workspace.CanDelete(actorUserId);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        foreach (var module in modules)
        {
            if (await module.HasDataAsync(workspace.Id, cancellationToken))
            {
                return NotEmpty;
            }
        }

        return Result.Success();
    }

    public async Task RemoveLeftoversAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        foreach (var module in modules)
        {
            await module.RemoveLeftoversAsync(workspaceId, cancellationToken);
        }
    }
}

public sealed record GetWorkspaceDeletionQuery : IQuery<WorkspaceDeletionResponse>;

internal sealed class GetWorkspaceDeletionHandler(IWorkspacesDb db, IWorkspaceContext context, ICurrentUser currentUser, WorkspaceDeletion deletion)
    : IQueryHandler<GetWorkspaceDeletionQuery, WorkspaceDeletionResponse>
{
    public async Task<Result<WorkspaceDeletionResponse>> HandleAsync(GetWorkspaceDeletionQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || await db.CurrentAsync(context, cancellationToken) is not { } workspace)
        {
            return WorkspaceErrors.NotFound;
        }

        var allowed = await deletion.CheckAsync(workspace, userId, cancellationToken);
        return new WorkspaceDeletionResponse(allowed.IsSuccess, allowed.IsFailure ? allowed.Error.Code : null);
    }
}

/// <summary>Deletes a workspace nothing was registered in (ADR-024), with what it set up on its own. One with data is archived instead.</summary>
public sealed record DeleteWorkspaceCommand : ICommand<Unit>;

internal sealed class DeleteWorkspaceHandler(
    IWorkspacesDb db, IWorkspaceContext context, ICurrentUser currentUser, WorkspaceDeletion deletion, IRowLock rows, IAuditLog audit)
    : ICommandHandler<DeleteWorkspaceCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeleteWorkspaceCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || context.WorkspaceId is not { } workspaceId)
        {
            return WorkspaceErrors.NotFound;
        }

        // Locked before reading, so nobody joins it meanwhile.
        await rows.LockAsync<Workspace>(workspaceId, cancellationToken);
        if (await db.CurrentAsync(context, cancellationToken) is not { } workspace)
        {
            return WorkspaceErrors.NotFound;
        }

        var allowed = await deletion.CheckAsync(workspace, userId, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        await deletion.RemoveLeftoversAsync(workspace.Id, cancellationToken);
        db.Workspaces.Remove(workspace);
        audit.Record(new AuditEntry("workspaces", "workspaces.workspace.deleted", "workspace", workspace.Id)
        {
            Before = new { workspace.Name, Kind = workspace.Kind.ToString() }
        });
        return Unit.Value;
    }
}
