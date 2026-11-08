using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.Modules.Goals.Application.Abstractions;

namespace PersonalFinance.Modules.Goals.Application;

/// <summary>Whether a workspace has goals, archived ones included, asked before deleting it (ADR-024).</summary>
internal sealed class GoalsUsage(IGoalsDb db) : IWorkspaceContents
{
    public Task<bool> HasDataAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        db.Goals.IgnoreQueryFilters().AnyAsync(goal => goal.WorkspaceId == workspaceId, cancellationToken);

    public Task RemoveLeftoversAsync(Guid workspaceId, CancellationToken cancellationToken) => Task.CompletedTask;
}
