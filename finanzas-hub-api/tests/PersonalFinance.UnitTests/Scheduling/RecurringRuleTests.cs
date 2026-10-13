using PersonalFinance.Modules.Scheduling.Domain;

namespace PersonalFinance.UnitTests.Scheduling;

public class RecurringRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);

    private static RecurringRule Rule(RuleFrequency frequency, DateOnly startsOn, DateOnly? endsOn = null) =>
        RecurringRule.Create(
            Guid.NewGuid(), Guid.NewGuid(), RuleType.Expense, Guid.NewGuid(), "PEN", Guid.NewGuid(), 300m, "Alquiler", frequency, startsOn, endsOn,
            PostingMode.Automatic, 1, Now).Value;

    [Fact]
    public void A_monthly_rule_keeps_its_day_and_takes_the_last_one_of_a_shorter_month()
    {
        var rule = Rule(RuleFrequency.Monthly, new DateOnly(2026, 12, 31), new DateOnly(2027, 4, 15));

        Assert.Equal(
            [new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 31), new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 31)],
            rule.DatesBetween(new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31)));
        Assert.Equal([new DateOnly(2027, 2, 28)], rule.DatesBetween(new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 28)));
        Assert.False(rule.IsScheduledOn(new DateOnly(2027, 4, 30)));
    }

    [Fact]
    public void A_yearly_rule_falls_once_a_year_and_a_single_one_only_on_its_day()
    {
        var yearly = Rule(RuleFrequency.Yearly, new DateOnly(2028, 2, 29));
        Assert.Equal([new DateOnly(2028, 2, 29), new DateOnly(2029, 2, 28)], yearly.DatesBetween(new DateOnly(2028, 1, 1), new DateOnly(2029, 12, 31)));

        var once = Rule(RuleFrequency.Once, new DateOnly(2026, 10, 20));
        Assert.Equal([new DateOnly(2026, 10, 20)], once.DatesBetween(new DateOnly(2026, 1, 1), new DateOnly(2030, 1, 1)));
        Assert.Equal(new DateOnly(2026, 10, 20), once.EndsOn);
    }

    [Fact]
    public void A_date_is_settled_once_and_ending_a_rule_takes_away_what_was_still_to_come()
    {
        var rule = Rule(RuleFrequency.Monthly, new DateOnly(2026, 10, 5));
        var october = new DateOnly(2026, 10, 5);

        Assert.True(rule.Record(october, OccurrenceStatus.Posted, Guid.NewGuid(), 300m, Now).IsSuccess);
        Assert.Equal("scheduling.occurrence.already_recorded", rule.Record(october, OccurrenceStatus.Skipped, null, 0m, Now).Error.Code);
        Assert.Equal("scheduling.occurrence.not_found", rule.Record(new DateOnly(2026, 10, 6), OccurrenceStatus.Skipped, null, 0m, Now).Error.Code);
        Assert.Equal(new DateOnly(2026, 11, 5), rule.NextPendingFrom(october));

        Assert.True(rule.End(new DateOnly(2026, 10, 13)).IsSuccess);
        Assert.Equal((RuleStatus.Ended, new DateOnly(2026, 10, 13)), (rule.Status, rule.EndsOn));
        Assert.Empty(rule.DatesBetween(new DateOnly(2026, 10, 14), new DateOnly(2027, 12, 31)));
        Assert.Single(rule.Occurrences);

        // A single payment is over the moment it is settled.
        var once = Rule(RuleFrequency.Once, october);
        once.Record(october, OccurrenceStatus.Skipped, null, 0m, Now);
        Assert.Equal(RuleStatus.Ended, once.Status);
    }
}
