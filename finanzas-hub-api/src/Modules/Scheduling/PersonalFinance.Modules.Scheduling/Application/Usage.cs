using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Ledger.Contracts;
using PersonalFinance.Modules.Scheduling.Application.Abstractions;

namespace PersonalFinance.Modules.Scheduling.Application;

/// <summary>What the schedule points at, so it is never deleted from under a rule (ADR-024).</summary>
internal sealed class SchedulingUsage(ISchedulingDb db) : IAccountUsage, ICategoryUsage, IWorkspaceContents
{
    public async Task<IReadOnlySet<Guid>> UsedAmongAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken) =>
        (await db.RecurringRules.AsNoTracking()
            .Where(rule => accountIds.Contains(rule.AccountId))
            .Select(rule => rule.AccountId)
            .ToListAsync(cancellationToken)).ToHashSet();

    public async Task<IReadOnlySet<Guid>> CategoriesUsedAmongAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken) =>
        (await db.RecurringRules.AsNoTracking()
            .Where(rule => categoryIds.Contains(rule.CategoryId))
            .Select(rule => rule.CategoryId)
            .ToListAsync(cancellationToken)).ToHashSet();

    public Task<bool> HasDataAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        db.RecurringRules.IgnoreQueryFilters().AnyAsync(rule => rule.WorkspaceId == workspaceId, cancellationToken);

    public Task RemoveLeftoversAsync(Guid workspaceId, CancellationToken cancellationToken) => Task.CompletedTask;
}
