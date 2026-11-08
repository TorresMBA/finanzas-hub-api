using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Ledger.Application.Abstractions;

namespace PersonalFinance.Modules.Ledger.Application;

/// <summary>What the ledger points at, so it is never deleted from under a movement (ADR-024). Voided movements count: they are history.</summary>
internal sealed class LedgerUsage(ILedgerDb db) : ICategoryUsage, IWorkspaceContents
{
    public async Task<IReadOnlySet<Guid>> CategoriesUsedAmongAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken) =>
        (await db.Operations.AsNoTracking()
            .Where(operation => operation.CategoryId != null && categoryIds.Contains(operation.CategoryId.Value))
            .Select(operation => operation.CategoryId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

    public async Task<IReadOnlySet<Guid>> TagsUsedAmongAsync(IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken) =>
        (await db.Operations.AsNoTracking()
            .SelectMany(operation => operation.Tags)
            .Where(tag => tagIds.Contains(tag.TagId))
            .Select(tag => tag.TagId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

    public async Task<bool> HasDataAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        await db.Accounts.IgnoreQueryFilters().AnyAsync(account => account.WorkspaceId == workspaceId, cancellationToken)
        || await db.Operations.IgnoreQueryFilters().AnyAsync(operation => operation.WorkspaceId == workspaceId, cancellationToken)
        || await db.ExchangeRates.IgnoreQueryFilters().AnyAsync(rate => rate.WorkspaceId == workspaceId, cancellationToken);

    public Task RemoveLeftoversAsync(Guid workspaceId, CancellationToken cancellationToken) => Task.CompletedTask;
}
