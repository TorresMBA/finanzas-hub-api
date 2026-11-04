using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.Modules.SharedExpenses.Application.Abstractions;

namespace PersonalFinance.Modules.SharedExpenses.Application;

/// <summary>Whether a workspace has shared groups, archived ones included, asked before deleting it (ADR-024).</summary>
internal sealed class SharedExpensesUsage(ISharedExpensesDb db) : IWorkspaceContents
{
    public Task<bool> HasDataAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        db.SharedGroups.IgnoreQueryFilters().AnyAsync(group => group.WorkspaceId == workspaceId, cancellationToken);

    public Task RemoveLeftoversAsync(Guid workspaceId, CancellationToken cancellationToken) => Task.CompletedTask;
}
