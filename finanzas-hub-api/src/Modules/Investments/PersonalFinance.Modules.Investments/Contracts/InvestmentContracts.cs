namespace PersonalFinance.Modules.Investments.Contracts;

/// <summary>What the investments of the current person were worth on a day, in the base currency.</summary>
/// <param name="Value">What they were worth.</param>
/// <param name="Invested">The capital in them, which the ledger already counts in the net worth.</param>
/// <param name="MissingRates">Currencies without an exchange rate, left out of both amounts.</param>
public sealed record PortfolioValue(decimal Value, decimal Invested, IReadOnlyList<string> MissingRates)
{
    /// <summary>What the investments add to the net worth on top of their capital: the gain not realized yet.</summary>
    public decimal UnrealizedGain => Value - Invested;
}

/// <summary>
/// The market value of the investments of the current person, for the net worth and the reports. Only the ones
/// whose account counts in the net worth are added.
/// </summary>
public interface IPortfolioValuation
{
    Task<PortfolioValue> GetAsync(DateOnly asOf, CancellationToken cancellationToken);
}
