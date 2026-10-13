namespace PersonalFinance.Modules.Budgets.Contracts;

/// <summary>The budgets of one month added up, in the base currency of the workspace.</summary>
/// <param name="Count">How many budgets apply to the month.</param>
public sealed record BudgetMonthSummary(int Count, decimal Limit, decimal Spent);

/// <summary>What other modules read from the budgets. Every answer is about the current person in the current workspace.</summary>
public interface IBudgetQueries
{
    Task<BudgetMonthSummary> GetMonthAsync(DateOnly month, CancellationToken cancellationToken);
}
