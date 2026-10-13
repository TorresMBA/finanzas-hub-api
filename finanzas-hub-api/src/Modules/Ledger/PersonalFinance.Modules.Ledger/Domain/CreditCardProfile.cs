using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Ledger.Domain;

/// <summary>What a person states about a credit card when opening or changing it.</summary>
public sealed record CreditCardTerms(decimal CreditLimit, int PaymentDay, int? ClosingDay);

/// <summary>
/// What makes an account a credit card: its line and the days of its cycle. What is owed is not here: it is the
/// balance of the account, negative while there is debt, and what is left to spend is the line plus that balance.
/// </summary>
public sealed class CreditCardProfile
{
    private CreditCardProfile()
    {
    }

    /// <summary>In the currency of the account.</summary>
    public decimal CreditLimit { get; private set; }

    /// <summary>Day of the month the payment is due. In a shorter month it falls on the last day.</summary>
    public int PaymentDay { get; private set; }

    /// <summary>Day of the month the statement closes, when the person knows it.</summary>
    public int? ClosingDay { get; private set; }

    internal static Result<CreditCardProfile> Create(CreditCardTerms terms, string currency)
    {
        var profile = new CreditCardProfile();
        var changed = profile.Change(terms, currency);
        return changed.IsFailure ? changed.Error : profile;
    }

    internal Result Change(CreditCardTerms terms, string currency)
    {
        if (terms.CreditLimit <= 0 || terms.CreditLimit > 999_999_999_999.99m)
        {
            return Error.Validation("ledger.card.limit_invalid", "A credit card needs a credit line greater than zero.");
        }

        var limit = Money.Create(terms.CreditLimit, Currency.From(currency).Value);
        if (limit.IsFailure)
        {
            return limit.Error;
        }

        if (terms.PaymentDay is < 1 or > 31 || terms.ClosingDay is < 1 or > 31)
        {
            return Error.Validation("ledger.card.day_invalid", "The payment day and the closing day are days of the month, from 1 to 31.");
        }

        CreditLimit = terms.CreditLimit;
        PaymentDay = terms.PaymentDay;
        ClosingDay = terms.ClosingDay;
        return Result.Success();
    }

    /// <summary>What can still be spent: the line plus the balance, which is negative while there is debt.</summary>
    public decimal AvailableWith(decimal balance) => CreditLimit + balance;

    /// <summary>The next day a payment is due, today included.</summary>
    public DateOnly NextPaymentDate(DateOnly today)
    {
        var thisMonth = DayIn(today.Year, today.Month);
        if (thisMonth >= today)
        {
            return thisMonth;
        }

        var next = today.AddMonths(1);
        return DayIn(next.Year, next.Month);
    }

    private DateOnly DayIn(int year, int month) => new(year, month, Math.Min(PaymentDay, DateTime.DaysInMonth(year, month)));
}
