namespace PersonalFinance.BuildingBlocks.Domain;

/// <summary>ISO 4217 currency. Only currencies in <see cref="Supported"/> can be used.</summary>
public sealed record Currency
{
    public static readonly Currency PEN = new("PEN", 2);
    public static readonly Currency USD = new("USD", 2);
    public static readonly Currency EUR = new("EUR", 2);

    private static readonly Dictionary<string, Currency> ByCode = new(StringComparer.Ordinal)
    {
        [PEN.Code] = PEN,
        [USD.Code] = USD,
        [EUR.Code] = EUR
    };

    private Currency(string code, int minorUnits)
    {
        Code = code;
        MinorUnits = minorUnits;
    }

    public string Code { get; }

    /// <summary>Number of decimal places an amount in this currency can carry.</summary>
    public int MinorUnits { get; }

    public static IReadOnlyCollection<Currency> Supported => ByCode.Values;

    public static Result<Currency> From(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant();
        if (normalized is null || !ByCode.TryGetValue(normalized, out var currency))
        {
            return Error.Validation("currency.unsupported", $"Currency '{code}' is not supported.");
        }

        return currency;
    }

    public override string ToString() => Code;
}
