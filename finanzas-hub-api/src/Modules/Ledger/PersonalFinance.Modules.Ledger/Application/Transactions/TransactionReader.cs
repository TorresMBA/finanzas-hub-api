using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.Transactions;

/// <summary>What a person registers by hand. The other kinds of operation are created by the application.</summary>
public enum TransactionType
{
    Income,
    Expense
}

public sealed record TransactionCategoryResponse(Guid Id, string Name, string? ParentName, string Icon, string Color);

/// <param name="Kind">Income and Expense are transactions; OpeningBalance and Adjustment are movements the application made on an account.</param>
/// <param name="Amount">Always positive.</param>
/// <param name="SignedAmount">Negative when the money left the account.</param>
/// <param name="IsEditable">False for a voided transaction and for a movement that is not an income or an expense.</param>
public sealed record TransactionResponse(
    Guid Id, OperationKind Kind, OperationStatus Status, DateOnly OccurredOn, string Description, string? Merchant, string? Note,
    decimal Amount, decimal SignedAmount, string Currency, Guid AccountId, string AccountName,
    TransactionCategoryResponse? Category, IReadOnlyList<string> Tags, bool IsEditable, DateTimeOffset CreatedAt, string? VoidReason);

/// <summary>Describes operations for the API: joins the names a person reads, each from the module that owns it.</summary>
internal sealed class TransactionReader(ILedgerDb db, ICategoryLookup categories, ITagLookup tags)
{
    public async Task<TransactionResponse> DescribeAsync(Operation operation, CancellationToken cancellationToken) =>
        (await DescribeAsync([operation], cancellationToken))[0];

    public async Task<IReadOnlyList<TransactionResponse>> DescribeAsync(IReadOnlyList<Operation> operations, CancellationToken cancellationToken)
    {
        var accountIds = operations.SelectMany(operation => operation.Entries).Select(entry => entry.AccountId).Distinct().ToList();
        var accountNames = await db.Accounts.AsNoTracking()
            .Where(account => accountIds.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id, account => account.Name, cancellationToken);

        // An account opened by the command in progress is not in the database yet.
        foreach (var added in db.Accounts.Local.Where(account => accountIds.Contains(account.Id)))
        {
            accountNames[added.Id] = added.Name;
        }

        var categoryInfo = await categories.GetAsync(
            operations.Where(operation => operation.CategoryId != null).Select(operation => operation.CategoryId!.Value).Distinct().ToList(), cancellationToken);
        var tagNames = await tags.GetNamesAsync(operations.SelectMany(operation => operation.Tags).Select(tag => tag.TagId).Distinct().ToList(), cancellationToken);

        return operations.Select(operation =>
        {
            // One entry per operation until transfers arrive.
            var entry = operation.Entries.First();
            var category = operation.CategoryId is { } categoryId && categoryInfo.TryGetValue(categoryId, out var info)
                ? new TransactionCategoryResponse(info.Id, info.Name, info.ParentName, info.Icon, info.Color)
                : null;
            return new TransactionResponse(
                operation.Id, operation.Kind, operation.Status, operation.OccurredOn, operation.Description, operation.Merchant, operation.Note,
                entry.Amount, entry.Direction == EntryDirection.In ? entry.Amount : -entry.Amount, entry.Currency,
                entry.AccountId, accountNames.GetValueOrDefault(entry.AccountId, string.Empty),
                category,
                operation.Tags.Where(tag => tagNames.ContainsKey(tag.TagId)).Select(tag => tagNames[tag.TagId]).Order(StringComparer.Ordinal).ToList(),
                operation is { IsTransaction: true, IsEffective: true, SourceModule: null },
                operation.CreatedAt, operation.VoidReason);
        }).ToList();
    }

    /// <summary>The category a transaction is being given: it must be the person's, of the right kind and in use.</summary>
    /// <param name="currentCategoryId">The one the transaction already has; keeping it is allowed even if it was archived since.</param>
    public async Task<Result> ValidateCategoryAsync(TransactionType type, Guid categoryId, Guid? currentCategoryId, CancellationToken cancellationToken)
    {
        if (await categories.FindAsync(categoryId, cancellationToken) is not { } category)
        {
            return LedgerErrors.CategoryNotFound;
        }

        if ((category.Use == CategoryUse.Income) != (type == TransactionType.Income))
        {
            return Error.BusinessRule("ledger.category_kind_mismatch", "An income needs an income category and an expense an expense category.");
        }

        return category.IsArchived && categoryId != currentCategoryId
            ? Error.BusinessRule("ledger.category_archived", "The category is archived. Choose another one or restore it.")
            : Result.Success();
    }

    public async Task<Result> SetTagsAsync(Operation operation, IReadOnlyList<string>? names, CancellationToken cancellationToken)
    {
        var resolved = await tags.ResolveAsync(names ?? [], cancellationToken);
        return resolved.IsFailure ? resolved.Error : operation.SetTags(resolved.Value.Select(tag => tag.Id).ToList());
    }
}
