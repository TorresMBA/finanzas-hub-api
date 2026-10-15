using PersonalFinance.Modules.Investments.Domain;

namespace PersonalFinance.UnitTests.Investments;

public class HoldingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Start = new(2026, 1, 1);

    private static Holding Open(InvestmentType type, decimal? rate = null, int? days = null) =>
        Holding.Open(Guid.NewGuid(), Guid.NewGuid(), type, "Prueba", null, null, "PEN", Start, rate, days, Now).Value;

    [Fact]
    public void A_term_deposit_grows_with_its_rate_over_360_days_and_stops_at_maturity()
    {
        var deposit = Open(InvestmentType.TermDeposit, rate: 6m, days: 360);
        deposit.Contribute(Guid.NewGuid(), 15_000m, null, Start, Guid.NewGuid(), Now);

        Assert.Equal(15_900.00m, deposit.ProjectedValue);
        Assert.Equal((15_000m, 15_900.00m, 15_900.00m), (deposit.ValueOn(Start), deposit.ValueOn(Start.AddDays(360)), deposit.ValueOn(Start.AddDays(500))));
        Assert.Equal("investments.deposit_fixed", deposit.Contribute(Guid.NewGuid(), 100m, null, Start.AddDays(10), Guid.NewGuid(), Now).Error.Code);
    }

    [Fact]
    public void A_redemption_takes_back_capital_in_proportion_to_the_value_and_the_rest_is_gain()
    {
        var etf = Open(InvestmentType.Etf);
        etf.Contribute(Guid.NewGuid(), 1_000m, 10m, Start, Guid.NewGuid(), Now);
        Assert.True(etf.RecordValuation(Guid.NewGuid(), Start.AddDays(30), null, 120m, Now).IsSuccess);
        etf.Contribute(Guid.NewGuid(), 300m, 2m, Start.AddDays(40), Guid.NewGuid(), Now);

        // Worth its valuation plus the money put in after it.
        Assert.Equal((1_200m, 1_500m, 1_300m), (etf.ValueOn(Start.AddDays(35)), etf.ValueOn(Start.AddDays(40)), etf.CapitalOn(Start.AddDays(40))));

        var redemption = etf.Withdraw(Guid.NewGuid(), 750m, 6m, Start.AddDays(50), Guid.NewGuid(), Now).Value;
        Assert.Equal((650m, 100m), (redemption.Capital, redemption.Realized));
        Assert.Equal((750m, 650m, 6m), (etf.ValueOn(Start.AddDays(50)), etf.CapitalOn(Start.AddDays(50)), etf.Quantity));

        Assert.Equal("investments.withdrawal_too_large", etf.Withdraw(Guid.NewGuid(), 751m, null, Start.AddDays(51), Guid.NewGuid(), Now).Error.Code);

        // Taking all of it closes it, with all the capital left.
        var all = etf.Withdraw(Guid.NewGuid(), 750m, null, Start.AddDays(52), Guid.NewGuid(), Now).Value;
        Assert.Equal((650m, 100m, true, 0m), (all.Capital, all.Realized, etf.IsClosed, etf.Quantity));
    }
}
