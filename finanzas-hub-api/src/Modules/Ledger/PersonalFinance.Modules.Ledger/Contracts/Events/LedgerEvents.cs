using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Ledger.Contracts.Events;

public enum LedgerOperationChange
{
    Posted,
    Edited,
    Voided
}

/// <summary>
/// An income or an expense was registered, edited or voided. Transfers, adjustments and opening balances do not
/// raise it: nothing outside the ledger depends on them yet.
/// </summary>
/// <param name="Amount">Positive, in <paramref name="Currency"/>, as it stands after the change.</param>
public sealed record LedgerOperationChanged(
    Guid OperationId, Guid WorkspaceId, Guid OwnerUserId, LedgerOperationChange Change, bool IsExpense, Guid? CategoryId, DateOnly OccurredOn, decimal Amount,
    string Currency, string Description) : IDomainEvent;
