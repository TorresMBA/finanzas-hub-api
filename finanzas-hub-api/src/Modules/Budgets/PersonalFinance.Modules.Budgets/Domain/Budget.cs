using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Budgets.Domain;

/// <summary>
/// How much one person plans to spend in a category each month, from one month on. It is a plan, not a fact:
/// what was spent is never stored here, it is read from the ledger. Changing the limit does not rewrite the
/// months already gone: the budget is closed and a new one starts in the month of the change.
/// </summary>
public sealed class Budget : AggregateRoot, IOwned
{
    public const decimal MaxAmount = 999_999_999.99m;

    /// <summary>The shares of the limit at which the person may ask to be warned.</summary>
    public static readonly IReadOnlyList<int> AlertThresholds = [75, 90, 100];

    private Budget()
    {
    }

    private Budget(Guid id) : base(id)
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    /// <summary>A root expense category. Its subcategories count towards the limit.</summary>
    public Guid CategoryId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = null!;

    public int AlertThresholdPercent { get; private set; }

    /// <summary>A payment that is the same every month, like rent: it is judged as paid or not, not by its pace.</summary>
    public bool IsFixed { get; private set; }

    /// <summary>The first day of the first month it applies to.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>The last day it applies to; null while it is in force.</summary>
    public DateOnly? EndsOn { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsInForce => EndsOn is null;

    public static Result<Budget> Create(
        Guid workspaceId, Guid ownerUserId, Guid categoryId, decimal amount, string currency, int alertThresholdPercent, bool isFixed, DateOnly month, DateTimeOffset now)
    {
        if (Check(amount, alertThresholdPercent) is { } error)
        {
            return error;
        }

        return new Budget(NewId())
        {
            WorkspaceId = workspaceId,
            OwnerUserId = ownerUserId,
            CategoryId = categoryId,
            Amount = amount,
            Currency = currency,
            AlertThresholdPercent = alertThresholdPercent,
            IsFixed = isFixed,
            EffectiveFrom = FirstDay(month),
            CreatedAt = now
        };
    }

    /// <summary>
    /// Changes the budget from a month on. Answers the budget that applies from that month: this same one when
    /// it started in it, or a new one that takes over, leaving this one closed the day before.
    /// </summary>
    public Result<Budget> Adjust(decimal amount, int alertThresholdPercent, bool isFixed, DateOnly month, DateTimeOffset now)
    {
        var from = FirstDay(month);
        if (!IsInForce)
        {
            return Error.BusinessRule("budgets.not_in_force", "This budget no longer applies; change the one in force.");
        }

        if (from < EffectiveFrom)
        {
            return Error.BusinessRule("budgets.adjust_before_start", "A change cannot apply before the month the budget started.");
        }

        if (Check(amount, alertThresholdPercent) is { } error)
        {
            return error;
        }

        if (from == EffectiveFrom)
        {
            Amount = amount;
            AlertThresholdPercent = alertThresholdPercent;
            IsFixed = isFixed;
            return this;
        }

        EndsOn = from.AddDays(-1);
        return new Budget(NewId())
        {
            WorkspaceId = WorkspaceId,
            OwnerUserId = OwnerUserId,
            CategoryId = CategoryId,
            Amount = amount,
            Currency = Currency,
            AlertThresholdPercent = alertThresholdPercent,
            IsFixed = isFixed,
            EffectiveFrom = from,
            CreatedAt = now
        };
    }

    /// <summary>Closes it so that it stops applying from a month on. It must have started before that month.</summary>
    public Result EndBefore(DateOnly month)
    {
        var from = FirstDay(month);
        if (!IsInForce || from <= EffectiveFrom)
        {
            return Error.BusinessRule("budgets.not_in_force", "This budget no longer applies; change the one in force.");
        }

        EndsOn = from.AddDays(-1);
        return Result.Success();
    }

    public bool Covers(DateOnly month)
    {
        var first = FirstDay(month);
        return EffectiveFrom <= first && (EndsOn is null || EndsOn >= first);
    }

    public static DateOnly FirstDay(DateOnly day) => new(day.Year, day.Month, 1);

    private static Error? Check(decimal amount, int alertThresholdPercent)
    {
        if (amount <= 0 || amount > MaxAmount || decimal.Round(amount, 2) != amount)
        {
            return Error.Validation("budgets.amount_invalid", "The limit must be greater than zero, with two decimals at most.");
        }

        return AlertThresholds.Contains(alertThresholdPercent)
            ? null
            : Error.Validation("budgets.threshold_invalid", "The warning must be at 75, 90 or 100 percent.");
    }
}
