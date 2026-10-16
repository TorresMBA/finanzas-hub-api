using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Goals.Contracts;
using PersonalFinance.Modules.Ledger.Contracts;

namespace PersonalFinance.Modules.Reporting.Application.Reports;

/// <param name="Savings">Income minus expenses; negative when more was spent.</param>
/// <param name="SavingsRate">What was left of the income, from 0 to 100; negative when more was spent than earned. Null without income.</param>
/// <param name="DailyExpense">The expenses spread over the days of the period gone by.</param>
/// <param name="Days">The days that average counts.</param>
public sealed record ReportTotalsResponse(decimal Income, decimal Expense, decimal Savings, decimal? SavingsRate, decimal DailyExpense, int Days);

/// <summary>What a category and everything under it spent, against the period before.</summary>
/// <param name="Share">Its part of the period's expenses, from 0 to 100.</param>
/// <param name="Change">How much it moved against the period before, in percent. Null when it spent nothing then.</param>
public sealed record ReportCategoryResponse(Guid CategoryId, string Name, string Icon, string Color, decimal Amount, decimal Share, decimal PreviousAmount, decimal? Change);

/// <param name="Name">The shop, or the description of the expenses that name none.</param>
/// <param name="Count">How many expenses.</param>
public sealed record ReportMerchantResponse(string Name, int Count, decimal Amount);

/// <param name="Month">As <c>2026-10</c>.</param>
public sealed record ReportMonthResponse(string Month, decimal Income, decimal Expense, decimal? SavingsRate);

/// <summary>
/// What a person earned and spent in a month, a quarter or a year, against the period before. Every amount is in
/// the base currency, and every figure comes from the same ledger queries as Transactions and Budgets.
/// </summary>
/// <param name="Label">"2026-10", "2026-T4" or "2026".</param>
/// <param name="BaseCurrency">Null while the workspace has not chosen one.</param>
/// <param name="Categories">Every expense category of either period, the one that spent most first.</param>
/// <param name="Merchants">The shops with most spent.</param>
/// <param name="Months">The savings rate month by month: the six ending with the period, or the twelve of a year.</param>
/// <param name="MissingRates">Currencies that could not be converted and are left out of the amounts.</param>
public sealed record ReportSummaryResponse(
    ReportPeriodKind Period, string Label, DateOnly From, DateOnly To, DateOnly PreviousFrom, DateOnly PreviousTo, string? BaseCurrency,
    ReportTotalsResponse Current, ReportTotalsResponse Previous, IReadOnlyList<ReportCategoryResponse> Categories,
    IReadOnlyList<ReportMerchantResponse> Merchants, IReadOnlyList<ReportMonthResponse> Months, IReadOnlyList<string> MissingRates);

/// <param name="Anchor">A month inside the period, as <c>2026-10</c>.</param>
public sealed record GetReportSummaryQuery(ReportPeriodKind Period, string Anchor) : IQuery<ReportSummaryResponse>;

/// <summary>Puts a report together from what the ledger and the categories answer, already limited to the current person.</summary>
internal sealed class ReportBuilder(ILedgerQueries ledger, ICategoryLookup categories, IPersonalSpace workspace, IClock clock)
{
    public const int Merchants = 5;

    public async Task<ReportSummaryResponse> SummaryAsync(ReportPeriod period, CancellationToken cancellationToken)
    {
        var previous = period.Previous();
        var trend = await ledger.GetFlowAsync(period.TrendFrom, period.To, cancellationToken);
        var before = await ledger.GetFlowAsync(previous.From, previous.To, cancellationToken);
        var totals = await ledger.GetCategoryTotalsAsync(period.From, period.To, cancellationToken);
        var previousTotals = await ledger.GetCategoryTotalsAsync(previous.From, previous.To, cancellationToken);
        var merchants = await ledger.GetMerchantTotalsAsync(period.From, period.To, Merchants, cancellationToken);
        var info = await categories.GetAsync(
            totals.Categories.Concat(previousTotals.Categories).Select(total => total.CategoryId).Distinct().ToList(), cancellationToken);

        var today = Today();
        var current = Totals(trend.Months.Where(month => new DateOnly(month.Year, month.Month, 1) >= period.From), period.DaysUpTo(today));
        var expenses = RootsOf(totals, info);
        var previousExpenses = RootsOf(previousTotals, info);
        var rows = expenses.Keys.Union(previousExpenses.Keys)
            .Select(rootId =>
            {
                var (amount, any) = expenses.TryGetValue(rootId, out var now) ? now : (0m, previousExpenses[rootId].Any);
                var earlier = previousExpenses.TryGetValue(rootId, out var then) ? then.Amount : 0m;
                return new ReportCategoryResponse(
                    rootId, any.ParentName ?? any.Name, any.Icon, any.Color, amount, current.Expense > 0 ? decimal.Round(amount / current.Expense * 100, 1) : 0,
                    earlier, earlier > 0 ? decimal.Round((amount - earlier) / earlier * 100, 1) : null);
            })
            .OrderByDescending(row => row.Amount)
            .ThenByDescending(row => row.PreviousAmount)
            .ToList();

        return new ReportSummaryResponse(
            period.Kind, period.Label, period.From, period.To, previous.From, previous.To, workspace.BaseCurrency,
            current, Totals(before.Months, previous.DaysUpTo(today)), rows,
            merchants.Merchants.Select(merchant => new ReportMerchantResponse(merchant.Name, merchant.Count, merchant.Amount)).ToList(),
            trend.Months.Select(month => new ReportMonthResponse($"{month.Year:D4}-{month.Month:D2}", month.Income, month.Expense, Rate(month.Income, month.Expense))).ToList(),
            trend.MissingRates.Union(before.MissingRates).Union(totals.MissingRates).Union(previousTotals.MissingRates).Union(merchants.MissingRates)
                .Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>Today in the workspace's time zone: the month does not turn over at midnight UTC.</summary>
    public DateOnly Today()
    {
        var zone = workspace.TimeZone is { } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found) ? found : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
    }

    private static ReportTotalsResponse Totals(IEnumerable<LedgerMonthFlow> months, int days)
    {
        var list = months.ToList();
        var (income, expense) = (list.Sum(month => month.Income), list.Sum(month => month.Expense));
        return new ReportTotalsResponse(income, expense, income - expense, Rate(income, expense), days > 0 ? decimal.Round(expense / days, 2) : 0, days);
    }

    private static decimal? Rate(decimal income, decimal expense) => income > 0 ? decimal.Round((income - expense) / income * 100, 1) : null;

    /// <summary>The expenses by top category, each with its subcategories added in.</summary>
    private static Dictionary<Guid, (decimal Amount, CategoryInfo Any)> RootsOf(LedgerCategoryTotals totals, IReadOnlyDictionary<Guid, CategoryInfo> info) =>
        totals.Categories
            .Where(total => !total.IsIncome && info.ContainsKey(total.CategoryId))
            .GroupBy(total => info[total.CategoryId].RootId)
            .ToDictionary(group => group.Key, group => (group.Sum(total => total.Amount), info[group.First().CategoryId]));
}

internal sealed class GetReportSummaryHandler(ReportBuilder builder) : IQueryHandler<GetReportSummaryQuery, ReportSummaryResponse>
{
    private static readonly Histogram<double> Duration =
        new Meter("PersonalFinance").CreateHistogram<double>("reporting.report.duration", "ms", "Time to put a report together.");

    public async Task<Result<ReportSummaryResponse>> HandleAsync(GetReportSummaryQuery query, CancellationToken cancellationToken)
    {
        var period = ReportPeriod.Of(query.Period, query.Anchor);
        if (period.IsFailure)
        {
            return period.Error;
        }

        var started = Stopwatch.GetTimestamp();
        var summary = await builder.SummaryAsync(period.Value, cancellationToken);
        Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, new KeyValuePair<string, object?>("period", query.Period.ToString()));
        return summary;
    }
}

public enum ReportFormat
{
    Xlsx,
    Csv
}

/// <summary>A file to download.</summary>
public sealed record ReportFileResponse(string FileName, string ContentType, byte[] Content)
{
    public const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string CsvType = "text/csv";
}

public sealed record ExportReportQuery(ReportPeriodKind Period, string Anchor, ReportFormat Format) : IQuery<ReportFileResponse>;

/// <summary>
/// The report as a file: an Excel workbook with the summary and every movement of the period, or the movements
/// alone as CSV. Only the person's own data, as everywhere else in the reports.
/// </summary>
internal sealed class ExportReportHandler(ReportBuilder builder, ILedgerQueries ledger, ICategoryLookup categories, ISpreadsheets spreadsheets)
    : IQueryHandler<ExportReportQuery, ReportFileResponse>
{
    private static readonly Counter<long> Exports =
        new Meter("PersonalFinance").CreateCounter<long>("reporting.exports", description: "Reports downloaded, by format.");

    private static readonly string[] MovementHeader = ["Fecha", "Tipo", "Descripción", "Comercio", "Categoría", "Cuenta", "Cuenta destino", "Monto", "Moneda", "Nota"];

    public async Task<Result<ReportFileResponse>> HandleAsync(ExportReportQuery query, CancellationToken cancellationToken)
    {
        var period = ReportPeriod.Of(query.Period, query.Anchor);
        if (period.IsFailure)
        {
            return period.Error;
        }

        var movements = await ledger.GetMovementsAsync(period.Value.From, period.Value.To, cancellationToken);
        var names = await categories.GetAsync(movements.Where(movement => movement.CategoryId != null).Select(movement => movement.CategoryId!.Value).Distinct().ToList(), cancellationToken);
        var rows = movements.Select(movement => Row(movement, names)).ToList();
        var name = $"finanzas-{period.Value.Label}";
        Exports.Add(1, new KeyValuePair<string, object?>("format", query.Format.ToString()));

        if (query.Format == ReportFormat.Csv)
        {
            return new ReportFileResponse($"{name}.csv", ReportFileResponse.CsvType, Csv([MovementHeader, .. rows]));
        }

        var summary = await builder.SummaryAsync(period.Value, cancellationToken);
        var sheets = new List<SpreadsheetSheet>
        {
            new("Resumen", Summary(summary)),
            new("Movimientos", [MovementHeader, .. rows])
        };
        return new ReportFileResponse($"{name}.xlsx", ReportFileResponse.XlsxType, spreadsheets.Write(sheets));
    }

    private static object?[] Row(LedgerMovement movement, IReadOnlyDictionary<Guid, CategoryInfo> names)
    {
        var category = movement.CategoryId is { } id && names.TryGetValue(id, out var info) ? CategoryPath(info) : null;
        var kind = movement.Kind switch
        {
            LedgerMovementKind.Income => "Ingreso",
            LedgerMovementKind.Expense => "Gasto",
            _ => "Transferencia"
        };
        return [movement.OccurredOn, kind, movement.Description, movement.Merchant, category, movement.AccountName, movement.CounterAccountName, movement.SignedAmount, movement.Currency, movement.Note];
    }

    private static string CategoryPath(CategoryInfo info) => info.ParentName is null ? info.Name : $"{info.ParentName} / {info.Name}";

    private static List<IReadOnlyList<object?>> Summary(ReportSummaryResponse report)
    {
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "Concepto", $"Periodo {report.Label}", "Periodo anterior" },
            new object?[] { "Desde", report.From, report.PreviousFrom },
            new object?[] { "Hasta", report.To, report.PreviousTo },
            new object?[] { "Ingresos", report.Current.Income, report.Previous.Income },
            new object?[] { "Gastos", report.Current.Expense, report.Previous.Expense },
            new object?[] { "Ahorro", report.Current.Savings, report.Previous.Savings },
            new object?[] { "Tasa de ahorro (%)", report.Current.SavingsRate, report.Previous.SavingsRate },
            new object?[] { "Gasto promedio diario", report.Current.DailyExpense, report.Previous.DailyExpense },
            new object?[] { "Moneda", report.BaseCurrency, report.BaseCurrency },
            Array.Empty<object?>(),
            new object?[] { "Categoría", "Gasto", "% del gasto", "Periodo anterior", "Variación (%)" }
        };
        rows.AddRange(report.Categories.Select(category => new object?[] { category.Name, category.Amount, category.Share, category.PreviousAmount, category.Change }));
        rows.Add([]);
        rows.Add(new object?[] { "Comercio", "Compras", "Gasto" });
        rows.AddRange(report.Merchants.Select(merchant => new object?[] { merchant.Name, merchant.Count, merchant.Amount }));
        return rows;
    }

    /// <summary>UTF-8 with its mark, so Excel opens the accents right; commas between cells and a point for decimals.</summary>
    private static byte[] Csv(IEnumerable<IReadOnlyList<object?>> rows)
    {
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            text.AppendJoin(',', row.Select(Cell)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    private static string Cell(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            decimal amount => amount.ToString("0.00", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        // A cell that starts like a formula is kept as text, so a description cannot run anything in a spreadsheet.
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' && value is string)
        {
            text = "'" + text;
        }

        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }
}

/// <param name="SignedAmount">In the currency of the account; negative when the money left it.</param>
public sealed record SearchMovementResponse(
    Guid Id, LedgerMovementKind Kind, DateOnly OccurredOn, string Description, decimal SignedAmount, string Currency, string AccountName, string? CategoryName);

/// <param name="Balance">In the currency of the account; negative when it is owed.</param>
public sealed record SearchAccountResponse(Guid Id, string Name, LedgerAccountType Type, string Currency, decimal Balance);

public sealed record SearchGoalResponse(Guid Id, string Name, decimal Saved, decimal TargetAmount, string Currency, decimal Percent, bool IsShared);

/// <param name="Movements">The newest matches first.</param>
public sealed record SearchResponse(IReadOnlyList<SearchMovementResponse> Movements, IReadOnlyList<SearchAccountResponse> Accounts, IReadOnlyList<SearchGoalResponse> Goals);

public sealed record SearchQuery(string Text) : IQuery<SearchResponse>;

/// <summary>Finds the person's own movements, accounts and goals by a piece of their text. Nobody else's ever comes up.</summary>
internal sealed class SearchHandler(ILedgerQueries ledger, IGoalQueries goals, ICategoryLookup categories) : IQueryHandler<SearchQuery, SearchResponse>
{
    public const int MinLength = 2;
    public const int MaxLength = 80;
    private const int Movements = 8;
    private const int Accounts = 5;
    private const int Goals = 5;

    public async Task<Result<SearchResponse>> HandleAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var text = query.Text.Trim();
        if (text.Length is < MinLength or > MaxLength)
        {
            return Error.Validation("reporting.search_invalid", $"Write between {MinLength} and {MaxLength} characters.");
        }

        var movements = await ledger.SearchMovementsAsync(text, Movements, cancellationToken);
        var names = await categories.GetAsync(movements.Where(movement => movement.CategoryId != null).Select(movement => movement.CategoryId!.Value).Distinct().ToList(), cancellationToken);
        var accounts = await ledger.SearchAccountsAsync(text, Accounts, cancellationToken);
        var open = await goals.GetActiveAsync(100, cancellationToken);
        return new SearchResponse(
            movements.Select(movement => new SearchMovementResponse(
                movement.Id, movement.Kind, movement.OccurredOn, movement.Description, movement.SignedAmount, movement.Currency, movement.AccountName,
                movement.CategoryId is { } id && names.TryGetValue(id, out var info) ? info.Name : null)).ToList(),
            accounts.Where(account => account.Type != LedgerAccountType.GoalFund)
                .Select(account => new SearchAccountResponse(account.Id, account.Name, account.Type, account.Currency, account.Balance)).ToList(),
            open.Where(goal => goal.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)).Take(Goals)
                .Select(goal => new SearchGoalResponse(goal.Id, goal.Name, goal.Saved, goal.TargetAmount, goal.Currency, goal.Percent, goal.IsShared)).ToList());
    }
}
