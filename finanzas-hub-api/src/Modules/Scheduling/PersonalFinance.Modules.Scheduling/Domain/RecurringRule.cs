using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Scheduling.Domain;

public enum RuleType
{
    Income,
    Expense
}

public enum RuleFrequency
{
    Once,
    Monthly,
    Yearly
}

public enum PostingMode
{
    /// <summary>Registered by the system on its date.</summary>
    Automatic,

    /// <summary>Waits for the person to confirm it, with the real amount if it differs.</summary>
    Confirm
}

public enum RuleStatus
{
    Active,
    Paused,
    Ended
}

public enum OccurrenceStatus
{
    Posted,
    Skipped
}

/// <summary>What happened with a rule on one of its dates. There is one at most per rule and date.</summary>
public sealed class Occurrence
{
    private Occurrence()
    {
    }

    internal Occurrence(DateOnly scheduledOn, OccurrenceStatus status, Guid? operationId, decimal amount, DateTimeOffset now)
    {
        ScheduledOn = scheduledOn;
        Status = status;
        OperationId = operationId;
        Amount = amount;
        RecordedAt = now;
    }

    public DateOnly ScheduledOn { get; private set; }

    public OccurrenceStatus Status { get; private set; }

    /// <summary>The operation of the ledger it became, by id: it belongs to another module.</summary>
    public Guid? OperationId { get; private set; }

    public decimal Amount { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }
}

/// <summary>
/// An income or an expense a person expects on a date, once or every month or year. The rule is a plan: its
/// future dates are calculated, never stored, and only what was registered or skipped leaves a row.
/// </summary>
public sealed class RecurringRule : AggregateRoot, IOwned
{
    public const int MaxDescriptionLength = 120;
    public const decimal MaxAmount = 999_999_999.99m;

    /// <summary>How many days before a date a reminder can be asked for.</summary>
    public static readonly IReadOnlyList<int> ReminderOffsets = [0, 1, 3, 7];

    private readonly List<Occurrence> _occurrences = [];

    private RecurringRule()
    {
    }

    private RecurringRule(Guid id) : base(id)
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public RuleType Type { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid CategoryId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>That of the account.</summary>
    public string Currency { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public RuleFrequency Frequency { get; private set; }

    /// <summary>The first date. The following ones fall on the same day of the month, or on its last day when the month is shorter.</summary>
    public DateOnly StartsOn { get; private set; }

    /// <summary>The last day a date can fall on; null while it has no end.</summary>
    public DateOnly? EndsOn { get; private set; }

    public PostingMode PostingMode { get; private set; }

    /// <summary>Null: no reminder.</summary>
    public int? ReminderDaysBefore { get; private set; }

    public RuleStatus Status { get; private set; }

    /// <summary>Why the system paused it, in words for the person.</summary>
    public string? PauseReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<Occurrence> Occurrences => _occurrences.AsReadOnly();

    public static Result<RecurringRule> Create(
        Guid workspaceId, Guid ownerUserId, RuleType type, Guid accountId, string currency, Guid categoryId, decimal amount, string description,
        RuleFrequency frequency, DateOnly startsOn, DateOnly? endsOn, PostingMode postingMode, int? reminderDaysBefore, DateTimeOffset now)
    {
        var text = description?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaxDescriptionLength)
        {
            return Error.Validation("scheduling.description_invalid", $"The description is required and can have at most {MaxDescriptionLength} characters.");
        }

        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 2) != amount)
        {
            return Error.Validation("scheduling.amount_invalid", "The amount must be greater than zero, with two decimals at most.");
        }

        if (endsOn < startsOn)
        {
            return Error.Validation("scheduling.period_invalid", "The rule ends before it starts.");
        }

        if (reminderDaysBefore is { } days && !ReminderOffsets.Contains(days))
        {
            return Error.Validation("scheduling.reminder_invalid", "The reminder is on the day, or 1, 3 or 7 days before.");
        }

        return new RecurringRule(NewId())
        {
            WorkspaceId = workspaceId,
            OwnerUserId = ownerUserId,
            Type = type,
            AccountId = accountId,
            Currency = currency,
            CategoryId = categoryId,
            Amount = amount,
            Description = text,
            Frequency = frequency,
            StartsOn = startsOn,
            EndsOn = frequency == RuleFrequency.Once ? startsOn : endsOn,
            PostingMode = postingMode,
            ReminderDaysBefore = reminderDaysBefore,
            Status = RuleStatus.Active,
            CreatedAt = now
        };
    }

    /// <summary>
    /// The dates of the rule in a period, both ends included. Each is counted from the first one, not from the
    /// one before, so a rule that starts on the 31st falls on the 28th in February and on the 31st again in March.
    /// </summary>
    public IEnumerable<DateOnly> DatesBetween(DateOnly from, DateOnly to)
    {
        var last = EndsOn is { } end && end < to ? end : to;
        for (var step = 0; ; step++)
        {
            var date = Frequency switch
            {
                RuleFrequency.Monthly => StartsOn.AddMonths(step),
                RuleFrequency.Yearly => StartsOn.AddYears(step),
                _ => StartsOn
            };
            if (date > last)
            {
                yield break;
            }

            if (date >= from)
            {
                yield return date;
            }

            if (Frequency == RuleFrequency.Once)
            {
                yield break;
            }
        }
    }

    public bool IsScheduledOn(DateOnly date) => DatesBetween(date, date).Any();

    public Occurrence? OccurrenceOn(DateOnly date) => _occurrences.FirstOrDefault(occurrence => occurrence.ScheduledOn == date);

    /// <summary>The first date from a day on that is still to be registered or skipped.</summary>
    public DateOnly? NextPendingFrom(DateOnly day) =>
        Status == RuleStatus.Ended && EndsOn < day
            ? null
            : DatesBetween(day, day.AddYears(2)).Cast<DateOnly?>().FirstOrDefault(date => OccurrenceOn(date!.Value) is null);

    /// <summary>Leaves written that a date was registered in the ledger or skipped. A date is settled once.</summary>
    public Result<Occurrence> Record(DateOnly date, OccurrenceStatus status, Guid? operationId, decimal amount, DateTimeOffset now)
    {
        if (!IsScheduledOn(date))
        {
            return Error.NotFound("scheduling.occurrence.not_found", "The rule has nothing on that date.");
        }

        if (OccurrenceOn(date) is not null)
        {
            return Error.Conflict("scheduling.occurrence.already_recorded", "That date was already registered or skipped.");
        }

        var occurrence = new Occurrence(date, status, operationId, amount, now);
        _occurrences.Add(occurrence);
        if (Frequency == RuleFrequency.Once)
        {
            Status = RuleStatus.Ended;
        }

        return occurrence;
    }

    public Result Pause(string? reason = null)
    {
        if (Status != RuleStatus.Active)
        {
            return Error.BusinessRule("scheduling.rule.not_active", "Only an active rule can be paused.");
        }

        Status = RuleStatus.Paused;
        PauseReason = reason;
        return Result.Success();
    }

    public Result Resume()
    {
        if (Status != RuleStatus.Paused)
        {
            return Error.BusinessRule("scheduling.rule.not_paused", "Only a paused rule can be resumed.");
        }

        Status = RuleStatus.Active;
        PauseReason = null;
        return Result.Success();
    }

    /// <summary>Stops it for good: the dates after today are gone, what was registered stays.</summary>
    public Result End(DateOnly today)
    {
        if (Status == RuleStatus.Ended)
        {
            return Error.BusinessRule("scheduling.rule.ended", "The rule had already ended.");
        }

        Status = RuleStatus.Ended;
        PauseReason = null;
        if (EndsOn is null || EndsOn > today)
        {
            // A rule that never started keeps a valid period by ending on its first day.
            EndsOn = today < StartsOn ? StartsOn : today;
        }

        return Result.Success();
    }
}
