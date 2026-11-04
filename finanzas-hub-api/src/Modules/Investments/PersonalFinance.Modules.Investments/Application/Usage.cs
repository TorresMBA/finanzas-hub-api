using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.Modules.Investments.Application.Abstractions;

namespace PersonalFinance.Modules.Investments.Application;

/// <summary>Whether a workspace has investments, asked before deleting it (ADR-024).</summary>
internal sealed class InvestmentsUsage(IInvestmentsDb db) : IWorkspaceContents
{
    public Task<bool> HasDataAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        db.Holdings.IgnoreQueryFilters().AnyAsync(holding => holding.WorkspaceId == workspaceId, cancellationToken);

    public Task RemoveLeftoversAsync(Guid workspaceId, CancellationToken cancellationToken) => Task.CompletedTask;
}
