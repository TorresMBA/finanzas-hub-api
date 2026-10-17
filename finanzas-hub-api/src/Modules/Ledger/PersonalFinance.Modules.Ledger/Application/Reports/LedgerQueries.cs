using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Application.Accounts;
using PersonalFinance.Modules.Ledger.Application.ExchangeRates;
using PersonalFinance.Modules.Ledger.Contracts;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.Reports;

/// <summary>
/// What the ledger answers to other modules. No query here names an owner: the context's global filter limits
/// accounts, operations, entries and rates to the current person, so a report can only ever be about them.
/// </summary>
internal sealed class LedgerQueries(ILedgerDb db, AccountBalances balances, NetWorthReader netWorth, ExchangeRateBook rates) : ILedgerQueries
{
    // The rates do not change within a request; a calendar converts many amounts.
    private RateTable? _table;

    public async Task<LedgerNetWorth> GetNetWorthAsync(DateOnly asOf, CancellationToken cancellationToken)
    {
        var summary = await netWorth.ReadAsync(asOf, cancellationToken);
        return new LedgerNetWorth(summary.BaseCurrency, summary.NetWorth, summary.Assets, summary.Debts, summary.MissingRates);
    }

    public async Task<LedgerFlow> GetFlowAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await Transactions(from, to)
            .SelectMany(operation => operation.Entries, (operation, entry) => new
            {
                Kind = operation.Kind == OperationKind.Refund ? OperationKind.Expense : operation.Kind,
                operation.OccurredOn.Year,
                operation.OccurredOn.Month,
                entry.Currency,
                FromGoal = operation.FundedByGoalId != null,
                Amount = operation.Kind == OperationKind.Refund ? -entry.Amount : entry.Amount,
                BaseAmount = operation.Kind == OperationKind.Refund ? -entry.BaseAmount : entry.BaseAmount
            })
            .GroupBy(row => new { row.Kind, row.Year, row.Month, row.Currency, row.FromGoal })
            .Select(group => new
            {
                group.Key.Kind,
                group.Key.Year,
                group.Key.Month,
                group.Key.Currency,
                group.Key.FromGoal,
                Valued = group.Sum(row => row.BaseAmount ?? 0m),
                Unvalued = group.Sum(row => row.BaseAmount == null ? row.Amount : 0m)
            })
            .ToListAsync(cancellationToken);

        var table = await rates.LoadAsync(cancellationToken);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var months = new List<LedgerMonthFlow>();
        for (var month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
        {
            var (income, expense, fromGoals) = (new BaseSum(table), new BaseSum(table), new BaseSum(table));
            foreach (var row in rows.Where(row => row.Year == month.Year && row.Month == month.Month))
            {
                (row.Kind == OperationKind.Income ? income : expense).Add(row.Currency, row.Unvalued, row.Valued);
                if (row.Kind != OperationKind.Income && row.FromGoal)
                {
                    fromGoals.Add(row.Currency, row.Unvalued, row.Valued);
                }
            }

            missing.UnionWith(income.Missing);
            missing.UnionWith(expense.Missing);
            months.Add(new LedgerMonthFlow(month.Year, month.Month, income.Total, expense.Total, fromGoals.Total));
        }

        return new LedgerFlow(months, [.. missing]);
    }

    public async Task<LedgerCategoryTotals> GetCategoryTotalsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await Transactions(from, to)
            .SelectMany(operation => operation.Entries, (operation, entry) => new
            {
                Kind = operation.Kind == OperationKind.Refund ? OperationKind.Expense : operation.Kind,
                operation.CategoryId,
                entry.Currency,
                Amount = operation.Kind == OperationKind.Refund ? -entry.Amount : entry.Amount,
                BaseAmount = operation.Kind == OperationKind.Refund ? -entry.BaseAmount : entry.BaseAmount
            })
            .GroupBy(row => new { row.Kind, row.CategoryId, row.Currency })
            .Select(group => new
            {
                group.Key.Kind,
                group.Key.CategoryId,
                group.Key.Currency,
                Count = group.Count(),
                Valued = group.Sum(row => row.BaseAmount ?? 0m),
                Unvalued = group.Sum(row => row.BaseAmount == null ? row.Amount : 0m)
            })
            .ToListAsync(cancellationToken);

        var table = await rates.LoadAsync(cancellationToken);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var categories = new List<LedgerCategoryTotal>();
        foreach (var group in rows.Where(row => row.CategoryId != null).GroupBy(row => new { row.Kind, CategoryId = row.CategoryId!.Value }))
        {
            var sum = new BaseSum(table);
            foreach (var row in group)
            {
                sum.Add(row.Currency, row.Unvalued, row.Valued);
            }

            missing.UnionWith(sum.Missing);
            categories.Add(new LedgerCategoryTotal(group.Key.CategoryId, group.Key.Kind == OperationKind.Income, sum.Total, group.Sum(row => row.Count)));
        }

        return new LedgerCategoryTotals(categories, [.. missing]);
    }

    public async Task<IReadOnlyList<LedgerExpense>> GetExpensesAsync(
        IReadOnlyCollection<Guid> categoryIds, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await Transactions(from, to)
            .Where(operation => operation.Kind == OperationKind.Expense && operation.CategoryId != null && categoryIds.Contains(operation.CategoryId.Value))
            .SelectMany(operation => operation.Entries, (operation, entry) => new
            {
                operation.Id, operation.OccurredOn, operation.Description, CategoryId = operation.CategoryId!.Value, entry.AccountId, entry.Amount, entry.Currency,
                entry.BaseAmount
            })
            .ToListAsync(cancellationToken);
        var accountIds = rows.Select(row => row.AccountId).Distinct().ToList();
        var names = await db.Accounts.AsNoTracking()
            .Where(account => accountIds.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id, account => account.Name, cancellationToken);
        var table = await rates.LoadAsync(cancellationToken);
        return rows
            .OrderByDescending(row => row.OccurredOn)
            .ThenByDescending(row => row.Id)
            .Select(row => new LedgerExpense(
                row.Id, row.OccurredOn, row.Description, row.CategoryId, names.GetValueOrDefault(row.AccountId, string.Empty), row.Amount, row.Currency,
                row.BaseAmount ?? table.ToBase(row.Currency, row.Amount)))
            .ToList();
    }

    public async Task<IReadOnlyList<LedgerCardDue>> GetCardDueDatesAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var cards = await db.Accounts.AsNoTracking().Where(account => account.ArchivedAt == null && account.Type == AccountType.CreditCard).ToListAsync(cancellationToken);
        var byAccount = await balances.AllAsync(cancellationToken);
        return cards
            .Where(card => card.CreditCard is not null && byAccount.GetValueOrDefault(card.Id) < 0)
            .Select(card => new LedgerCardDue(card.Id, card.Name, card.Currency, -byAccount.GetValueOrDefault(card.Id), card.CreditCard!.NextPaymentDate(today)))
            .ToList();
    }

    public async Task<IReadOnlyList<LedgerCardDate>> GetCardCalendarAsync(DateOnly from, DateOnly to, DateOnly today, CancellationToken cancellationToken)
    {
        var cards = await db.Accounts.AsNoTracking().Where(account => account.ArchivedAt == null && account.Type == AccountType.CreditCard).ToListAsync(cancellationToken);
        var byAccount = await balances.AllAsync(cancellationToken);
        return cards
            .Where(card => card.CreditCard is not null)
            .SelectMany(card => CardDates(card, -byAccount.GetValueOrDefault(card.Id), from, to, today))
            .ToList();
    }

    /// <summary>The closings and payment days of one card in a period. Only the next payment carries what the card owes.</summary>
    private static IEnumerable<LedgerCardDate> CardDates(Account card, decimal debt, DateOnly from, DateOnly to, DateOnly today)
    {
        var profile = card.CreditCard!;
        var next = profile.NextPaymentDate(today);
        for (var month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
        {
            if (profile.ClosingDateIn(month.Year, month.Month) is { } closing && closing >= from && closing <= to)
            {
                yield return new LedgerCardDate(card.Id, card.Name, card.Currency, closing, true, null);
            }

            var payment = profile.PaymentDateIn(month.Year, month.Month);
            if (payment >= from && payment <= to)
            {
                yield return new LedgerCardDate(card.Id, card.Name, card.Currency, payment, false, payment == next && debt > 0 ? debt : null);
            }
        }
    }

    public async Task<decimal?> ToBaseAsync(string currency, decimal amount, CancellationToken cancellationToken) =>
        (_table ??= await rates.LoadAsync(cancellationToken)).ToBase(currency, amount);

    public async Task<IReadOnlyList<LedgerAccountBalance>> GetAccountBalancesAsync(CancellationToken cancellationToken)
    {
        var accounts = await db.Accounts.AsNoTracking().Where(account => account.ArchivedAt == null).OrderBy(account => account.CreatedAt).ToListAsync(cancellationToken);
        var byAccount = await balances.AllAsync(cancellationToken);
        var table = await rates.LoadAsync(cancellationToken);
        return accounts.Select(account =>
        {
            var balance = byAccount.GetValueOrDefault(account.Id);
            return new LedgerAccountBalance(
                account.Id, account.Name, Enum.Parse<LedgerAccountType>(account.Type.ToString()), account.Currency, balance, table.ToBase(account.Currency, balance),
                account.CreditCard?.CreditLimit, account.IncludeInNetWorth);
        }).ToList();
    }

    public Task<IReadOnlyList<LedgerMovement>> GetRecentMovementsAsync(DateOnly from, DateOnly to, int count, CancellationToken cancellationToken) =>
        DescribeAsync(
            Movements().Where(operation => operation.OccurredOn >= from && operation.OccurredOn <= to)
                .OrderByDescending(operation => operation.OccurredOn).ThenByDescending(operation => operation.Id)
                .Take(Math.Clamp(count, 1, 500)),
            cancellationToken);

    public Task<IReadOnlyList<LedgerMovement>> GetMovementsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        DescribeAsync(
            Movements().Where(operation => operation.OccurredOn >= from && operation.OccurredOn <= to)
                .OrderBy(operation => operation.OccurredOn).ThenBy(operation => operation.Id)
                .Take(ILedgerQueries.MaxExportedMovements),
            cancellationToken);

    public Task<IReadOnlyList<LedgerMovement>> SearchMovementsAsync(string text, int count, CancellationToken cancellationToken)
    {
        var lowered = text.Trim().ToLowerInvariant();
        // EF translates ToLower().Contains to lower(...) LIKE in SQL; the StringComparison overload CA1862 asks for is not translatable.
#pragma warning disable CA1862
        return DescribeAsync(
            Movements().Where(operation =>
                    operation.Description.ToLower().Contains(lowered)
                    || (operation.Merchant != null && operation.Merchant.ToLower().Contains(lowered))
                    || (operation.Note != null && operation.Note.ToLower().Contains(lowered)))
#pragma warning restore CA1862
                .OrderByDescending(operation => operation.OccurredOn).ThenByDescending(operation => operation.Id)
                .Take(Math.Clamp(count, 1, 50)),
            cancellationToken);
    }

    public async Task<IReadOnlyList<LedgerAccountBalance>> SearchAccountsAsync(string text, int count, CancellationToken cancellationToken)
    {
        var lowered = text.Trim().ToLowerInvariant();
        return (await GetAccountBalancesAsync(cancellationToken))
            .Where(account => account.Name.Contains(lowered, StringComparison.CurrentCultureIgnoreCase))
            .Take(Math.Clamp(count, 1, 50))
            .ToList();
    }

    public async Task<LedgerMerchantTotals> GetMerchantTotalsAsync(DateOnly from, DateOnly to, int count, CancellationToken cancellationToken)
    {
        var rows = await Transactions(from, to)
            .Where(operation => operation.Kind == OperationKind.Expense)
            .SelectMany(operation => operation.Entries, (operation, entry) => new
            {
                operation.Merchant, operation.Description, entry.Currency, entry.Amount, entry.BaseAmount
            })
            .ToListAsync(cancellationToken);

        // Grouped here and not in the database: the key needs the rules of MerchantName, and a person's expenses of a year are few.
        var table = await rates.LoadAsync(cancellationToken);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var merchants = rows
            .Select(row => (Row: row, Key: MerchantName.KeyOf(row.Merchant, row.Description)))
            .Where(item => item.Key.Length > 0)
            .GroupBy(item => item.Key)
            .Select(group =>
            {
                var sum = new BaseSum(table);
                foreach (var (row, _) in group)
                {
                    sum.Add(row.Currency, row.BaseAmount == null ? row.Amount : 0m, row.BaseAmount ?? 0m);
                }

                missing.UnionWith(sum.Missing);
                var name = group.Select(item => (string.IsNullOrWhiteSpace(item.Row.Merchant) ? item.Row.Description : item.Row.Merchant).Trim())
                    .GroupBy(spelling => spelling).OrderByDescending(spelling => spelling.Count()).ThenBy(spelling => spelling.Key, StringComparer.Ordinal)
                    .First().Key;
                return new LedgerMerchantTotal(name, group.Count(), sum.Total);
            })
            .OrderByDescending(merchant => merchant.Amount)
            .ThenByDescending(merchant => merchant.Count)
            .Take(Math.Clamp(count, 1, 100))
            .ToList();
        return new LedgerMerchantTotals(merchants, [.. missing]);
    }

    /// <summary>The income, expenses and transfers a person sees in their movements: not the internal ones (opening balances, adjustments).</summary>
    private IQueryable<Operation> Movements() =>
        db.Operations.AsNoTracking().Where(operation => operation.Status != OperationStatus.Voided
            && (operation.Kind == OperationKind.Income || operation.Kind == OperationKind.Expense || operation.Kind == OperationKind.Transfer));

    private async Task<IReadOnlyList<LedgerMovement>> DescribeAsync(IQueryable<Operation> query, CancellationToken cancellationToken)
    {
        var operations = await query.Include(operation => operation.Entries).ToListAsync(cancellationToken);
        var accountIds = operations.SelectMany(operation => operation.Entries).Select(entry => entry.AccountId).Distinct().ToList();
        var names = await db.Accounts.AsNoTracking()
            .Where(account => accountIds.Contains(account.Id))
            .Select(account => new { account.Id, account.Name })
            .ToDictionaryAsync(account => account.Id, account => account.Name, cancellationToken);

        return operations.Select(operation =>
        {
            var entry = operation.Entries.FirstOrDefault(candidate => candidate.Direction == EntryDirection.Out) ?? operation.Entries.First();
            var arriving = operation.Kind == OperationKind.Transfer ? operation.Entries.First(candidate => candidate.Direction == EntryDirection.In) : null;
            return new LedgerMovement(
                operation.Id,
                operation.Kind switch
                {
                    OperationKind.Income => LedgerMovementKind.Income,
                    OperationKind.Expense => LedgerMovementKind.Expense,
                    _ => LedgerMovementKind.Transfer
                },
                operation.OccurredOn, operation.Description, entry.Direction == EntryDirection.In ? entry.Amount : -entry.Amount, entry.Currency,
                names.GetValueOrDefault(entry.AccountId, string.Empty),
                arriving is null ? null : names.GetValueOrDefault(arriving.AccountId, string.Empty),
                operation.CategoryId, operation.Merchant, operation.Note);
        }).ToList();
    }

    /// <summary>
    /// The income and expenses that count in a period: what every report of money in and out is made of. A refund
    /// comes along because it is an expense in reverse: the reports count it as one with its amount negated.
    /// </summary>
    private IQueryable<Operation> Transactions(DateOnly from, DateOnly to) =>
        db.Operations.AsNoTracking().Where(operation =>
            operation.Status != OperationStatus.Voided
            && (operation.Kind == OperationKind.Income || operation.Kind == OperationKind.Expense || operation.Kind == OperationKind.Refund)
            && operation.OccurredOn >= from && operation.OccurredOn <= to);
}

/// <param name="Amount">In the base currency of the workspace.</param>
public sealed record CategoryTotalResponse(Guid CategoryId, decimal Amount, int Count);

/// <summary>What each category moved in a period. A subcategory is reported apart from its parent.</summary>
/// <param name="BaseCurrency">Null while the workspace has not chosen one.</param>
/// <param name="MissingRates">Currencies that could not be converted and are left out of the amounts.</param>
public sealed record CategoryTotalsResponse(string? BaseCurrency, IReadOnlyList<CategoryTotalResponse> Categories, IReadOnlyList<string> MissingRates);

public sealed record GetCategoryTotalsQuery(DateOnly From, DateOnly To) : IQuery<CategoryTotalsResponse>;

internal sealed class GetCategoryTotalsHandler(ILedgerQueries ledger, ExchangeRateBook rates) : IQueryHandler<GetCategoryTotalsQuery, CategoryTotalsResponse>
{
    public async Task<Result<CategoryTotalsResponse>> HandleAsync(GetCategoryTotalsQuery query, CancellationToken cancellationToken)
    {
        if (query.From > query.To)
        {
            return Error.Validation("ledger.period_invalid", "The period ends before it starts.");
        }

        var totals = await ledger.GetCategoryTotalsAsync(query.From, query.To, cancellationToken);
        return new CategoryTotalsResponse(
            (await rates.LoadAsync(cancellationToken)).BaseCurrency,
            totals.Categories.Select(category => new CategoryTotalResponse(category.CategoryId, category.Amount, category.Count)).ToList(),
            totals.MissingRates);
    }
}
