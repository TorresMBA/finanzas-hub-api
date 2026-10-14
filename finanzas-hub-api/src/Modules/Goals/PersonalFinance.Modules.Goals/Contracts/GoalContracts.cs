namespace PersonalFinance.Modules.Goals.Contracts;

/// <summary>A goal the current person takes part in, as a summary.</summary>
/// <param name="Saved">By all its participants, in <paramref name="Currency"/>.</param>
/// <param name="Percent">Of the target; it passes 100 when more was saved.</param>
public sealed record GoalSummary(Guid Id, string Name, decimal Saved, decimal TargetAmount, string Currency, decimal Percent, bool IsShared);

/// <summary>What other modules read from the goals. Only goals the current person takes part in are ever answered.</summary>
public interface IGoalQueries
{
    /// <summary>The goals still open, the closest to their target first.</summary>
    Task<IReadOnlyList<GoalSummary>> GetActiveAsync(int count, CancellationToken cancellationToken);
}
