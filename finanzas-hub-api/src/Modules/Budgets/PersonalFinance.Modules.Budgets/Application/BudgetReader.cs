using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Budgets.Application.Abstractions;
using PersonalFinance.Modules.Budgets.Contracts;
using PersonalFinance.Modules.Budgets.Domain;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Ledger.Contracts;

namespace PersonalFinance.Modules.Budgets.Application;

internal static class BudgetErrors
{
    public static readonly Error Unauthenticated = Error.Unauthorized("auth.unauthenticated", "Authentication is required.");

    public static readonly Error NotFound = Error.NotFound("budgets.not_found", "The budget was not found.");
}

/// <summary>A budget in one month: the plan and how the month is going against it.</summary>
/// <param name="Id">Of the budget that applies in the month asked for. It changes when the limit is adjusted in a later month.</param>
/// <param name="Limit">In the base currency of the workspace, like every amount here.</param>
/// <param name="Remaining">Negative when the limit was passed.</param>
/// <param name="DailyAllowance">What can still be spent per day within the limit. Null for a fixed budget, a month gone or no margin.</param>
/// <param name="MovementCount">Expenses of the month in the category and its subcategories.</param>
public sealed record BudgetResponse(
    Guid Id, Guid CategoryId, string CategoryName, string CategoryIcon, string CategoryColor, bool CategoryIsArchived, decimal Limit, string Currency,
    int AlertThresholdPercent, bool IsFixed, DateOnly EffectiveFrom, bool IsInForce, decimal Spent, decimal Remaining, decimal UsedPercent, BudgetStatus Status,
    decimal? DailyAllowance, int MovementCount);

/// <summary>The budgets of one person for one month.</summary>
/// <param name="Month">As <c>2026-10</c>.</param>
/// <param name="Day">The day of the month it is today; the last one for a month gone, zero for one to come.</param>
/// <param name="DaysLeft">Today included.</param>
/// <param name="PacePercent">The share of the month gone.</param>
/// <param name="DailyAllowance">What is left of all the budgets, per day still to live. Null when nothing is left.</param>
/// <param name="MissingRates">Currencies that could not be converted; their expenses are left out of what was spent.</param>
public sealed record BudgetsResponse(
    string Month, string? BaseCurrency, int Day, int DaysInMonth, int DaysLeft, decimal PacePercent, decimal Limit, decimal Spent, decimal Remaining,
    decimal? DailyAllowance, IReadOnlyList<BudgetResponse> Budgets, IReadOnlyList<string> MissingRates);

/// <summary>
/// Reads budgets against the ledger. What was spent is asked to the ledger every time and never stored, so a
/// budget cannot disagree with the movements. Everything read is already limited to the current person.
/// </summary>
internal sealed class BudgetReader(
    IBudgetsDb db, ILedgerQueries ledger, ICategoryLookup categories, ICurrentUser currentUser, IWorkspaceContext workspace, IClock clock)
{
    public (Guid WorkspaceId, Guid UserId)? Owner =>
        currentUser.UserId is { } userId && workspace.WorkspaceId is { } workspaceId ? (workspaceId, userId) : null;

    public string? BaseCurrency => workspace.BaseCurrency;

    /// <summary>The first day of the current month in the workspace's time zone: the month does not turn over at midnight UTC.</summary>
    public DateOnly CurrentMonth() => Budget.FirstDay(Today());

    public DateOnly Today()
    {
        var zone = workspace.TimeZone is { } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found) ? found : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
    }

    /// <summary>The first day of a month written as <c>2026-10</c>; the current one when left out.</summary>
    public Result<DateOnly> Month(string? month)
    {
        if (string.IsNullOrWhiteSpace(month))
        {
            return CurrentMonth();
        }

        return DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first)
            ? first
            : Error.Validation("budgets.month_invalid", "The month must look like 2026-10.");
    }

    public async Task<BudgetsResponse> ReadAsync(DateOnly month, CancellationToken cancellationToken)
    {
        var first = Budget.FirstDay(month);
        var budgets = await db.Budgets.AsNoTracking()
            .Where(budget => budget.EffectiveFrom <= first && (budget.EndsOn == null || budget.EndsOn >= first))
            .ToListAsync(cancellationToken);
        var (rows, missing) = await DescribeAsync(budgets, first, cancellationToken);
        var pace = MonthPace.Of(first, Today());
        var (limit, spent) = (rows.Sum(row => row.Limit), rows.Sum(row => row.Spent));
        return new BudgetsResponse(
            $"{first.Year:D4}-{first.Month:D2}", workspace.BaseCurrency, pace.Day, pace.DaysInMonth, pace.DaysLeft, pace.PacePercent, limit, spent, limit - spent,
            BudgetProgress.Allowance(limit - spent, pace), rows, missing);
    }

    public async Task<BudgetResponse> DescribeAsync(Budget budget, DateOnly month, CancellationToken cancellationToken) =>
        (await DescribeAsync([budget], month, cancellationToken)).Rows[0];

    public async Task<BudgetMonthSummary> SummarizeAsync(DateOnly month, CancellationToken cancellationToken)
    {
        var read = await ReadAsync(month, cancellationToken);
        return new BudgetMonthSummary(read.Budgets.Count, read.Limit, read.Spent);
    }

    private async Task<(List<BudgetResponse> Rows, IReadOnlyList<string> MissingRates)> DescribeAsync(
        IReadOnlyList<Budget> budgets, DateOnly first, CancellationToken cancellationToken)
    {
        if (budgets.Count == 0)
        {
            return ([], []);
        }

        var totals = await ledger.GetCategoryTotalsAsync(first, first.AddMonths(1).AddDays(-1), cancellationToken);
        var info = await categories.GetAsync(
            budgets.Select(budget => budget.CategoryId).Concat(totals.Categories.Select(total => total.CategoryId)).Distinct().ToList(), cancellationToken);

        // Only expenses count, each under the root of its category. Transfers and income never reach here.
        var spent = totals.Categories
            .Where(total => !total.IsIncome && info.ContainsKey(total.CategoryId))
            .GroupBy(total => info[total.CategoryId].RootId)
            .ToDictionary(group => group.Key, group => (Amount: group.Sum(total => total.Amount), Count: group.Sum(total => total.Count)));
        var pace = MonthPace.Of(first, Today());
        var rows = budgets
            .Select(budget =>
            {
                var category = info.GetValueOrDefault(budget.CategoryId);
                var (amount, count) = spent.GetValueOrDefault(budget.CategoryId);
                var progress = BudgetProgress.Of(budget.Amount, amount, budget.IsFixed, pace);
                return new BudgetResponse(
                    budget.Id, budget.CategoryId, category?.Name ?? string.Empty, category?.Icon ?? "category", category?.Color ?? "blue", category?.IsArchived ?? false,
                    budget.Amount, budget.Currency, budget.AlertThresholdPercent, budget.IsFixed, budget.EffectiveFrom, budget.IsInForce, progress.Spent,
                    progress.Remaining, progress.UsedPercent, progress.Status, progress.DailyAllowance, count);
            })
            .OrderBy(row => row.CategoryName, StringComparer.InvariantCultureIgnoreCase)
            .ToList();
        return (rows, totals.MissingRates);
    }
}

internal sealed class BudgetQueries(BudgetReader reader) : IBudgetQueries
{
    public Task<BudgetMonthSummary> GetMonthAsync(DateOnly month, CancellationToken cancellationToken) => reader.SummarizeAsync(month, cancellationToken);
}
