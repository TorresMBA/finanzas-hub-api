using PersonalFinance.Modules.Ledger.Domain;
using PersonalFinance.Modules.Reporting.Application.Reports;

namespace PersonalFinance.UnitTests.Reporting;

public class ReportPeriodTests
{
    [Theory]
    [InlineData(ReportPeriodKind.Month, "2026-03", "2026-03-01", "2026-03-31", "2026-02-01", "2026-02-28", "2026-03")]
    [InlineData(ReportPeriodKind.Month, "2026-01", "2026-01-01", "2026-01-31", "2025-12-01", "2025-12-31", "2026-01")]
    [InlineData(ReportPeriodKind.Quarter, "2026-11", "2026-10-01", "2026-12-31", "2026-07-01", "2026-09-30", "2026-T4")]
    [InlineData(ReportPeriodKind.Quarter, "2026-02", "2026-01-01", "2026-03-31", "2025-10-01", "2025-12-31", "2026-T1")]
    [InlineData(ReportPeriodKind.Year, "2026-07", "2026-01-01", "2026-12-31", "2025-01-01", "2025-12-31", "2026")]
    public void A_period_is_compared_with_the_whole_one_right_before(
        ReportPeriodKind kind, string anchor, string from, string to, string previousFrom, string previousTo, string label)
    {
        var period = ReportPeriod.Of(kind, anchor).Value;
        var previous = period.Previous();

        Assert.Equal((DateOnly.Parse(from), DateOnly.Parse(to), label), (period.From, period.To, period.Label));
        Assert.Equal((DateOnly.Parse(previousFrom), DateOnly.Parse(previousTo)), (previous.From, previous.To));
    }

    [Fact]
    public void The_daily_expense_counts_the_days_gone_by_of_a_running_period()
    {
        var october = ReportPeriod.Of(ReportPeriodKind.Month, "2026-10").Value;

        Assert.Equal((0, 15, 31), (october.DaysUpTo(new DateOnly(2026, 9, 30)), october.DaysUpTo(new DateOnly(2026, 10, 15)), october.DaysUpTo(new DateOnly(2026, 11, 2))));
    }

    [Theory]
    [InlineData("Wong", "Compra", "wong")]
    [InlineData(" WÓNG  San Isidro ", "Compra", "wong san isidro")]
    [InlineData("UBER *TRIP 4821", "Taxi", "uber trip")]
    [InlineData(null, "Netflix.com", "netflix com")]
    public void The_ways_of_writing_a_shop_share_one_key(string? merchant, string description, string key) =>
        Assert.Equal(key, MerchantName.KeyOf(merchant, description));
}
