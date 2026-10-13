namespace PersonalFinance.Modules.Ledger.Contracts;

/// <summary>Mirrors the type of an account for modules that must not see the Ledger domain.</summary>
public enum LedgerAccountType
{
    Bank,
    Cash,
    Wallet,
    CreditCard
}

public enum LedgerMovementKind
{
    Income,
    Expense,
    Transfer
}

/// <summary>What a person has and owes, in the base currency of the workspace.</summary>
/// <param name="BaseCurrency">Null while the workspace has no base currency; every amount is then zero.</param>
/// <param name="Debts">Positive: what is owed.</param>
/// <param name="MissingRates">Currencies with money in them and no exchange rate. They are left out of the amounts.</param>
public sealed record LedgerNetWorth(string? BaseCurrency, decimal NetWorth, decimal Assets, decimal Debts, IReadOnlyList<string> MissingRates);

/// <summary>Income and expenses of one calendar month, in the base currency.</summary>
public sealed record LedgerMonthFlow(int Year, int Month, decimal Income, decimal Expense);

public sealed record LedgerFlow(IReadOnlyList<LedgerMonthFlow> Months, IReadOnlyList<string> MissingRates);

/// <summary>What one category moved in a period, in the base currency. A subcategory is reported apart from its parent.</summary>
public sealed record LedgerCategoryTotal(Guid CategoryId, bool IsIncome, decimal Amount, int Count);

public sealed record LedgerCategoryTotals(IReadOnlyList<LedgerCategoryTotal> Categories, IReadOnlyList<string> MissingRates);

/// <param name="Balance">In the currency of the account; negative when it is owed.</param>
/// <param name="BaseBalance">The balance in the base currency; null when there is no rate for its currency.</param>
/// <param name="CreditLimit">Set for a credit card.</param>
public sealed record LedgerAccountBalance(
    Guid Id, string Name, LedgerAccountType Type, string Currency, decimal Balance, decimal? BaseBalance, decimal? CreditLimit, bool IncludeInNetWorth);

/// <param name="SignedAmount">Negative when the money left the account. For a transfer, what left the origin account.</param>
/// <param name="CounterAccountName">The destination account of a transfer.</param>
public sealed record LedgerMovement(
    Guid Id, LedgerMovementKind Kind, DateOnly OccurredOn, string Description, decimal SignedAmount, string Currency, string AccountName,
    string? CounterAccountName, Guid? CategoryId);

/// <summary>
/// What other modules read from the ledger. Every answer is about the current person in the current workspace:
/// nobody reaches the books of someone else through here.
/// </summary>
public interface ILedgerQueries
{
    /// <summary>Net worth with the movements up to a day, valued at the latest exchange rate.</summary>
    Task<LedgerNetWorth> GetNetWorthAsync(DateOnly asOf, CancellationToken cancellationToken);

    /// <summary>Income and expenses month by month. Every month of the range is present, also the empty ones.</summary>
    Task<LedgerFlow> GetFlowAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<LedgerCategoryTotals> GetCategoryTotalsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>The accounts in use, with what each holds today.</summary>
    Task<IReadOnlyList<LedgerAccountBalance>> GetAccountBalancesAsync(CancellationToken cancellationToken);

    /// <summary>The latest income, expenses and transfers of a period, newest first.</summary>
    Task<IReadOnlyList<LedgerMovement>> GetRecentMovementsAsync(DateOnly from, DateOnly to, int count, CancellationToken cancellationToken);
}
