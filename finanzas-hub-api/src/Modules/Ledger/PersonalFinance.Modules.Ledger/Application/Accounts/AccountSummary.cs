using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Application.ExchangeRates;

namespace PersonalFinance.Modules.Ledger.Application.Accounts;

/// <summary>What the person has and owes in one currency, without converting anything.</summary>
/// <param name="Debts">Positive: what is owed.</param>
/// <param name="Rate">Units of the base currency for one of this currency; null for the base currency itself and when no rate was given.</param>
public sealed record CurrencyPositionResponse(string Currency, decimal Assets, decimal Debts, decimal Net, decimal? Rate, DateOnly? RateDate);

/// <summary>Net worth: what the accounts that count hold, minus what they owe, in the base currency of the workspace.</summary>
/// <param name="BaseCurrency">Null while the workspace has not chosen one; the three amounts are then zero.</param>
/// <param name="Debts">Positive: what is owed.</param>
/// <param name="MissingRates">Currencies with money in them and no exchange rate. They are left out of the three amounts.</param>
/// <param name="Currencies">The same position currency by currency, nothing converted.</param>
public sealed record AccountSummaryResponse(
    string? BaseCurrency, decimal NetWorth, decimal Assets, decimal Debts, IReadOnlyList<string> MissingRates, IReadOnlyList<CurrencyPositionResponse> Currencies);

public sealed record GetAccountSummaryQuery : IQuery<AccountSummaryResponse>;

internal sealed class GetAccountSummaryHandler(NetWorthReader netWorth) : IQueryHandler<GetAccountSummaryQuery, AccountSummaryResponse>
{
    public async Task<Result<AccountSummaryResponse>> HandleAsync(GetAccountSummaryQuery query, CancellationToken cancellationToken) =>
        await netWorth.ReadAsync(null, cancellationToken);
}

/// <summary>
/// The one place net worth is computed. An account counts when it is not archived and the person has not taken
/// it out. A positive balance is something owned and a negative one something owed, whatever the type of account.
/// </summary>
internal sealed class NetWorthReader(ILedgerDb db, AccountBalances balances, ExchangeRateBook rates)
{
    /// <param name="asOf">Leaves out the movements after that day. Null counts everything.</param>
    public async Task<AccountSummaryResponse> ReadAsync(DateOnly? asOf, CancellationToken cancellationToken)
    {
        var accounts = await db.Accounts.AsNoTracking()
            .Where(account => account.ArchivedAt == null && account.IncludeInNetWorth)
            .Select(account => new { account.Id, account.Currency })
            .ToListAsync(cancellationToken);
        var byAccount = await balances.AllAsync(cancellationToken, asOf);
        var table = await rates.LoadAsync(cancellationToken);

        var (assets, debts) = (new BaseSum(table), new BaseSum(table));
        var currencies = new List<CurrencyPositionResponse>();
        foreach (var group in accounts.GroupBy(account => account.Currency).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var held = group.Select(account => byAccount.GetValueOrDefault(account.Id)).ToList();
            var owned = held.Where(balance => balance > 0).Sum();
            var owed = -held.Where(balance => balance < 0).Sum();
            assets.Add(group.Key, owned);
            debts.Add(group.Key, owed);
            var rate = group.Key == table.BaseCurrency ? null : table.Latest(group.Key);
            currencies.Add(new CurrencyPositionResponse(group.Key, owned, owed, owned - owed, rate?.Rate, rate?.AsOf));
        }

        return new AccountSummaryResponse(
            table.BaseCurrency, assets.Total - debts.Total, assets.Total, debts.Total, assets.Missing.Union(debts.Missing).Order(StringComparer.Ordinal).ToList(), currencies);
    }
}
