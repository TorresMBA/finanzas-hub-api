using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Investments.Domain;

public enum InvestmentType
{
    MutualFund,
    TermDeposit,
    Stock,
    Etf,
    Crypto,
    Other
}

/// <summary>
/// What a term deposit promises: a yearly effective rate (TEA) for a number of days. Its value grows day by day
/// as the banks in Peru count it, over a year of 360 days, and stops at maturity.
/// </summary>
public sealed class TermDepositTerms
{
    public const int YearDays = 360;
    public const int MaxDays = 3650;
    public const decimal MaxRate = 100m;

    private TermDepositTerms()
    {
    }

    /// <summary>In percent: 6.5 is 6.5 % a year.</summary>
    public decimal AnnualRate { get; private set; }

    public int Days { get; private set; }

    public DateOnly MaturesOn { get; private set; }

    public static Result<TermDepositTerms> Create(decimal? annualRate, int? days, DateOnly startsOn)
    {
        if (annualRate is not { } rate || rate <= 0 || rate > MaxRate || decimal.Round(rate, 4) != rate)
        {
            return Error.Validation("investments.rate_invalid", "The yearly effective rate (TEA) must be greater than zero and at most 100 %.");
        }

        if (days is not { } term || term is < 1 or > MaxDays)
        {
            return Error.Validation("investments.term_invalid", $"The term must be between 1 and {MaxDays} days.");
        }

        return new TermDepositTerms { AnnualRate = rate, Days = term, MaturesOn = startsOn.AddDays(term) };
    }

    /// <summary>What one unit put in on <paramref name="from"/> is worth on <paramref name="on"/>, never past maturity.</summary>
    public decimal GrowthBetween(DateOnly from, DateOnly on)
    {
        var days = Math.Clamp((on < MaturesOn ? on : MaturesOn).DayNumber - from.DayNumber, 0, Days);
        return (decimal)Math.Pow(1 + (double)(AnnualRate / 100), days / (double)YearDays);
    }
}

public enum HoldingMovementKind
{
    Contribution,
    Withdrawal
}

/// <summary>
/// Money that went into the investment or came out of it. It mirrors the transfer the ledger registered; a
/// withdrawal also says which part of it was capital and which was gain (or loss).
/// </summary>
public sealed class HoldingMovement
{
    private HoldingMovement()
    {
    }

    internal HoldingMovement(Guid id, HoldingMovementKind kind, DateOnly occurredOn, decimal amount, decimal capital, decimal? quantity, Guid cashAccountId, DateTimeOffset now)
    {
        Id = id;
        Kind = kind;
        OccurredOn = occurredOn;
        Amount = amount;
        Capital = capital;
        Quantity = quantity;
        CashAccountId = cashAccountId;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public HoldingMovementKind Kind { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    /// <summary>The money that moved, positive.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The part of it that was capital: all of a contribution; of a withdrawal, the capital it took back.</summary>
    public decimal Capital { get; private set; }

    /// <summary>Gain (positive) or loss (negative) realized by a withdrawal. Zero for a contribution.</summary>
    public decimal Realized => Kind == HoldingMovementKind.Withdrawal ? Amount - Capital : 0m;

    /// <summary>Units bought or sold, for shares and crypto.</summary>
    public decimal? Quantity { get; private set; }

    /// <summary>The account the money came from or went to.</summary>
    public Guid CashAccountId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public decimal SignedAmount => Kind == HoldingMovementKind.Contribution ? Amount : -Amount;
}

/// <summary>What the investment was worth on a day, as the person read it from their bank or broker.</summary>
public sealed class HoldingValuation
{
    private HoldingValuation()
    {
    }

    internal HoldingValuation(Guid id, DateOnly onDate, decimal value, decimal? unitPrice, DateTimeOffset now)
    {
        Id = id;
        OnDate = onDate;
        Value = value;
        UnitPrice = unitPrice;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public DateOnly OnDate { get; private set; }

    public decimal Value { get; private set; }

    /// <summary>The price of one unit, when the value came from it.</summary>
    public decimal? UnitPrice { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>
/// One investment of one person: a fund, a term deposit, shares, an ETF or crypto, at an institution. The capital
/// put in lives in an account of the ledger that only this investment moves, so money is counted once; what it is
/// worth comes from the valuations the person records, or from its rate for a term deposit.
/// </summary>
public sealed class Holding : AggregateRoot, IOwned
{
    public const int MaxNameLength = 60;
    public const int MaxTickerLength = 15;
    public const int MaxInstitutionLength = 60;

    private readonly List<HoldingMovement> _movements = [];
    private readonly List<HoldingValuation> _valuations = [];

    private Holding()
    {
    }

    private Holding(Guid id) : base(id)
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public InvestmentType Type { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Ticker { get; private set; }

    public string? Institution { get; private set; }

    public string Currency { get; private set; } = null!;

    /// <summary>The account of the ledger that holds the capital.</summary>
    public Guid AccountId { get; private set; }

    public DateOnly OpenedOn { get; private set; }

    /// <summary>Set once everything was redeemed.</summary>
    public DateOnly? ClosedOn { get; private set; }

    public TermDepositTerms? TermDeposit { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<HoldingMovement> Movements => _movements.AsReadOnly();

    public IReadOnlyCollection<HoldingValuation> Valuations => _valuations.AsReadOnly();

    public bool IsClosed => ClosedOn is not null;

    /// <summary>Whether its units are counted: shares, ETFs and crypto.</summary>
    public bool HasUnits => Type is InvestmentType.Stock or InvestmentType.Etf or InvestmentType.Crypto;

    public static Result<Holding> Open(
        Guid workspaceId, Guid ownerUserId, InvestmentType type, string? name, string? ticker, string? institution, string currency, DateOnly openedOn,
        decimal? annualRate, int? termDays, DateTimeOffset now)
    {
        var cleanName = Clean(name);
        if (cleanName is null or { Length: > MaxNameLength })
        {
            return Error.Validation("investments.name_invalid", $"The name must have between 1 and {MaxNameLength} characters.");
        }

        var cleanTicker = Clean(ticker)?.ToUpperInvariant();
        if (cleanTicker is { Length: > MaxTickerLength })
        {
            return Error.Validation("investments.ticker_invalid", $"The ticker can have up to {MaxTickerLength} characters.");
        }

        var cleanInstitution = Clean(institution);
        if (cleanInstitution is { Length: > MaxInstitutionLength })
        {
            return Error.Validation("investments.institution_invalid", $"The institution can have up to {MaxInstitutionLength} characters.");
        }

        TermDepositTerms? terms = null;
        if (type == InvestmentType.TermDeposit)
        {
            var created = TermDepositTerms.Create(annualRate, termDays, openedOn);
            if (created.IsFailure)
            {
                return created.Error;
            }

            terms = created.Value;
        }

        return new Holding(Guid.CreateVersion7())
        {
            WorkspaceId = workspaceId,
            OwnerUserId = ownerUserId,
            Type = type,
            Name = cleanName,
            Ticker = cleanTicker,
            Institution = cleanInstitution,
            Currency = currency,
            OpenedOn = openedOn,
            TermDeposit = terms,
            CreatedAt = now
        };
    }

    /// <summary>The account of the ledger is opened right after the holding, in the same command.</summary>
    public void HoldIn(Guid accountId) => AccountId = accountId;

    /// <summary>The capital still in the investment on a day: what was put in minus the capital taken back.</summary>
    public decimal CapitalOn(DateOnly date) =>
        _movements.Where(movement => movement.OccurredOn <= date)
            .Sum(movement => movement.Kind == HoldingMovementKind.Contribution ? movement.Capital : -movement.Capital);

    public decimal Capital => _movements.Sum(movement => movement.Kind == HoldingMovementKind.Contribution ? movement.Capital : -movement.Capital);

    public decimal? Quantity => HasUnits ? _movements.Sum(movement => movement.Kind == HoldingMovementKind.Contribution ? movement.Quantity ?? 0 : -(movement.Quantity ?? 0)) : null;

    /// <summary>
    /// What it was worth on a day. A term deposit grows with its rate. Anything else is worth its latest valuation
    /// up to that day plus the money that went in or out after it; without a valuation, its capital.
    /// </summary>
    public decimal ValueOn(DateOnly date)
    {
        if (date < OpenedOn || ClosedOn is { } closed && date >= closed)
        {
            return 0m;
        }

        if (TermDeposit is { } terms)
        {
            var grown = _movements.Where(movement => movement.OccurredOn <= date)
                .Sum(movement => movement.SignedAmount * terms.GrowthBetween(movement.OccurredOn, date));
            return decimal.Round(Math.Max(0, grown), 2);
        }

        var latest = _valuations.Where(valuation => valuation.OnDate <= date).MaxBy(valuation => (valuation.OnDate, valuation.CreatedAt));
        if (latest is null)
        {
            return CapitalOn(date);
        }

        // A valuation includes what moved before it was written down: a later day, or the same day but recorded after it, is added.
        var after = _movements
            .Where(movement => movement.OccurredOn <= date
                && (movement.OccurredOn > latest.OnDate || movement.OccurredOn == latest.OnDate && movement.CreatedAt > latest.CreatedAt))
            .Sum(movement => movement.SignedAmount);
        return Math.Max(0, latest.Value + after);
    }

    /// <summary>What a term deposit will be worth at maturity, with the money it holds today.</summary>
    public decimal? ProjectedValue =>
        TermDeposit is { } terms ? decimal.Round(_movements.Sum(movement => movement.SignedAmount * terms.GrowthBetween(movement.OccurredOn, terms.MaturesOn)), 2) : null;

    public Result Contribute(Guid movementId, decimal amount, decimal? quantity, DateOnly occurredOn, Guid fromAccountId, DateTimeOffset now)
    {
        var valid = ValidateMovement(amount, quantity, occurredOn);
        if (valid.IsFailure)
        {
            return valid;
        }

        if (TermDeposit is not null && _movements.Count > 0)
        {
            return Error.BusinessRule("investments.deposit_fixed", "A term deposit keeps the amount it was opened with. Open a new one to put in more.");
        }

        _movements.Add(new HoldingMovement(movementId, HoldingMovementKind.Contribution, occurredOn, amount, amount, quantity, fromAccountId, now));
        return Result.Success();
    }

    /// <summary>
    /// Takes money out. The capital it takes back is in proportion to what it is worth: taking 600 of an
    /// investment worth 1 200 that cost 1 000 takes back 500 of capital and realizes 100 of gain. Taking all of it
    /// closes the investment.
    /// </summary>
    /// <returns>The movement, with the capital and the gain or loss it realized.</returns>
    public Result<HoldingMovement> Withdraw(Guid movementId, decimal amount, decimal? quantity, DateOnly occurredOn, Guid toAccountId, DateTimeOffset now)
    {
        var valid = ValidateMovement(amount, quantity, occurredOn);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        var value = ValueOn(occurredOn);
        if (amount > value)
        {
            return Error.BusinessRule("investments.withdrawal_too_large", "You cannot redeem more than the investment is worth on that day.");
        }

        if (quantity is { } sold && sold > (Quantity ?? 0))
        {
            return Error.BusinessRule("investments.quantity_too_large", "You cannot sell more units than you hold.");
        }

        var capital = CapitalOn(occurredOn);
        var all = amount == value;
        var taken = all ? capital : decimal.Round(capital * amount / value, 2);
        var movement = new HoldingMovement(movementId, HoldingMovementKind.Withdrawal, occurredOn, amount, taken, all && HasUnits ? Quantity : quantity, toAccountId, now);
        _movements.Add(movement);
        if (all)
        {
            ClosedOn = occurredOn;
        }

        return movement;
    }

    /// <summary>Records what it is worth, as an amount or as the price of one unit.</summary>
    public Result RecordValuation(Guid valuationId, DateOnly onDate, decimal? value, decimal? unitPrice, DateTimeOffset now)
    {
        if (TermDeposit is not null)
        {
            return Error.BusinessRule("investments.deposit_valued_by_rate", "A term deposit is valued by its rate; there is nothing to record.");
        }

        if (IsClosed)
        {
            return Error.BusinessRule("investments.closed", "This investment was redeemed in full.");
        }

        if (onDate < OpenedOn)
        {
            return Error.Validation("investments.date_invalid", "The date cannot be before the investment was opened.");
        }

        decimal worth;
        if (unitPrice is { } price)
        {
            if (!HasUnits || price <= 0 || decimal.Round(price, 8) != price)
            {
                return Error.Validation("investments.price_invalid", "A price is for shares, ETFs and crypto, greater than zero.");
            }

            worth = decimal.Round(price * (Quantity ?? 0), 2);
        }
        else if (value is { } amount && amount >= 0 && decimal.Round(amount, 2) == amount)
        {
            worth = amount;
        }
        else
        {
            return Error.Validation("investments.value_invalid", "The value must be zero or more, with two decimals at most.");
        }

        _valuations.Add(new HoldingValuation(valuationId, onDate, worth, unitPrice, now));
        return Result.Success();
    }

    private Result ValidateMovement(decimal amount, decimal? quantity, DateOnly occurredOn)
    {
        if (IsClosed)
        {
            return Error.BusinessRule("investments.closed", "This investment was redeemed in full.");
        }

        if (amount <= 0 || decimal.Round(amount, 2) != amount)
        {
            return Error.Validation("investments.amount_invalid", "The amount must be greater than zero, with two decimals at most.");
        }

        if (quantity is { } units && (!HasUnits || units <= 0 || decimal.Round(units, 8) != units))
        {
            return Error.Validation("investments.quantity_invalid", "Units are for shares, ETFs and crypto, greater than zero.");
        }

        if (occurredOn < OpenedOn)
        {
            return Error.Validation("investments.date_invalid", "The date cannot be before the investment was opened.");
        }

        return Result.Success();
    }

    private static string? Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
