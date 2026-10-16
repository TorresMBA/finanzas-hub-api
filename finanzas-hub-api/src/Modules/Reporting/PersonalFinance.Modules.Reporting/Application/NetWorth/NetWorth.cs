using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Investments.Contracts;
using PersonalFinance.Modules.Ledger.Contracts;

namespace PersonalFinance.Modules.Reporting.Application.NetWorth;

/// <param name="Amount">What is owned minus what is owed, in the base currency, with the investments at their market value.</param>
/// <param name="Debts">Positive: what is owed.</param>
/// <param name="UnrealizedGain">What the investments are worth above the capital put into them; negative when they lost.</param>
/// <param name="MissingRates">Currencies that could not be converted and are left out of the amounts.</param>
public sealed record NetWorthResponse(string? BaseCurrency, decimal Amount, decimal Assets, decimal Debts, decimal UnrealizedGain, IReadOnlyList<string> MissingRates);

/// <summary>The net worth of the signed-in person today: the same figure the home page shows for the current month.</summary>
public sealed record GetNetWorthQuery : IQuery<NetWorthResponse>;

internal sealed class GetNetWorthHandler(NetWorthCalculator calculator) : IQueryHandler<GetNetWorthQuery, NetWorthResponse>
{
    public async Task<Result<NetWorthResponse>> HandleAsync(GetNetWorthQuery query, CancellationToken cancellationToken) =>
        await calculator.OnAsync(calculator.Today(), cancellationToken);
}

/// <summary>
/// The one definition of net worth. The ledger counts the capital of the investments; what they gained on top of it
/// comes from their valuations, never further than today: the interest of days still to come is not money yet.
/// </summary>
internal sealed class NetWorthCalculator(ILedgerQueries ledger, IPortfolioValuation portfolio, IPersonalSpace space, IClock clock)
{
    public async Task<NetWorthResponse> OnAsync(DateOnly asOf, CancellationToken cancellationToken)
    {
        var books = await ledger.GetNetWorthAsync(asOf, cancellationToken);
        var today = Today();
        var invested = await portfolio.GetAsync(asOf < today ? asOf : today, cancellationToken);
        var gain = invested.UnrealizedGain;
        return new NetWorthResponse(
            books.BaseCurrency, books.NetWorth + gain, books.Assets + gain, books.Debts, gain,
            books.MissingRates.Union(invested.MissingRates).Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>Today in the person's time zone: the month does not turn over at midnight UTC.</summary>
    public DateOnly Today()
    {
        var zone = space.TimeZone is { } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found) ? found : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
    }
}
