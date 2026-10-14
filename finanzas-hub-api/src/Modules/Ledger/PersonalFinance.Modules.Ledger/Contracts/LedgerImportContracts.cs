using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Ledger.Contracts;

public enum LedgerImportKind
{
    Income,
    Expense,
    Transfer,

    /// <summary>The balance an account started with in the application the file comes from.</summary>
    OpeningBalance
}

/// <summary>One movement read from a file, already decided: which account, which category, how much.</summary>
/// <param name="Amount">Positive. For an opening balance it carries its sign: negative is money owed.</param>
/// <param name="CategoryId">Required for an income or an expense.</param>
/// <param name="CounterAccountId">Where a transfer arrives.</param>
/// <param name="CounterAmount">What arrives, when the two accounts of a transfer have different currencies.</param>
/// <param name="Tags">Names of tags. A name that is not a tag yet becomes one.</param>
public sealed record LedgerImportItem(
    LedgerImportKind Kind, Guid AccountId, decimal Amount, DateOnly OccurredOn, string Description, string? Note, Guid? CategoryId, Guid? CounterAccountId,
    decimal? CounterAmount, IReadOnlyList<string> Tags);

/// <summary>Either every movement was registered, or none was and it says which one stopped it.</summary>
/// <param name="OperationIds">One per item, in the same order. Empty when it failed.</param>
/// <param name="FailedIndex">The position of the item that could not be registered.</param>
public sealed record LedgerImportOutcome(IReadOnlyList<Guid> OperationIds, int? FailedIndex, Error? Error)
{
    public bool IsFailure => Error is not null;
}

/// <summary>One movement on one account, reduced to what tells it apart from another: enough to spot a repeated one.</summary>
/// <param name="SignedAmount">Negative when the money left the account.</param>
public sealed record LedgerEntryKey(Guid AccountId, DateOnly OccurredOn, decimal SignedAmount);

/// <summary>
/// How a file of movements enters the ledger of the current person. The movements are theirs like any they
/// register by hand: they can edit and void them afterwards. Being history, they announce nothing: no notice of
/// a large movement, no budget alert.
/// </summary>
public interface ILedgerImport
{
    /// <summary>Opens an empty account for the current person. A credit card is opened from the accounts, where its terms are asked.</summary>
    /// <returns>The id of the account.</returns>
    Task<Result<Guid>> OpenAccountAsync(string name, LedgerAccountType type, string currency, CancellationToken cancellationToken);

    /// <summary>What the current person already has registered in a period, voided movements left out.</summary>
    Task<IReadOnlyList<LedgerEntryKey>> GetEntryKeysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Registers all the movements in the transaction of the caller, or none.</summary>
    Task<LedgerImportOutcome> PostAsync(IReadOnlyList<LedgerImportItem> items, CancellationToken cancellationToken);
}
