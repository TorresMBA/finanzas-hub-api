namespace PersonalFinance.BuildingBlocks.Domain;

/// <summary>
/// ISO 4217 currency. Only currencies in <see cref="Supported"/> can be used. This list is the only place that says
/// which ones exist: adding one here makes the API and the web accept it, with its symbol and its decimals.
/// </summary>
public sealed record Currency
{
    public static readonly Currency PEN = new("PEN", 2, "S/", "Sol peruano", "soles");
    public static readonly Currency USD = new("USD", 2, "US$", "Dólar estadounidense", "dólares");
    public static readonly Currency EUR = new("EUR", 2, "€", "Euro", "euros");

    private static readonly Dictionary<string, Currency> ByCode = new(StringComparer.Ordinal)
    {
        [PEN.Code] = PEN,
        [USD.Code] = USD,
        [EUR.Code] = EUR
    };

    private Currency(string code, int minorUnits, string symbol, string name, string pluralName)
    {
        Code = code;
        MinorUnits = minorUnits;
        Symbol = symbol;
        Name = name;
        PluralName = pluralName;
    }

    public string Code { get; }

    /// <summary>Number of decimal places an amount in this currency can carry.</summary>
    public int MinorUnits { get; }

    /// <summary>How an amount is written in Peru: "S/", "US$", "€".</summary>
    public string Symbol { get; }

    /// <summary>"Sol peruano".</summary>
    public string Name { get; }

    /// <summary>"soles", as in "1 200 soles".</summary>
    public string PluralName { get; }

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
