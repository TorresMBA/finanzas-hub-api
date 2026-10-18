using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Budgets.Contracts;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Goals.Contracts;
using PersonalFinance.Modules.Ledger.Contracts;
using PersonalFinance.Modules.Reporting.Application.NetWorth;
using PersonalFinance.Modules.Scheduling.Contracts;

namespace PersonalFinance.Modules.Reporting.Application.Dashboard;

/// <param name="Amount">Net worth at the end of the month, with the movements up to then.</param>
/// <param name="Debts">Positive: what is owed.</param>
/// <param name="Change">Against the end of the month before.</param>
public sealed record DashboardNetWorthResponse(decimal Amount, decimal Assets, decimal Debts, decimal PreviousAmount, decimal Change);

/// <param name="Month">As <c>2026-10</c>.</param>
public sealed record DashboardMonthResponse(string Month, decimal Income, decimal Expense);

/// <summary>Something still to be paid: a scheduled expense or a credit card falling due.</summary>
/// <param name="Amount">Positive, in its own currency.</param>
public sealed record DashboardUpcomingResponse(DateOnly Date, string Description, decimal Amount, string Currency, bool IsCardPayment);

/// <summary>A goal the person takes part in. What was saved is by all its participants.</summary>
/// <param name="Percent">Of the target; it passes 100 when more was saved.</param>
public sealed record DashboardGoalResponse(Guid Id, string Name, decimal Saved, decimal TargetAmount, string Currency, decimal Percent, bool IsShared);

/// <summary>The budgets of the month added up.</summary>
/// <param name="UsedPercent">What was spent of the limit; it passes 100 when the limit was passed.</param>
public sealed record DashboardBudgetResponse(decimal Limit, decimal Spent, decimal UsedPercent);

/// <summary>What a category and everything under it spent in the month.</summary>
/// <param name="Share">Its part of the month's expenses, from 0 to 100.</param>
public sealed record DashboardCategoryResponse(Guid CategoryId, string Name, string Icon, string Color, decimal Amount, decimal Share);

/// <param name="SignedAmount">In the currency of the account, not converted. Negative when the money left it.</param>
/// <param name="CounterAccountName">The destination of a transfer.</param>
public sealed record DashboardMovementResponse(
    Guid Id, LedgerMovementKind Kind, DateOnly OccurredOn, string Description, decimal SignedAmount, string Currency, string AccountName,
    string? CounterAccountName, string? CategoryName, string? CategoryIcon, string? CategoryColor);

/// <param name="Balance">In the currency of the account; negative when it is owed.</param>
public sealed record DashboardAccountResponse(Guid Id, string Name, LedgerAccountType Type, string Currency, decimal Balance, bool IsFavorite);

/// <summary>
/// The home page of one person for one month. Every amount that adds things up is in the base currency of the
/// workspace; movements and accounts keep their own.
/// </summary>
/// <param name="Month">As <c>2026-10</c>.</param>
/// <param name="BaseCurrency">Null while the workspace has not chosen one.</param>
/// <param name="SavingsRate">What was left of the income, from 0 to 100; negative when more was spent than earned. Null in a month without income.</param>
/// <param name="Cashflow">The six months that end in this one, oldest first.</param>
/// <param name="Budget">Null when no budget applies to the month.</param>
/// <param name="Goals">The open goals closest to their target.</param>
/// <param name="Upcoming">The next payments of the thirty days from today, the soonest first.</param>
/// <param name="MissingRates">Currencies that could not be converted and are left out of the amounts.</param>
public sealed record DashboardResponse(
    string Month, string? BaseCurrency, DashboardNetWorthResponse NetWorth, decimal Income, decimal Expense, decimal PreviousIncome, decimal PreviousExpense,
    decimal? SavingsRate, IReadOnlyList<DashboardMonthResponse> Cashflow, IReadOnlyList<DashboardCategoryResponse> TopCategories,
    IReadOnlyList<DashboardMovementResponse> RecentMovements, IReadOnlyList<DashboardAccountResponse> Accounts, DashboardBudgetResponse? Budget, IReadOnlyList<DashboardUpcomingResponse> Upcoming, IReadOnlyList<DashboardGoalResponse> Goals,
    IReadOnlyList<string> MissingRates);

/// <param name="Month">As <c>2026-10</c>. The current month of the workspace when left out.</param>
public sealed record GetDashboardQuery(string? Month) : IQuery<DashboardResponse>;

/// <summary>
/// Puts the home page together from what the ledger and the categories answer. It owns no data, and everything
/// it reads is already limited to the current person by the modules it asks.
/// </summary>
internal sealed class GetDashboardHandler(
    ILedgerQueries ledger, IBudgetQueries budgets, IScheduleQueries schedule, IGoalQueries goals, ICategoryLookup categories, NetWorthCalculator netWorthOf)
    : IQueryHandler<GetDashboardQuery, DashboardResponse>
{
    private const int CashflowMonths = 6;
    private const int TopCategories = 5;
    private const int RecentMovements = 5;
    private const int MainAccounts = 4;
    private const int UpcomingPayments = 5;
    private const int UpcomingDays = 30;
    private const int MainGoals = 3;

    // Same meter name as the platform's, which is the one the hosts export.
    private static readonly Histogram<double> Duration =
        new Meter("PersonalFinance").CreateHistogram<double>("reporting.dashboard.duration", "ms", "Time to put the dashboard together.");

    public async Task<Result<DashboardResponse>> HandleAsync(GetDashboardQuery query, CancellationToken cancellationToken)
    {
        DateOnly first;
        if (string.IsNullOrWhiteSpace(query.Month))
        {
            var today = Today();
            first = new DateOnly(today.Year, today.Month, 1);
        }
        else if (!DateOnly.TryParseExact(query.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out first))
        {
            return Error.Validation("reporting.month_invalid", "The month must look like 2026-10.");
        }

        var started = Stopwatch.GetTimestamp();
        var last = first.AddMonths(1).AddDays(-1);

        // The same figure as Cuentas: one definition of net worth, with the investments at their market value.
        var netWorth = await netWorthOf.OnAsync(last, cancellationToken);
        var previousNetWorth = await netWorthOf.OnAsync(first.AddDays(-1), cancellationToken);
        var flow = await ledger.GetFlowAsync(first.AddMonths(1 - CashflowMonths), last, cancellationToken);
        var totals = await ledger.GetCategoryTotalsAsync(first, last, cancellationToken);
        var movements = await ledger.GetRecentMovementsAsync(first, last, RecentMovements, cancellationToken);
        var accounts = await ledger.GetAccountBalancesAsync(cancellationToken);
        var budget = await budgets.GetMonthAsync(first, cancellationToken);
        var upcoming = await schedule.GetUpcomingAsync(Today(), Today().AddDays(UpcomingDays), UpcomingPayments, cancellationToken);
        var info = await categories.GetAsync(
            totals.Categories.Select(total => total.CategoryId).Concat(movements.Where(movement => movement.CategoryId != null).Select(movement => movement.CategoryId!.Value))
                .Distinct().ToList(),
            cancellationToken);

        var (month, previous) = (flow.Months[^1], flow.Months[^2]);
        var response = new DashboardResponse(
            Label(first.Year, first.Month),
            netWorth.BaseCurrency,
            new DashboardNetWorthResponse(
                netWorth.Amount, netWorth.Assets, netWorth.Debts, previousNetWorth.Amount, netWorth.Amount - previousNetWorth.Amount),
            month.Income,
            month.Expense,
            previous.Income,
            previous.Expense,
            month.Income > 0 ? decimal.Round((month.Income - month.Expense) / month.Income * 100, 1) : null,
            flow.Months.Select(item => new DashboardMonthResponse(Label(item.Year, item.Month), item.Income, item.Expense)).ToList(),
            Top(totals, info, month.Expense),
            movements.Select(movement =>
            {
                var category = movement.CategoryId is { } id ? info.GetValueOrDefault(id) : null;
                return new DashboardMovementResponse(
                    movement.Id, movement.Kind, movement.OccurredOn, movement.Description, movement.SignedAmount, movement.Currency, movement.AccountName,
                    movement.CounterAccountName, category?.Name, category?.Icon, category?.Color);
            }).ToList(),
            accounts.OrderByDescending(account => account.IsFavorite).ThenByDescending(account => Math.Abs(account.BaseBalance ?? account.Balance))
                .Take(MainAccounts)
                .Select(account => new DashboardAccountResponse(account.Id, account.Name, account.Type, account.Currency, account.Balance, account.IsFavorite))
                .ToList(),
            budget.Count == 0 ? null : new DashboardBudgetResponse(budget.Limit, budget.Spent, decimal.Round(budget.Spent * 100 / budget.Limit, 1)),
            upcoming.Select(payment => new DashboardUpcomingResponse(payment.Date, payment.Description, payment.Amount, payment.Currency, payment.IsCardPayment)).ToList(),
            (await goals.GetActiveAsync(MainGoals, cancellationToken))
                .Select(goal => new DashboardGoalResponse(goal.Id, goal.Name, goal.Saved, goal.TargetAmount, goal.Currency, goal.Percent, goal.IsShared)).ToList(),
            netWorth.MissingRates.Union(flow.MissingRates).Union(totals.MissingRates).Order(StringComparer.Ordinal).ToList());

        Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return response;
    }

    /// <summary>The expense categories that weigh most, each with its subcategories added in.</summary>
    private static List<DashboardCategoryResponse> Top(LedgerCategoryTotals totals, IReadOnlyDictionary<Guid, CategoryInfo> info, decimal expense) =>
        totals.Categories
            .Where(total => !total.IsIncome && info.ContainsKey(total.CategoryId))
            .GroupBy(total => info[total.CategoryId].RootId)
            .Select(group =>
            {
                var any = info[group.First().CategoryId];
                var amount = group.Sum(total => total.Amount);
                return new DashboardCategoryResponse(
                    group.Key, any.ParentName ?? any.Name, any.Icon, any.Color, amount, expense > 0 ? decimal.Round(amount / expense * 100, 1) : 0);
            })
            .OrderByDescending(category => category.Amount)
            .Take(TopCategories)
            .ToList();

    private static string Label(int year, int month) => $"{year:D4}-{month:D2}";

    private DateOnly Today() => netWorthOf.Today();
}
