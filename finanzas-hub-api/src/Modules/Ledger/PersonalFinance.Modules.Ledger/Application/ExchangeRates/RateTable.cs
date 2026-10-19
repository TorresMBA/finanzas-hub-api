using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.ExchangeRates;

/// <summary>The exchange rates of a person towards the base currency of the workspace, ready to be asked.</summary>
public sealed class RateTable(string? baseCurrency, IReadOnlyList<ExchangeRate> rates)
{
    /// <summary>Null while the workspace has not chosen one: nothing can be converted then.</summary>
    public string? BaseCurrency => baseCurrency;

    /// <summary>The rate in force today: the one with the latest date.</summary>
    public ExchangeRate? Latest(string currency) =>
        rates.Where(rate => rate.Currency == currency).MaxBy(rate => rate.AsOf);

    /// <summary>The rate that applied on a day: the latest one that had started by then.</summary>
    public ExchangeRate? At(string currency, DateOnly day) =>
        rates.Where(rate => rate.Currency == currency && rate.AsOf <= day).MaxBy(rate => rate.AsOf);

    /// <summary>
    /// How many units of <paramref name="quote"/> one unit of <paramref name="currency"/> was worth on a day, through the
    /// base currency: what the market paid, to compare with what a bank applied. Null when a rate is missing.
    /// </summary>
    public decimal? CrossAt(string currency, string quote, DateOnly day)
    {
        decimal? InBase(string code) => code == baseCurrency ? 1m : At(code, day)?.Rate;
        return baseCurrency is not null && InBase(currency) is { } valued && InBase(quote) is { } quoted
            ? decimal.Round(valued / quoted, 6, MidpointRounding.AwayFromZero)
            : null;
    }

    /// <summary>An amount in the base currency at the latest rate; null when it cannot be converted.</summary>
    public decimal? ToBase(string currency, decimal amount) =>
        currency == baseCurrency ? amount : Latest(currency)?.Convert(amount);
}

/// <summary>
/// Adds amounts of several currencies into the base currency. What cannot be converted is left out and its
/// currency reported, so a total never pretends to be complete when it is not.
/// </summary>
public sealed class BaseSum(RateTable rates)
{
    private readonly SortedSet<string> _missing = new(StringComparer.Ordinal);

    public decimal Total { get; private set; }

    public IReadOnlyList<string> Missing => [.. _missing];

    /// <param name="amount">In its own currency, still to be converted at the latest rate.</param>
    /// <param name="alreadyInBase">What was valued when it was registered.</param>
    public void Add(string currency, decimal amount, decimal alreadyInBase = 0)
    {
        Total += alreadyInBase;
        if (amount == 0)
        {
            return;
        }

        if (rates.ToBase(currency, amount) is { } converted)
        {
            Total += converted;
        }
        else
        {
            _missing.Add(currency);
        }
    }
}
