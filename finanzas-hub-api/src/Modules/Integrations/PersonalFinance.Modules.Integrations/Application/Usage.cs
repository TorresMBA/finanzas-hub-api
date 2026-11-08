using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Integrations.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Contracts;

namespace PersonalFinance.Modules.Integrations.Application;

/// <summary>What imports and classification rules point at, so it is never deleted from under them (ADR-024).</summary>
internal sealed class IntegrationsUsage(IIntegrationsDb db) : IAccountUsage, ICategoryUsage, IWorkspaceContents
{
    public async Task<IReadOnlySet<Guid>> UsedAmongAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken) =>
        (await db.ImportSessions.AsNoTracking()
            .SelectMany(session => session.Accounts)
            .Where(account => account.AccountId != null && accountIds.Contains(account.AccountId.Value))
            .Select(account => account.AccountId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

    public async Task<IReadOnlySet<Guid>> CategoriesUsedAmongAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken)
    {
        var used = (await db.CategorizationRules.AsNoTracking()
            .Where(rule => categoryIds.Contains(rule.CategoryId))
            .Select(rule => rule.CategoryId)
            .ToListAsync(cancellationToken)).ToHashSet();
        used.UnionWith(await db.ImportRecords.AsNoTracking()
            .Where(record => record.CategoryId != null && categoryIds.Contains(record.CategoryId.Value))
            .Select(record => record.CategoryId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken));
        used.UnionWith(await db.ImportSessions.AsNoTracking()
            .SelectMany(session => session.Categories)
            .Where(category => category.CategoryId != null && categoryIds.Contains(category.CategoryId.Value))
            .Select(category => category.CategoryId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken));
        return used;
    }

    public async Task<bool> HasDataAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        await db.ImportSessions.IgnoreQueryFilters().AnyAsync(session => session.WorkspaceId == workspaceId, cancellationToken)
        || await db.CategorizationRules.IgnoreQueryFilters().AnyAsync(rule => rule.WorkspaceId == workspaceId, cancellationToken);

    public Task RemoveLeftoversAsync(Guid workspaceId, CancellationToken cancellationToken) => Task.CompletedTask;
}
