using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Ledger.Contracts;
using PersonalFinance.Modules.Notifications.Contracts;
using PersonalFinance.Modules.Scheduling.Application.Abstractions;
using PersonalFinance.Modules.Scheduling.Contracts;
using PersonalFinance.Modules.Scheduling.Domain;
using PersonalFinance.Modules.Workspaces.Contracts;

namespace PersonalFinance.Modules.Scheduling.Application;

/// <summary>A person in a workspace, with what the background process needs to act as them there.</summary>
public sealed record ScheduleOwner(Guid WorkspaceId, Guid UserId, WorkspaceRole Role, string? BaseCurrency, string TimeZone);

/// <summary>
/// Everyone the cycle has to run for: each person once, in their personal workspace, which is where their
/// scheduled movements and their accounts live.
/// </summary>
public sealed record ListScheduleOwnersQuery : IQuery<IReadOnlyList<ScheduleOwner>>;

internal sealed class ListScheduleOwnersHandler(IWorkspaceDirectory directory) : IQueryHandler<ListScheduleOwnersQuery, IReadOnlyList<ScheduleOwner>>
{
    public async Task<Result<IReadOnlyList<ScheduleOwner>>> HandleAsync(ListScheduleOwnersQuery query, CancellationToken cancellationToken) =>
        (await directory.ListActiveMembershipsAsync(cancellationToken))
        .Where(membership => membership.IsPersonal)
        .Select(membership => new ScheduleOwner(membership.WorkspaceId, membership.UserId, membership.Role, membership.BaseCurrency, membership.TimeZone))
        .ToList();
}

public sealed record RunScheduleResponse(int Posted, int Failed, int Notices);

/// <summary>
/// What is due today for the current person in the current workspace: registers the automatic rules that fall
/// today, reminds of what is coming and warns of a credit card about to fall due. It only looks at today, so a
/// day the process did not run is not made up for: those dates wait for the person to confirm or skip them.
/// Running it again the same day does nothing more.
/// </summary>
public sealed record RunScheduleCommand : ICommand<RunScheduleResponse>;

internal sealed class RunScheduleHandler(
    ISchedulingDb db, ScheduleReader reader, OccurrencePoster poster, ILedgerQueries ledger, INotificationPublisher notifications,
    IEnumerable<IScheduleCycleStep> steps)
    : ICommandHandler<RunScheduleCommand, RunScheduleResponse>
{
    /// <summary>How many days before a credit card falls due the person is told.</summary>
    private const int CardNoticeDays = 3;

    public async Task<Result<RunScheduleResponse>> HandleAsync(RunScheduleCommand command, CancellationToken cancellationToken)
    {
        if (reader.Owner is not (var workspaceId, var userId))
        {
            return SchedulingErrors.Unauthenticated;
        }

        var today = reader.Today();
        var (posted, failed, notices) = (0, 0, 0);
        var rules = await db.RecurringRules.Where(rule => rule.Status == RuleStatus.Active).ToListAsync(cancellationToken);
        foreach (var rule in rules)
        {
            if (rule.PostingMode == PostingMode.Automatic && rule.IsScheduledOn(today) && rule.OccurrenceOn(today) is null)
            {
                if ((await poster.PostAsync(rule, today, rule.Amount, cancellationToken)).IsSuccess)
                {
                    posted++;
                }
                else
                {
                    // An archived account or category: it would fail every time, so it stops until the person looks at it.
                    failed++;
                    rule.Pause($"No se pudo registrar el {Day(today)}. Revisa su cuenta y su categoría y reanúdala.");
                    notices += await NotifyAsync(
                        NotificationSeverity.Critical, $"No se pudo registrar «{rule.Description}»",
                        "La regla quedó en pausa. Revisa su cuenta y su categoría en el calendario.", "/calendar", $"rule-failed:{rule.Id}:{today:yyyy-MM-dd}");
                    continue;
                }
            }

            if (rule.ReminderDaysBefore is { } days && rule.NextPendingFrom(today) is { } next && next.AddDays(-days) <= today)
            {
                notices += await NotifyAsync(
                    NotificationSeverity.Info, $"{(rule.Type == RuleType.Expense ? "Pago" : "Ingreso")} programado: {rule.Description}",
                    $"{Money(rule.Amount, rule.Currency)} · {When(next, today)}.", "/calendar", $"reminder:{rule.Id}:{next:yyyy-MM-dd}");
            }
        }

        foreach (var card in (await ledger.GetCardDueDatesAsync(today, cancellationToken)).Where(card => card.DueOn <= today.AddDays(CardNoticeDays)))
        {
            notices += await NotifyAsync(
                NotificationSeverity.Warning, $"Tu tarjeta {card.Name} vence {When(card.DueOn, today)}", $"Debes {Money(card.Debt, card.Currency)}.", "/accounts",
                $"card-due:{card.AccountId}:{card.DueOn:yyyy-MM-dd}");
        }

        // What other modules watch day by day, as a term deposit about to mature.
        foreach (var step in steps)
        {
            notices += await step.RunAsync(today, cancellationToken);
        }

        return new RunScheduleResponse(posted, failed, notices);

        // The key makes each of these said once, however many times the cycle runs.
        async Task<int> NotifyAsync(NotificationSeverity severity, string title, string body, string link, string key) =>
            await notifications.PublishAsync(
                new NotificationRequest(userId, workspaceId, NotificationCategory.Bills, severity, title, body, link, key), cancellationToken)
                ? 1
                : 0;
    }

    private static string Money(decimal amount, string currency) => $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    private static string Day(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string When(DateOnly date, DateOnly today) =>
        date == today ? "hoy" : date == today.AddDays(1) ? "mañana" : $"el {Day(date)}";
}
