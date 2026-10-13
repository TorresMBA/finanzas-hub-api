using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.UnitTests.BuildingBlocks;

public class MoneyTests
{
    [Theory]
    [InlineData("pen", "PEN")]
    [InlineData(" USD ", "USD")]
    public void A_supported_currency_code_is_normalized(string code, string expected)
    {
        var currency = Currency.From(code);

        Assert.True(currency.IsSuccess);
        Assert.Equal(expected, currency.Value.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XXX")]
    [InlineData("SOLES")]
    public void An_unsupported_currency_code_is_rejected(string? code)
    {
        var currency = Currency.From(code);

        Assert.True(currency.IsFailure);
        Assert.Equal("currency.unsupported", currency.Error.Code);
    }

    [Fact]
    public void An_amount_with_more_decimals_than_the_currency_allows_is_rejected_instead_of_rounded()
    {
        var money = Money.Create(10.005m, Currency.PEN);

        Assert.True(money.IsFailure);
        Assert.Equal("money.too_many_decimals", money.Error.Code);
    }

    [Fact]
    public void Amounts_in_the_same_currency_add_and_subtract_exactly()
    {
        var first = Money.Create(0.10m, Currency.PEN).Value;
        var second = Money.Create(0.20m, Currency.PEN).Value;

        Assert.Equal(0.30m, first.Add(second).Amount);
        Assert.Equal(-0.10m, first.Subtract(second).Amount);
        Assert.Equal(Currency.PEN, first.Add(second).Currency);
    }

    [Fact]
    public void Amounts_in_different_currencies_cannot_be_combined()
    {
        var soles = Money.Create(100m, Currency.PEN).Value;
        var dollars = Money.Create(100m, Currency.USD).Value;

        Assert.Throws<InvalidOperationException>(() => soles.Add(dollars));
        Assert.Throws<InvalidOperationException>(() => soles.Subtract(dollars));
    }

    [Fact]
    public void A_month_range_covers_its_first_and_last_day()
    {
        var february = DateRange.ForMonth(2028, 2);

        Assert.Equal(new DateOnly(2028, 2, 1), february.Start);
        Assert.Equal(new DateOnly(2028, 2, 29), february.End);
        Assert.Equal(29, february.Days);
        Assert.True(february.Contains(new DateOnly(2028, 2, 29)));
        Assert.False(february.Contains(new DateOnly(2028, 3, 1)));
    }

    [Fact]
    public void A_range_that_ends_before_it_starts_is_rejected()
    {
        var range = DateRange.Create(new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 11));

        Assert.True(range.IsFailure);
        Assert.Equal("date_range.inverted", range.Error.Code);
    }
}
