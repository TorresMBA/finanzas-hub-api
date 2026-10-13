namespace PersonalFinance.BuildingBlocks.Domain;

/// <summary>An amount in an explicit currency. Amounts in different currencies are never combined.</summary>
public readonly record struct Money
{
    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public static Money Zero(Currency currency) => new(0m, currency);

    /// <summary>Rejects amounts with more decimal places than the currency allows; it never rounds silently.</summary>
    public static Result<Money> Create(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        if (decimal.Round(amount, currency.MinorUnits) != amount)
        {
            return Error.Validation("money.too_many_decimals", $"An amount in {currency.Code} can have at most {currency.MinorUnits} decimal places.");
        }

        return new Money(amount, currency);
    }

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Negate() => new(-Amount, Currency);

    public override string ToString() => $"{Currency.Code} {Amount.ToString("F" + Currency.MinorUnits, System.Globalization.CultureInfo.InvariantCulture)}";

    private void EnsureSameCurrency(Money other)
    {
        // Mixing currencies is a programming error, not a business outcome: conversion must be explicit.
        if (Currency != other.Currency)
        {
            throw new InvalidOperationException($"Cannot combine {Currency.Code} with {other.Currency.Code} without an explicit conversion.");
        }
    }
}
