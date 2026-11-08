using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.Modules.Budgets.Application.Abstractions;
using PersonalFinance.Modules.Categories.Contracts;

namespace PersonalFinance.Modules.Budgets.Application;

/// <summary>What budgets point at, so a category is never deleted from under one, ended or not (ADR-024).</summary>
internal sealed class BudgetsUsage(IBudgetsDb db) : ICategoryUsage, IWorkspaceContents
{
    public async Task<IReadOnlySet<Guid>> CategoriesUsedAmongAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken) =>
        (await db.Budgets.AsNoTracking()
            .Where(budget => categoryIds.Contains(budget.CategoryId))
            .Select(budget => budget.CategoryId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

    public Task<bool> HasDataAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        db.Budgets.IgnoreQueryFilters().AnyAsync(budget => budget.WorkspaceId == workspaceId, cancellationToken);

    public Task RemoveLeftoversAsync(Guid workspaceId, CancellationToken cancellationToken) => Task.CompletedTask;
}
