using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Ledger.Contracts;
using PersonalFinance.Modules.Scheduling.Application.Abstractions;
using PersonalFinance.Modules.Scheduling.Contracts;
using PersonalFinance.Modules.Scheduling.Domain;

namespace PersonalFinance.Modules.Scheduling.Application;

internal static class SchedulingErrors
{
    public static readonly Error Unauthenticated = Error.Unauthorized("auth.unauthenticated", "Authentication is required.");

    public static readonly Error RuleNotFound = Error.NotFound("scheduling.rule.not_found", "The rule was not found.");
}

public enum CalendarItemKind
{
    Income,
    Expense,
    Transfer,

    /// <summary>The day the payment of a credit card is due; with what it owes when it is the next one.</summary>
    CardDue,

    /// <summary>The day the statement of a credit card closes. A date, never an amount.</summary>
    CardClosing
}

public enum CalendarItemStatus
{
    /// <summary>Already in the ledger.</summary>
    Confirmed,

    /// <summary>Expected on a day still to come.</summary>
    Scheduled,

    /// <summary>Its day arrived and it is neither registered nor skipped.</summary>
    Pending
}

/// <summary>One thing on one day of the calendar. Nothing here is stored for the calendar: it is read or calculated each time.</summary>
/// <param name="Id">Unique within an answer; not an id to send back.</param>
/// <param name="Amount">Positive, in <paramref name="Currency"/>; 0 for a day of a card that carries no amount (a closing, a payment that is not the next one, or a card that owes nothing).</param>
/// <param name="RuleId">Set when it comes from a rule; with the date, what confirm and skip need.</param>
public sealed record CalendarItemResponse(
    string Id, DateOnly Date, CalendarItemKind Kind, CalendarItemStatus Status, string Description, decimal Amount, string Currency, string AccountName, Guid? RuleId,
    bool IsRecurring);

/// <summary>A period of the calendar of one person. The amounts that add things up are in the base currency.</summary>
/// <param name="IncomeReceived">Income already registered in the period.</param>
/// <param name="PendingExpenses">Scheduled expenses not registered yet, plus what the credit cards owe on their due day.</param>
/// <param name="ProjectedBalance">What the accounts that are not credit cards hold today, plus the pending income, minus the pending payments. Null when today is outside the period.</param>
/// <param name="MissingRates">Currencies that could not be converted and are left out of the amounts.</param>
public sealed record CalendarResponse(
    DateOnly From, DateOnly To, DateOnly Today, string? BaseCurrency, decimal IncomeReceived, decimal PendingIncome, decimal PendingExpenses,
    decimal? ProjectedBalance, IReadOnlyList<CalendarItemResponse> Items, IReadOnlyList<string> MissingRates);

/// <param name="NextDate">The first date from today that is neither registered nor skipped.</param>
public sealed record RecurringRuleResponse(
    Guid Id, RuleType Type, string Description, decimal Amount, string Currency, Guid AccountId, string AccountName, Guid CategoryId, string CategoryName,
    RuleFrequency Frequency, DateOnly StartsOn, DateOnly? EndsOn, PostingMode PostingMode, int? ReminderDaysBefore, RuleStatus Status, string? PauseReason,
    DateOnly? NextDate);

/// <summary>
/// Reads the rules of the current person and works out their dates. The global filter of the context limits
/// every query to that person, so a calendar can only ever show their own plans.
/// </summary>
internal sealed class ScheduleReader(
    ISchedulingDb db, ILedgerQueries ledger, ICategoryLookup categories, ICurrentUser currentUser, IPersonalSpace workspace, IClock clock)
{
    public (Guid WorkspaceId, Guid UserId)? Owner =>
        currentUser.UserId is { } userId && workspace.WorkspaceId is { } workspaceId ? (workspaceId, userId) : null;

    public string? BaseCurrency => workspace.BaseCurrency;

    /// <summary>Today in the workspace's time zone: a payment is due on the person's day, not on the server's.</summary>
    public DateOnly Today()
    {
        var zone = workspace.TimeZone is { } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found) ? found : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
    }

    public async Task<List<RecurringRule>> RulesAsync(CancellationToken cancellationToken) =>
        await db.RecurringRules.AsNoTracking().OrderBy(rule => rule.CreatedAt).ToListAsync(cancellationToken);

    /// <summary>
    /// The dates of the rules in a period that are neither registered nor skipped. A paused rule expects nothing
    /// in the future, but what was already due stays for the person to settle.
    /// </summary>
    public static IEnumerable<(RecurringRule Rule, DateOnly Date, CalendarItemStatus Status)> Project(
        IEnumerable<RecurringRule> rules, DateOnly from, DateOnly to, DateOnly today) =>
        rules.SelectMany(rule => rule.DatesBetween(from, to)
            .Where(date => rule.OccurrenceOn(date) is null && (date <= today || rule.Status == RuleStatus.Active))
            .Select(date => (rule, date, date > today ? CalendarItemStatus.Scheduled : CalendarItemStatus.Pending)));

    public async Task<IReadOnlyList<RecurringRuleResponse>> DescribeAsync(IReadOnlyList<RecurringRule> rules, CancellationToken cancellationToken)
    {
        var accounts = (await ledger.GetAccountBalancesAsync(cancellationToken)).ToDictionary(account => account.Id, account => account.Name);
        var names = await categories.GetAsync(rules.Select(rule => rule.CategoryId).Distinct().ToList(), cancellationToken);
        var today = Today();
        return rules
            .Select(rule => new RecurringRuleResponse(
                rule.Id, rule.Type, rule.Description, rule.Amount, rule.Currency, rule.AccountId, accounts.GetValueOrDefault(rule.AccountId, string.Empty),
                rule.CategoryId, names.GetValueOrDefault(rule.CategoryId)?.Name ?? string.Empty, rule.Frequency, rule.StartsOn, rule.EndsOn, rule.PostingMode,
                rule.ReminderDaysBefore, rule.Status, rule.PauseReason, rule.Status == RuleStatus.Active ? rule.NextPendingFrom(today) : null))
            .ToList();
    }
}

public sealed record ListRecurringRulesQuery : IQuery<IReadOnlyList<RecurringRuleResponse>>;

internal sealed class ListRecurringRulesHandler(ScheduleReader reader) : IQueryHandler<ListRecurringRulesQuery, IReadOnlyList<RecurringRuleResponse>>
{
    public async Task<Result<IReadOnlyList<RecurringRuleResponse>>> HandleAsync(ListRecurringRulesQuery query, CancellationToken cancellationToken) =>
        Result.Success(await reader.DescribeAsync(await reader.RulesAsync(cancellationToken), cancellationToken));
}

public sealed record GetCalendarQuery(DateOnly From, DateOnly To) : IQuery<CalendarResponse>;

internal sealed class GetCalendarHandler(ScheduleReader reader, ILedgerQueries ledger) : IQueryHandler<GetCalendarQuery, CalendarResponse>
{
    private const int MaxDays = 92;
    private const int MaxMovements = 500;

    public async Task<Result<CalendarResponse>> HandleAsync(GetCalendarQuery query, CancellationToken cancellationToken)
    {
        var (from, to) = (query.From, query.To);
        if (from > to || to.DayNumber - from.DayNumber >= MaxDays)
        {
            return Error.Validation("scheduling.period_invalid", $"The period must go forward and cover {MaxDays} days at most.");
        }

        var today = reader.Today();
        var rules = await reader.RulesAsync(cancellationToken);
        var fromRules = rules.SelectMany(rule => rule.Occurrences).Where(occurrence => occurrence.OperationId != null).Select(occurrence => occurrence.OperationId!.Value).ToHashSet();
        var accounts = await ledger.GetAccountBalancesAsync(cancellationToken);
        var accountNames = accounts.ToDictionary(account => account.Id, account => account.Name);

        // What already happened comes from the ledger, the only place where it is written.
        var items = (await ledger.GetRecentMovementsAsync(from, to, MaxMovements, cancellationToken))
            .Select(movement => new CalendarItemResponse(
                $"op:{movement.Id}", movement.OccurredOn,
                movement.Kind switch
                {
                    LedgerMovementKind.Income => CalendarItemKind.Income,
                    LedgerMovementKind.Expense => CalendarItemKind.Expense,
                    _ => CalendarItemKind.Transfer
                },
                CalendarItemStatus.Confirmed, movement.Description, Math.Abs(movement.SignedAmount), movement.Currency, movement.AccountName, null,
                fromRules.Contains(movement.Id)))
            .ToList();

        var pending = new BaseAmounts(ledger);
        foreach (var (rule, date, status) in ScheduleReader.Project(rules, from, to, today))
        {
            items.Add(new CalendarItemResponse(
                $"rule:{rule.Id}:{date:yyyy-MM-dd}", date, rule.Type == RuleType.Income ? CalendarItemKind.Income : CalendarItemKind.Expense, status,
                rule.Description, rule.Amount, rule.Currency, accountNames.GetValueOrDefault(rule.AccountId, string.Empty), rule.Id,
                rule.Frequency != RuleFrequency.Once));
            await pending.AddAsync(rule.Type == RuleType.Income, rule.Currency, rule.Amount, cancellationToken);
        }

        // The cycle of every card, owing or not and in any month: when it closes and when it is due. Only the next payment
        // carries an amount, what the card owes now, and only that one counts as an expense still to come.
        foreach (var card in await ledger.GetCardCalendarAsync(from, to, today, cancellationToken))
        {
            var status = card.Date < today ? CalendarItemStatus.Confirmed : CalendarItemStatus.Scheduled;
            items.Add(new CalendarItemResponse(
                $"card:{(card.IsClosing ? "closing" : "due")}:{card.AccountId}:{card.Date:yyyy-MM-dd}", card.Date,
                card.IsClosing ? CalendarItemKind.CardClosing : CalendarItemKind.CardDue, status,
                card.IsClosing ? $"Corte de {card.Name}" : $"Pago de {card.Name}", card.Debt ?? 0m, card.Currency, card.Name, null, true));
            if (card.Debt is { } debt)
            {
                await pending.AddAsync(false, card.Currency, debt, cancellationToken);
            }
        }

        var flow = await ledger.GetFlowAsync(from, to, cancellationToken);
        decimal? projected = null;
        if (today >= from && today <= to)
        {
            // Money that can be spent: what a credit card owes is already counted on its due day.
            var liquid = accounts
                .Where(account => account.Type != LedgerAccountType.CreditCard && account.IncludeInNetWorth)
                .Sum(account => account.BaseBalance ?? 0m);
            projected = liquid + pending.Income - pending.Expense;
        }

        return new CalendarResponse(
            from, to, today, reader.BaseCurrency, flow.Months.Sum(month => month.Income), pending.Income, pending.Expense, projected,
            items.OrderBy(item => item.Date).ThenBy(item => item.Status).ThenBy(item => item.Description, StringComparer.InvariantCultureIgnoreCase).ToList(),
            flow.MissingRates.Union(pending.Missing).Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>Adds what is expected in and out, in the base currency. What cannot be converted is left out and its currency noted.</summary>
    private sealed class BaseAmounts(ILedgerQueries ledger)
    {
        public decimal Income { get; private set; }

        public decimal Expense { get; private set; }

        public SortedSet<string> Missing { get; } = new(StringComparer.Ordinal);

        public async Task AddAsync(bool isIncome, string currency, decimal amount, CancellationToken cancellationToken)
        {
            if (await ledger.ToBaseAsync(currency, amount, cancellationToken) is not { } inBase)
            {
                Missing.Add(currency);
            }
            else if (isIncome)
            {
                Income += inBase;
            }
            else
            {
                Expense += inBase;
            }
        }
    }
}

internal sealed class ScheduleQueries(ScheduleReader reader, ILedgerQueries ledger) : IScheduleQueries
{
    public async Task<IReadOnlyList<UpcomingPayment>> GetUpcomingAsync(DateOnly from, DateOnly to, int count, CancellationToken cancellationToken)
    {
        var today = reader.Today();
        var payments = ScheduleReader.Project(await reader.RulesAsync(cancellationToken), from, to, today)
            .Where(item => item.Rule.Type == RuleType.Expense)
            .Select(item => new UpcomingPayment(item.Date, item.Rule.Description, item.Rule.Amount, item.Rule.Currency, false))
            .ToList();
        payments.AddRange((await ledger.GetCardDueDatesAsync(today, cancellationToken))
            .Where(card => card.DueOn >= from && card.DueOn <= to)
            .Select(card => new UpcomingPayment(card.DueOn, $"Pago de {card.Name}", card.Debt, card.Currency, true)));
        return payments.OrderBy(payment => payment.Date).Take(count).ToList();
    }
}
