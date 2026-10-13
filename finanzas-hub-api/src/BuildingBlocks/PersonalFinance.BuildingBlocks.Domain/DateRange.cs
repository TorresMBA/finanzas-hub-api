namespace PersonalFinance.BuildingBlocks.Domain;

/// <summary>Inclusive range of business dates.</summary>
public readonly record struct DateRange
{
    private DateRange(DateOnly start, DateOnly end)
    {
        Start = start;
        End = end;
    }

    public DateOnly Start { get; }

    public DateOnly End { get; }

    public int Days => End.DayNumber - Start.DayNumber + 1;

    public static Result<DateRange> Create(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            return Error.Validation("date_range.inverted", "The end of a date range cannot be before its start.");
        }

        return new DateRange(start, end);
    }

    public static DateRange ForMonth(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        return new DateRange(start, start.AddMonths(1).AddDays(-1));
    }

    public bool Contains(DateOnly date) => date >= Start && date <= End;
}
