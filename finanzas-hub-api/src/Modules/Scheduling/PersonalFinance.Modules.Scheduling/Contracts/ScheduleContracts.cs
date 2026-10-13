namespace PersonalFinance.Modules.Scheduling.Contracts;

/// <summary>Something the current person still has to pay: a scheduled expense or what a credit card owes.</summary>
/// <param name="Amount">Positive, in <paramref name="Currency"/>.</param>
/// <param name="IsCardPayment">A credit card falling due, not an expense: the expense was the purchase.</param>
public sealed record UpcomingPayment(DateOnly Date, string Description, decimal Amount, string Currency, bool IsCardPayment);

/// <summary>What other modules read from the schedule. Every answer is about the current person in the current workspace.</summary>
public interface IScheduleQueries
{
    /// <summary>The payments between two days that are not registered yet, the soonest first.</summary>
    Task<IReadOnlyList<UpcomingPayment>> GetUpcomingAsync(DateOnly from, DateOnly to, int count, CancellationToken cancellationToken);
}
