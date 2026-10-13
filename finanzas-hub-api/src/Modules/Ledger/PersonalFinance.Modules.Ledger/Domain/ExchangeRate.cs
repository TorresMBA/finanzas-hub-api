using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Ledger.Domain;

/// <summary>
/// A reference rate a person keeps by hand: how much of the base currency one unit of another currency is worth
/// from a given day. It values balances and reports; it never moves money.
/// </summary>
public sealed class ExchangeRate : Entity, IOwned
{
    public const decimal MaxRate = 1_000_000m;
    public const int MaxDecimals = 6;

    private ExchangeRate()
    {
    }

    private ExchangeRate(Guid id) : base(id)
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    /// <summary>The currency amounts are converted to: the base currency of the workspace.</summary>
    public string BaseCurrency { get; private set; } = null!;

    /// <summary>The currency being valued.</summary>
    public string Currency { get; private set; } = null!;

    /// <summary>Units of <see cref="BaseCurrency"/> for one unit of <see cref="Currency"/>.</summary>
    public decimal Rate { get; private set; }

    /// <summary>The first day the rate applies. It holds until a later one exists.</summary>
    public DateOnly AsOf { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<ExchangeRate> Set(
        Guid workspaceId, Guid ownerUserId, string baseCurrency, string? currency, decimal rate, DateOnly asOf, DateTimeOffset now)
    {
        var valued = BuildingBlocks.Domain.Currency.From(currency);
        if (valued.IsFailure)
        {
            return valued.Error;
        }

        if (valued.Value.Code == baseCurrency)
        {
            return Error.Validation("ledger.exchange_rate.same_currency", "The base currency needs no exchange rate.");
        }

        var exchangeRate = new ExchangeRate(NewId())
        {
            WorkspaceId = workspaceId,
            OwnerUserId = ownerUserId,
            BaseCurrency = baseCurrency,
            Currency = valued.Value.Code,
            AsOf = asOf
        };
        var changed = exchangeRate.Change(rate, now);
        return changed.IsFailure ? changed.Error : exchangeRate;
    }

    public Result Change(decimal rate, DateTimeOffset now)
    {
        if (rate <= 0 || rate > MaxRate || decimal.Round(rate, MaxDecimals) != rate)
        {
            return Error.Validation("ledger.exchange_rate.rate_invalid", $"An exchange rate is greater than zero and has at most {MaxDecimals} decimal places.");
        }

        Rate = rate;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>The amount in the base currency, rounded to the decimals that currency carries.</summary>
    public decimal Convert(decimal amount) =>
        decimal.Round(amount * Rate, BuildingBlocks.Domain.Currency.From(BaseCurrency).Value.MinorUnits, MidpointRounding.AwayFromZero);
}
