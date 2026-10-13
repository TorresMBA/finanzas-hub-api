using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.Transactions;

/// <summary>What a person registers by hand on one account. The other kinds of operation are created by the application.</summary>
public enum TransactionType
{
    Income,
    Expense
}

public sealed record TransactionCategoryResponse(Guid Id, string Name, string? ParentName, string Icon, string Color);

/// <summary>The two sides of a transfer. Between two currencies the amounts differ, and each is in the currency of its account.</summary>
/// <param name="IsCardPayment">The destination is a credit card.</param>
public sealed record TransferResponse(
    Guid FromAccountId, string FromAccountName, decimal AmountOut, string CurrencyOut,
    Guid ToAccountId, string ToAccountName, decimal AmountIn, string CurrencyIn, bool IsCardPayment);

/// <param name="Kind">
/// Income and Expense are transactions; Transfer moves money between two accounts; OpeningBalance and Adjustment
/// are movements the application made on an account.
/// </param>
/// <param name="Amount">Always positive.</param>
/// <param name="SignedAmount">Negative when the money left the account.</param>
/// <param name="AccountId">
/// The account the amounts refer to. For a transfer, the origin; or the account the list was filtered by, so the
/// same transfer reads as money in from its destination.
/// </param>
/// <param name="IsEditable">True only for an income or an expense that is not voided.</param>
/// <param name="Transfer">Present exactly when the kind is Transfer.</param>
public sealed record TransactionResponse(
    Guid Id, OperationKind Kind, OperationStatus Status, DateOnly OccurredOn, string Description, string? Merchant, string? Note,
    decimal Amount, decimal SignedAmount, string Currency, Guid AccountId, string AccountName,
    TransactionCategoryResponse? Category, IReadOnlyList<string> Tags, bool IsEditable, DateTimeOffset CreatedAt, string? VoidReason,
    TransferResponse? Transfer);

/// <summary>Describes operations for the API: joins the names a person reads, each from the module that owns it.</summary>
internal sealed class TransactionReader(ILedgerDb db, ICategoryLookup categories, ITagLookup tags)
{
    public async Task<TransactionResponse> DescribeAsync(Operation operation, CancellationToken cancellationToken) =>
        (await DescribeAsync([operation], null, cancellationToken))[0];

    /// <param name="perspectiveAccountId">The account the caller is looking at, when there is one: a transfer is told from its side.</param>
    public async Task<IReadOnlyList<TransactionResponse>> DescribeAsync(
        IReadOnlyList<Operation> operations, Guid? perspectiveAccountId, CancellationToken cancellationToken)
    {
        var accountIds = operations.SelectMany(operation => operation.Entries).Select(entry => entry.AccountId).Distinct().ToList();
        var accounts = await db.Accounts.AsNoTracking()
            .Where(account => accountIds.Contains(account.Id))
            .Select(account => new { account.Id, account.Name, account.Type })
            .ToDictionaryAsync(account => account.Id, account => (account.Name, account.Type), cancellationToken);

        // An account opened by the command in progress is not in the database yet.
        foreach (var added in db.Accounts.Local.Where(account => accountIds.Contains(account.Id)))
        {
            accounts[added.Id] = (added.Name, added.Type);
        }

        var categoryInfo = await categories.GetAsync(
            operations.Where(operation => operation.CategoryId != null).Select(operation => operation.CategoryId!.Value).Distinct().ToList(), cancellationToken);
        var tagNames = await tags.GetNamesAsync(operations.SelectMany(operation => operation.Tags).Select(tag => tag.TagId).Distinct().ToList(), cancellationToken);
        string NameOf(Guid accountId) => accounts.TryGetValue(accountId, out var account) ? account.Name : string.Empty;

        return operations.Select(operation =>
        {
            // The entry that left, unless the caller is looking at the other account of a transfer.
            var leaving = operation.Entries.FirstOrDefault(candidate => candidate.Direction == EntryDirection.Out);
            var entry = operation.Entries.FirstOrDefault(candidate => candidate.AccountId == perspectiveAccountId) ?? leaving ?? operation.Entries.First();
            var category = operation.CategoryId is { } categoryId && categoryInfo.TryGetValue(categoryId, out var info)
                ? new TransactionCategoryResponse(info.Id, info.Name, info.ParentName, info.Icon, info.Color)
                : null;
            TransferResponse? transfer = null;
            if (operation.Kind == OperationKind.Transfer && leaving is not null)
            {
                var arriving = operation.Entries.First(candidate => candidate.Direction == EntryDirection.In);
                transfer = new TransferResponse(
                    leaving.AccountId, NameOf(leaving.AccountId), leaving.Amount, leaving.Currency,
                    arriving.AccountId, NameOf(arriving.AccountId), arriving.Amount, arriving.Currency,
                    accounts.TryGetValue(arriving.AccountId, out var destination) && destination.Type == AccountType.CreditCard);
            }

            return new TransactionResponse(
                operation.Id, operation.Kind, operation.Status, operation.OccurredOn, operation.Description, operation.Merchant, operation.Note,
                entry.Amount, entry.Direction == EntryDirection.In ? entry.Amount : -entry.Amount, entry.Currency,
                entry.AccountId, NameOf(entry.AccountId),
                category,
                operation.Tags.Where(tag => tagNames.ContainsKey(tag.TagId)).Select(tag => tagNames[tag.TagId]).Order(StringComparer.Ordinal).ToList(),
                operation is { IsTransaction: true, IsEffective: true, SourceModule: null },
                operation.CreatedAt, operation.VoidReason, transfer);
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
