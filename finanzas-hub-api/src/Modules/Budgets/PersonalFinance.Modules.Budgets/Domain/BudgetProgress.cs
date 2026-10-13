namespace PersonalFinance.Modules.Budgets.Domain;

public enum BudgetStatus
{
    OnTrack,
    AtRisk,
    Exceeded,
    Fixed,
    Paid
}

/// <summary>Where a month stands for someone looking at it on a given day.</summary>
/// <param name="Day">The day of the month it is today; the last one for a month gone, zero for one to come.</param>
/// <param name="DaysLeft">Days still to live in the month, today included.</param>
/// <param name="PacePercent">The share of the month gone: what would be spent by now at an even pace.</param>
public sealed record MonthPace(int Day, int DaysInMonth, int DaysLeft, decimal PacePercent)
{
    public static MonthPace Of(DateOnly month, DateOnly today)
    {
        var first = Budget.FirstDay(month);
        var days = DateTime.DaysInMonth(first.Year, first.Month);
        var current = Budget.FirstDay(today);
        if (first < current)
        {
            return new MonthPace(days, days, 0, 100m);
        }

        return first > current
            ? new MonthPace(0, days, days, 0m)
            : new MonthPace(today.Day, days, days - today.Day + 1, decimal.Round(today.Day * 100m / days, 1));
    }
}

/// <summary>
/// How a budget is doing. A pure calculation: the limit and what was spent come in, nothing is read or stored.
/// </summary>
/// <param name="Remaining">Negative when the limit was passed.</param>
/// <param name="DailyAllowance">What can still be spent per day to end the month within the limit. Null when it does not apply.</param>
public sealed record BudgetProgress(decimal Spent, decimal Remaining, decimal UsedPercent, BudgetStatus Status, decimal? DailyAllowance)
{
    /// <summary>How far ahead of the pace of the month the spending may run before the budget is at risk, in points.</summary>
    public const decimal RiskMargin = 10m;

    public static BudgetProgress Of(decimal limit, decimal spent, bool isFixed, MonthPace pace)
    {
        var remaining = limit - spent;
        var used = limit > 0 ? decimal.Round(spent * 100m / limit, 1) : 0m;
        var status = spent > limit ? BudgetStatus.Exceeded
            : isFixed ? (spent >= limit ? BudgetStatus.Paid : BudgetStatus.Fixed)
            : used > pace.PacePercent + RiskMargin ? BudgetStatus.AtRisk
            : BudgetStatus.OnTrack;
        return new BudgetProgress(spent, remaining, used, status, isFixed ? null : Allowance(remaining, pace));
    }

    public static decimal? Allowance(decimal remaining, MonthPace pace) =>
        remaining > 0 && pace.DaysLeft > 0 ? decimal.Round(remaining / pace.DaysLeft, 2) : null;
}
