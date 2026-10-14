namespace PersonalFinance.Modules.Goals.Domain;

public enum GoalHealth
{
    OnTrack,

    /// <summary>What is needed each month is somewhat more than what has been going in.</summary>
    AdjustPace,

    /// <summary>What is needed each month is far more than what has been going in, or the date passed.</summary>
    Behind,
    Completed
}

/// <summary>How far a goal is and whether it will get there. A pure calculation over what was contributed.</summary>
/// <param name="Remaining">Zero once the target is reached, also when more than the target was saved.</param>
/// <param name="Percent">Of the target; it passes 100 when more was saved.</param>
/// <param name="RequiredMonthly">What has to go in each month from now to reach the target on its date. Null without a date or once reached.</param>
/// <param name="AverageMonthly">What has been going in each month, over the last six at most.</param>
public sealed record GoalProjection(decimal Saved, decimal Remaining, decimal Percent, decimal? RequiredMonthly, decimal AverageMonthly, GoalHealth Health)
{
    private const int WindowMonths = 6;
    private const decimal BehindFactor = 1.5m;
    private const decimal AdjustFactor = 1.15m;

    public static GoalProjection Of(Goal goal, DateOnly today)
    {
        var saved = goal.Saved;
        var remaining = Math.Max(0m, goal.TargetAmount - saved);
        var percent = decimal.Round(saved * 100m / goal.TargetAmount, 1);

        // The average is over the months the goal has existed, so a goal of two months is not judged over six.
        var created = DateOnly.FromDateTime(goal.CreatedAt.UtcDateTime);
        var months = Math.Clamp(MonthsBetween(created, today) + 1, 1, WindowMonths);
        var windowStart = new DateOnly(today.Year, today.Month, 1).AddMonths(1 - months);
        var average = decimal.Round(goal.Contributions.Where(contribution => contribution.OccurredOn >= windowStart).Sum(contribution => contribution.Signed) / months, 2);

        if (remaining == 0)
        {
            return new GoalProjection(saved, 0m, percent, null, average, GoalHealth.Completed);
        }

        if (goal.TargetDate is not { } date)
        {
            return new GoalProjection(saved, remaining, percent, null, average, GoalHealth.OnTrack);
        }

        var required = decimal.Round(remaining / Math.Max(1, MonthsBetween(today, date)), 2);
        var health = date < today ? GoalHealth.Behind
            : goal.Contributions.Count == 0 ? GoalHealth.OnTrack // Nothing to judge yet.
            : required > average * BehindFactor ? GoalHealth.Behind
            : required > average * AdjustFactor ? GoalHealth.AdjustPace
            : GoalHealth.OnTrack;
        return new GoalProjection(saved, remaining, percent, required, average, health);
    }

    private static int MonthsBetween(DateOnly from, DateOnly to) => (to.Year - from.Year) * 12 + to.Month - from.Month;
}
