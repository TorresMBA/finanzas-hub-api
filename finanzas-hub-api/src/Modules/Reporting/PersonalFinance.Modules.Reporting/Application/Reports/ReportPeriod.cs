using System.Globalization;
using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Reporting.Application.Reports;

public enum ReportPeriodKind
{
    Month,
    Quarter,
    Year
}

/// <summary>
/// The whole calendar month, quarter or year a report is about. The one it is compared with is the one right
/// before, of the same length: October with September, the fourth quarter with the third, 2026 with 2025.
/// </summary>
public sealed record ReportPeriod(ReportPeriodKind Kind, DateOnly From, DateOnly To)
{
    /// <param name="anchor">A month inside the period, as <c>2026-10</c>.</param>
    public static Result<ReportPeriod> Of(ReportPeriodKind kind, string anchor)
    {
        if (!DateOnly.TryParseExact(anchor + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        {
            return Error.Validation("reporting.month_invalid", "The month must look like 2026-10.");
        }

        var from = kind switch
        {
            ReportPeriodKind.Year => new DateOnly(month.Year, 1, 1),
            ReportPeriodKind.Quarter => new DateOnly(month.Year, (month.Month - 1) / 3 * 3 + 1, 1),
            _ => month
        };
        return new ReportPeriod(kind, from, from.AddMonths(Months(kind)).AddDays(-1));
    }

    public ReportPeriod Previous()
    {
        var from = From.AddMonths(-Months(Kind));
        return new ReportPeriod(Kind, from, From.AddDays(-1));
    }

    /// <summary>"2026-10", "2026-T4" or "2026".</summary>
    public string Label => Kind switch
    {
        ReportPeriodKind.Year => $"{From.Year:D4}",
        ReportPeriodKind.Quarter => $"{From.Year:D4}-T{(From.Month - 1) / 3 + 1}",
        _ => $"{From.Year:D4}-{From.Month:D2}"
    };

    /// <summary>
    /// The days the expenses of the period are spread over for a daily average: up to today while the period is
    /// running, none before it starts, all of them once it is over.
    /// </summary>
    public int DaysUpTo(DateOnly today)
    {
        if (today < From)
        {
            return 0;
        }

        var last = today > To ? To : today;
        return last.DayNumber - From.DayNumber + 1;
    }

    /// <summary>The months shown as the savings rate, oldest first: the six ending with the period, or the twelve of a year.</summary>
    public DateOnly TrendFrom => Kind == ReportPeriodKind.Year ? From : new DateOnly(To.Year, To.Month, 1).AddMonths(-5);

    private static int Months(ReportPeriodKind kind) => kind switch
    {
        ReportPeriodKind.Year => 12,
        ReportPeriodKind.Quarter => 3,
        _ => 1
    };
}
