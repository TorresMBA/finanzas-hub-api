using PersonalFinance.Modules.Budgets.Domain;

namespace PersonalFinance.UnitTests.Budgets;

public class BudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly October = new(2026, 10, 1);

    // The 13th of a month of 31 days: 41.9 % of the month is gone, so the risk line is at 51.9 %.
    private static readonly MonthPace Pace = MonthPace.Of(October, new DateOnly(2026, 10, 13));

    [Theory]
    [InlineData(450, 498, false, BudgetStatus.Exceeded)]
    [InlineData(450, 450, false, BudgetStatus.AtRisk)]
    [InlineData(1000, 520, false, BudgetStatus.AtRisk)]
    [InlineData(1000, 519, false, BudgetStatus.OnTrack)]
    [InlineData(1000, 0, false, BudgetStatus.OnTrack)]
    [InlineData(1800, 0, true, BudgetStatus.Fixed)]
    [InlineData(1800, 1800, true, BudgetStatus.Paid)]
    [InlineData(1800, 1900, true, BudgetStatus.Exceeded)]
    public void The_status_comes_from_the_limit_what_was_spent_and_the_pace_of_the_month(decimal limit, decimal spent, bool isFixed, BudgetStatus expected)
    {
        Assert.Equal(expected, BudgetProgress.Of(limit, spent, isFixed, Pace).Status);
    }

    [Fact]
    public void What_is_left_is_spread_over_the_days_still_to_live_and_a_closed_month_is_never_at_risk()
    {
        // 19 days left, today included.
        var progress = BudgetProgress.Of(1000m, 430m, false, Pace);
        Assert.Equal((570m, 43m, 30m), (progress.Remaining, progress.UsedPercent, progress.DailyAllowance));
        Assert.Null(BudgetProgress.Of(450m, 498m, false, Pace).DailyAllowance);

        var september = BudgetProgress.Of(1000m, 990m, false, MonthPace.Of(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 13)));
        Assert.Equal((BudgetStatus.OnTrack, null), (september.Status, september.DailyAllowance));
    }

    [Fact]
    public void Adjusting_in_a_later_month_closes_the_budget_and_opens_another_without_touching_the_past()
    {
        var budget = Budget.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 450m, "PEN", 90, false, new DateOnly(2026, 8, 20), Now).Value;
        Assert.Equal(new DateOnly(2026, 8, 1), budget.EffectiveFrom);

        var sameMonth = budget.Adjust(500m, 75, false, new DateOnly(2026, 8, 1), Now).Value;
        Assert.Same(budget, sameMonth);
        Assert.Equal((500m, 75, true), (budget.Amount, budget.AlertThresholdPercent, budget.IsInForce));

        var next = budget.Adjust(600m, 75, true, October, Now).Value;
        Assert.NotSame(budget, next);
        Assert.Equal((500m, new DateOnly(2026, 9, 30), false), (budget.Amount, budget.EndsOn, budget.IsInForce));
        Assert.Equal((600m, October, true, budget.CategoryId, budget.OwnerUserId), (next.Amount, next.EffectiveFrom, next.IsFixed, next.CategoryId, next.OwnerUserId));
        Assert.True(budget.Covers(new DateOnly(2026, 9, 15)) && !budget.Covers(October) && next.Covers(October));

        // The closed one cannot be changed again, and no limit is zero or has a warning the design does not offer.
        Assert.Equal("budgets.not_in_force", budget.Adjust(700m, 75, false, October, Now).Error.Code);
        Assert.Equal("budgets.amount_invalid", next.Adjust(0m, 75, false, October, Now).Error.Code);
        Assert.Equal("budgets.threshold_invalid", next.Adjust(700m, 80, false, October, Now).Error.Code);
    }
}
