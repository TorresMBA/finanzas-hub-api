using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Events;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Goals.Application.Abstractions;
using PersonalFinance.Modules.Goals.Domain;
using PersonalFinance.Modules.Ledger.Contracts;
using PersonalFinance.Modules.Ledger.Contracts.Events;
using PersonalFinance.Modules.Notifications.Contracts;

namespace PersonalFinance.Modules.Goals.Application;

/// <summary>
/// Tells the other participants of a goal that it changed: a signal so their open pages reload, and a notice
/// when it is worth one. Nobody outside the goal hears anything.
/// </summary>
internal sealed class GoalNotifier(INotificationPublisher notifications, IRealtimeNotifier realtime)
{
    /// <summary>The name the browsers listen to on the hub. Only "this goal changed" travels; the page asks again over HTTP.</summary>
    public const string ChangedEvent = "resource.changed";

    public void Signal(Goal goal, Guid exceptUserId)
    {
        foreach (var participant in goal.Participants.Where(participant => participant.IsActive && participant.UserId != exceptUserId))
        {
            realtime.ToUser(participant.UserId, ChangedEvent, new { Resource = "goal", goal.Id });
        }
    }

    public async Task NotifyAsync(Goal goal, IEnumerable<Guid> userIds, NotificationSeverity severity, string title, string body, string? key, CancellationToken cancellationToken)
    {
        foreach (var userId in userIds)
        {
            await notifications.PublishAsync(
                new NotificationRequest(userId, goal.WorkspaceId, NotificationCategory.Goals, severity, title, body, "/goals", key), cancellationToken);
        }
    }

    public static string Money(decimal amount, string currency) => $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";
}

/// <param name="Currency">That of the workspace when left out.</param>
/// <param name="ParticipantUserIds">Other members of the workspace who save for it too. The creator is always its owner.</param>
public sealed record CreateGoalCommand(string Name, decimal TargetAmount, string? Currency, DateOnly? TargetDate, IReadOnlyList<Guid>? ParticipantUserIds)
    : ICommand<GoalResponse>;

internal sealed class CreateGoalHandler(IGoalsDb db, GoalReader reader, GoalNotifier notifier, IAuditLog audit, IClock clock)
    : ICommandHandler<CreateGoalCommand, GoalResponse>
{
    public async Task<Result<GoalResponse>> HandleAsync(CreateGoalCommand command, CancellationToken cancellationToken)
    {
        if (reader.Owner is not (var workspaceId, var userId))
        {
            return GoalErrors.Unauthenticated;
        }

        var currency = (command.Currency ?? reader.BaseCurrency)?.Trim().ToUpperInvariant();
        if (currency is not { Length: 3 })
        {
            return Error.Validation("goals.currency_invalid", "State the currency of the goal with its three-letter code.");
        }

        var created = Goal.Create(workspaceId, userId, command.Name, command.TargetAmount, currency, command.TargetDate, clock.UtcNow);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var goal = created.Value;
        var names = await reader.MemberNamesAsync(cancellationToken);
        var others = (command.ParticipantUserIds ?? []).Where(id => id != userId).Distinct().ToList();
        foreach (var other in others)
        {
            // Only someone who belongs to the workspace can be brought into one of its goals.
            if (!names.ContainsKey(other))
            {
                return Error.BusinessRule("goals.participant.not_a_member", "Only members of the workspace can take part in a goal.");
            }

            goal.AddParticipant(userId, other, clock.UtcNow);
        }

        db.Goals.Add(goal);
        audit.Record(new AuditEntry("goals", "goals.goal.created", "goal", goal.Id)
        {
            ScopeType = "goal", ScopeId = goal.Id, After = new { goal.Name, goal.TargetAmount, goal.Currency, goal.TargetDate, Participants = others }
        });
        await notifier.NotifyAsync(
            goal, others, NotificationSeverity.Info, $"Te sumaron a la meta «{goal.Name}»",
            $"{GoalReader.NameOf(names, userId)} te añadió. Objetivo: {GoalNotifier.Money(goal.TargetAmount, goal.Currency)}.", $"goal-joined:{goal.Id}", cancellationToken);
        return reader.Describe(goal, names);
    }
}

public sealed record UpdateGoalCommand(Guid GoalId, string Name, decimal TargetAmount, DateOnly? TargetDate) : ICommand<GoalResponse>;

internal sealed class UpdateGoalHandler(GoalReader reader, GoalNotifier notifier, IAuditLog audit) : ICommandHandler<UpdateGoalCommand, GoalResponse>
{
    public async Task<Result<GoalResponse>> HandleAsync(UpdateGoalCommand command, CancellationToken cancellationToken)
    {
        if (reader.Owner is not var (_, userId) || await reader.FindAsync(command.GoalId, cancellationToken) is not { } goal)
        {
            return GoalErrors.NotFound;
        }

        var before = new { goal.Name, goal.TargetAmount, goal.TargetDate };
        var updated = goal.Update(userId, command.Name, command.TargetAmount, command.TargetDate);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        audit.Record(new AuditEntry("goals", "goals.goal.updated", "goal", goal.Id)
        {
            ScopeType = "goal", ScopeId = goal.Id, Before = before, After = new { goal.Name, goal.TargetAmount, goal.TargetDate }
        });
        notifier.Signal(goal, userId);
        return await reader.DescribeAsync(goal, cancellationToken);
    }
}

public sealed record AddGoalParticipantCommand(Guid GoalId, Guid UserId) : ICommand<GoalResponse>;

internal sealed class AddGoalParticipantHandler(GoalReader reader, GoalNotifier notifier, IAuditLog audit, IClock clock)
    : ICommandHandler<AddGoalParticipantCommand, GoalResponse>
{
    public async Task<Result<GoalResponse>> HandleAsync(AddGoalParticipantCommand command, CancellationToken cancellationToken)
    {
        if (reader.Owner is not var (_, userId) || await reader.FindAsync(command.GoalId, cancellationToken) is not { } goal)
        {
            return GoalErrors.NotFound;
        }

        var names = await reader.MemberNamesAsync(cancellationToken);
        if (!names.ContainsKey(command.UserId))
        {
            return Error.BusinessRule("goals.participant.not_a_member", "Only members of the workspace can take part in a goal.");
        }

        var added = goal.AddParticipant(userId, command.UserId, clock.UtcNow);
        if (added.IsFailure)
        {
            return added.Error;
        }

        audit.Record(new AuditEntry("goals", "goals.participant.added", "goal", goal.Id) { ScopeType = "goal", ScopeId = goal.Id, After = new { command.UserId } });
        notifier.Signal(goal, userId);
        await notifier.NotifyAsync(
            goal, [command.UserId], NotificationSeverity.Info, $"Te sumaron a la meta «{goal.Name}»",
            $"{GoalReader.NameOf(names, userId)} te añadió. Objetivo: {GoalNotifier.Money(goal.TargetAmount, goal.Currency)}.", null, cancellationToken);
        return reader.Describe(goal, names);
    }
}

/// <summary>Takes a participant out of the goal: the owner removes someone, or someone leaves by themselves.</summary>
public sealed record RemoveGoalParticipantCommand(Guid GoalId, Guid UserId) : ICommand<Unit>;

internal sealed class RemoveGoalParticipantHandler(GoalReader reader, GoalNotifier notifier, IAuditLog audit) : ICommandHandler<RemoveGoalParticipantCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RemoveGoalParticipantCommand command, CancellationToken cancellationToken)
    {
        if (reader.Owner is not var (_, userId) || await reader.FindAsync(command.GoalId, cancellationToken) is not { } goal)
        {
            return GoalErrors.NotFound;
        }

        var removed = goal.RemoveParticipant(userId, command.UserId);
        if (removed.IsFailure)
        {
            return removed.Error;
        }

        audit.Record(new AuditEntry("goals", "goals.participant.removed", "goal", goal.Id) { ScopeType = "goal", ScopeId = goal.Id, After = new { command.UserId } });
        notifier.Signal(goal, userId);
        return Unit.Value;
    }
}

/// <summary>
/// Puts money of the current person into the goal: a transfer from one of their accounts to their own fund for
/// this goal, and the contribution that records it, in one transaction. It is neither an expense nor a gift:
/// the money is still theirs.
/// </summary>
/// <param name="OccurredOn">Today when left out.</param>
public sealed record ContributeToGoalCommand(Guid GoalId, decimal Amount, Guid FromAccountId, DateOnly? OccurredOn, string IdempotencyKey)
    : ICommand<GoalResponse>, IIdempotentCommand;

internal sealed class ContributeToGoalHandler(IGoalsDb db, GoalReader reader, ILedgerPosting ledger, GoalNotifier notifier, IAuditLog audit, IClock clock)
    : ICommandHandler<ContributeToGoalCommand, GoalResponse>
{
    public async Task<Result<GoalResponse>> HandleAsync(ContributeToGoalCommand command, CancellationToken cancellationToken)
    {
        if (reader.Owner is not var (_, userId) || await reader.FindAsync(command.GoalId, cancellationToken) is not { } goal)
        {
            return GoalErrors.NotFound;
        }

        var allowed = goal.CanContribute(userId, command.Amount);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var participant = allowed.Value;
        if (participant.FundAccountId is null)
        {
            var opened = await ledger.OpenGoalFundAsync(goal.Name, goal.Currency, cancellationToken);
            if (opened.IsFailure)
            {
                return opened.Error;
            }

            goal.SetFund(participant, opened.Value);
        }

        // The ledger only moves between accounts of the current person: an account of someone else is not found.
        var contributionId = Guid.CreateVersion7();
        var occurredOn = command.OccurredOn ?? reader.Today();
        var moved = await ledger.MoveGoalMoneyAsync(
            command.FromAccountId, participant.FundAccountId!.Value, command.Amount, occurredOn, $"Aporte a {goal.Name}", contributionId, cancellationToken);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        var reached = goal.Record(contributionId, userId, ContributionType.Contribution, command.Amount, occurredOn, moved.Value, clock.UtcNow);
        // A contribution is a row of its own, which leaves the goal untouched: two at once would each see room for
        // themselves and pass the target together. Touching the goal makes the second one conflict (409).
        db.Goals.Entry(goal).Property(saved => saved.Name).IsModified = true;
        audit.Record(new AuditEntry("goals", "goals.contribution.added", "goal", goal.Id)
        {
            ScopeType = "goal", ScopeId = goal.Id, After = new { ContributionId = contributionId, command.Amount, goal.Currency, occurredOn, OperationId = moved.Value }
        });

        var names = await reader.MemberNamesAsync(cancellationToken);
        var others = goal.Participants.Where(other => other.IsActive && other.UserId != userId).Select(other => other.UserId).ToList();
        notifier.Signal(goal, userId);
        await notifier.NotifyAsync(
            goal, others, NotificationSeverity.Info, $"{GoalReader.NameOf(names, userId)} aportó a «{goal.Name}»",
            $"{GoalNotifier.Money(command.Amount, goal.Currency)}. Van {GoalNotifier.Money(goal.Saved, goal.Currency)} de {GoalNotifier.Money(goal.TargetAmount, goal.Currency)}.",
            null, cancellationToken);
        if (reached)
        {
            // Said once in the life of the goal, to everyone in it.
            await notifier.NotifyAsync(
                goal, [.. others, userId], NotificationSeverity.Info, $"Meta alcanzada: «{goal.Name}»",
                $"Llegaron a {GoalNotifier.Money(goal.TargetAmount, goal.Currency)}.", $"goal-reached:{goal.Id}", cancellationToken);
        }

        return reader.Describe(goal, names);
    }
}

/// <summary>Takes out of the goal part of what the current person put in, back to one of their accounts.</summary>
public sealed record WithdrawFromGoalCommand(Guid GoalId, decimal Amount, Guid ToAccountId, DateOnly? OccurredOn, string IdempotencyKey)
    : ICommand<GoalResponse>, IIdempotentCommand;

internal sealed class WithdrawFromGoalHandler(GoalReader reader, ILedgerPosting ledger, GoalNotifier notifier, IAuditLog audit, IClock clock)
    : ICommandHandler<WithdrawFromGoalCommand, GoalResponse>
{
    public async Task<Result<GoalResponse>> HandleAsync(WithdrawFromGoalCommand command, CancellationToken cancellationToken)
    {
        if (reader.Owner is not var (_, userId) || await reader.FindAsync(command.GoalId, cancellationToken) is not { } goal)
        {
            return GoalErrors.NotFound;
        }

        var allowed = goal.CanWithdraw(userId, command.Amount);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        // Having something to withdraw means having contributed, so the fund exists.
        var contributionId = Guid.CreateVersion7();
        var occurredOn = command.OccurredOn ?? reader.Today();
        var moved = await ledger.MoveGoalMoneyAsync(
            allowed.Value.FundAccountId!.Value, command.ToAccountId, command.Amount, occurredOn, $"Retiro de {goal.Name}", contributionId, cancellationToken);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        goal.Record(contributionId, userId, ContributionType.Withdrawal, command.Amount, occurredOn, moved.Value, clock.UtcNow);
        audit.Record(new AuditEntry("goals", "goals.contribution.withdrawn", "goal", goal.Id)
        {
            ScopeType = "goal", ScopeId = goal.Id, After = new { ContributionId = contributionId, command.Amount, goal.Currency, occurredOn, OperationId = moved.Value }
        });
        notifier.Signal(goal, userId);
        return await reader.DescribeAsync(goal, cancellationToken);
    }
}

/// <summary>
/// Voids a contribution of the current person made by mistake, with its transfer: the money is back in the account
/// it came from as if it had never left. Voiding it again changes nothing.
/// </summary>
public sealed record VoidGoalContributionCommand(Guid GoalId, Guid ContributionId) : ICommand<GoalResponse>;

internal sealed class VoidGoalContributionHandler(GoalReader reader, ILedgerPosting ledger, GoalNotifier notifier, IAuditLog audit, IClock clock)
    : ICommandHandler<VoidGoalContributionCommand, GoalResponse>
{
    public async Task<Result<GoalResponse>> HandleAsync(VoidGoalContributionCommand command, CancellationToken cancellationToken)
    {
        if (reader.Owner is not var (_, userId) || await reader.FindAsync(command.GoalId, cancellationToken) is not { } goal)
        {
            return GoalErrors.NotFound;
        }

        var alreadyVoided = goal.Contributions.Any(contribution => contribution.Id == command.ContributionId && contribution.IsVoided);
        var voided = goal.VoidContribution(userId, command.ContributionId, clock.UtcNow);
        if (voided.IsFailure)
        {
            return voided.Error;
        }

        if (!alreadyVoided)
        {
            var contribution = voided.Value;
            var undone = await ledger.VoidGoalMoveAsync(contribution.LedgerOperationId, $"Aporte anulado en {goal.Name}", cancellationToken);
            if (undone.IsFailure)
            {
                return undone.Error;
            }

            audit.Record(new AuditEntry("goals", "goals.contribution.voided", "goal", goal.Id)
            {
                ScopeType = "goal", ScopeId = goal.Id, Before = new { ContributionId = contribution.Id, contribution.Amount, goal.Currency, OperationId = contribution.LedgerOperationId }
            });
            notifier.Signal(goal, userId);
        }

        return await reader.DescribeAsync(goal, cancellationToken);
    }
}

/// <param name="Description">What was bought; the name of the goal when left out.</param>
public sealed record SpendFromGoalCommand(
    Guid GoalId, decimal Amount, Guid AccountId, Guid CategoryId, string? Description, DateOnly? OccurredOn, string IdempotencyKey)
    : ICommand<GoalResponse>, IIdempotentCommand;

/// <summary>
/// Spends what the current person saved for the goal, in one step: the money leaves the fund to the account that
/// pays (a transfer, as any withdrawal) and the expense is registered there. Putting money aside was not an expense;
/// spending it is, in the month it is spent, marked as paid with the goal.
/// </summary>
internal sealed class SpendFromGoalHandler(GoalReader reader, ILedgerPosting ledger, GoalNotifier notifier, IAuditLog audit, IClock clock)
    : ICommandHandler<SpendFromGoalCommand, GoalResponse>
{
    public async Task<Result<GoalResponse>> HandleAsync(SpendFromGoalCommand command, CancellationToken cancellationToken)
    {
        if (reader.Owner is not var (_, userId) || await reader.FindAsync(command.GoalId, cancellationToken) is not { } goal)
        {
            return GoalErrors.NotFound;
        }

        var allowed = goal.CanWithdraw(userId, command.Amount);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var contributionId = Guid.CreateVersion7();
        var occurredOn = command.OccurredOn ?? reader.Today();
        var description = string.IsNullOrWhiteSpace(command.Description) ? goal.Name : command.Description.Trim();
        var moved = await ledger.MoveGoalMoneyAsync(
            allowed.Value.FundAccountId!.Value, command.AccountId, command.Amount, occurredOn, $"Retiro de {goal.Name}", contributionId, cancellationToken);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        var spent = await ledger.PostTransactionAsync(
            new LedgerTransactionRequest(
                true, command.AccountId, command.Amount, command.CategoryId, occurredOn, description, $"goals:spend:{command.IdempotencyKey}", goal.Id),
            cancellationToken);
        if (spent.IsFailure)
        {
            return spent.Error;
        }

        goal.Record(contributionId, userId, ContributionType.Withdrawal, command.Amount, occurredOn, moved.Value, clock.UtcNow);
        goal.LinkExpense(contributionId, spent.Value);
        audit.Record(new AuditEntry("goals", "goals.contribution.spent", "goal", goal.Id)
        {
            ScopeType = "goal",
            ScopeId = goal.Id,
            After = new { ContributionId = contributionId, command.Amount, goal.Currency, occurredOn, OperationId = moved.Value, ExpenseId = spent.Value, command.CategoryId }
        });
        notifier.Signal(goal, userId);
        return await reader.DescribeAsync(goal, cancellationToken);
    }
}

/// <param name="Cancel">True to give the goal up; false to archive one that is done.</param>
public sealed record CloseGoalCommand(Guid GoalId, bool Cancel) : ICommand<GoalResponse>;

internal sealed class CloseGoalHandler(GoalReader reader, ILedgerPosting ledger, GoalNotifier notifier, IAuditLog audit, IClock clock)
    : ICommandHandler<CloseGoalCommand, GoalResponse>
{
    public async Task<Result<GoalResponse>> HandleAsync(CloseGoalCommand command, CancellationToken cancellationToken)
    {
        if (reader.Owner is not var (_, userId) || await reader.FindAsync(command.GoalId, cancellationToken) is not { } goal)
        {
            return GoalErrors.NotFound;
        }

        // Read before closing: archiving moves nothing, so the plan is only carried out when cancelling.
        var plan = command.Cancel ? goal.RefundPlan() : [];
        var closed = goal.Close(userId, command.Cancel ? GoalStatus.Cancelled : GoalStatus.Archived);
        if (closed.IsFailure)
        {
            return closed.Error;
        }

        var kept = await GiveBackAsync(goal, plan, cancellationToken);
        if (kept.IsFailure)
        {
            return kept.Error;
        }

        audit.Record(new AuditEntry("goals", "goals.goal.closed", "goal", goal.Id)
        {
            ScopeType = "goal",
            ScopeId = goal.Id,
            After = new { Status = goal.Status.ToString(), Returned = plan.Select(item => new { item.From.UserId, item.Amount, ContributionId = item.From.Id }) }
        });
        notifier.Signal(goal, userId);
        var others = goal.Participants.Where(other => other.IsActive && other.UserId != userId).Select(other => other.UserId).ToList();
        foreach (var other in others)
        {
            await notifier.NotifyAsync(
                goal, [other], NotificationSeverity.Info, $"La meta «{goal.Name}» se {(command.Cancel ? "canceló" : "archivó")}",
                NoticeBody(command.Cancel, kept.Value.Contains(other)), $"goal-closed:{goal.Id}", cancellationToken);
        }

        return await reader.DescribeAsync(goal, cancellationToken);
    }

    /// <summary>
    /// Giving up the goal gives the money back: what each participant still had in it returns to the accounts it
    /// came from, in their own ledger. What came from an account they archived stays in their fund.
    /// </summary>
    /// <returns>Who kept part of their money in the fund.</returns>
    private async Task<Result<HashSet<Guid>>> GiveBackAsync(Goal goal, IReadOnlyList<(GoalContribution From, decimal Amount)> plan, CancellationToken cancellationToken)
    {
        var kept = new HashSet<Guid>();
        var today = reader.Today();
        foreach (var (from, amount) in plan)
        {
            var returnId = Guid.CreateVersion7();
            var returned = await ledger.ReturnGoalMoneyAsync(
                from.LedgerOperationId, from.UserId, amount, today, $"Devolución de {goal.Name}", returnId, cancellationToken);
            if (returned.IsFailure)
            {
                return returned.Error;
            }

            if (returned.Value is { } operationId)
            {
                goal.Record(returnId, from.UserId, ContributionType.Withdrawal, amount, today, operationId, clock.UtcNow);
            }
            else
            {
                kept.Add(from.UserId);
            }
        }

        return kept;
    }

    private static string NoticeBody(bool cancelled, bool keptSome)
    {
        if (!cancelled)
        {
            return "Lo que aportaste sigue en tu fondo: puedes retirarlo desde la meta.";
        }

        return keptSome
            ? "Lo que aportaste volvió a tus cuentas, salvo lo que vino de una cuenta archivada: eso sigue en tu fondo y puedes retirarlo desde la meta."
            : "Lo que aportaste volvió a las cuentas de donde salió.";
    }
}

/// <summary>
/// The expense paid with a goal was voided in the ledger: it was never spent, so the money goes back to the goal
/// it came from and what is missing to reach the target grows back with it.
/// </summary>
internal sealed class ReturnSpentMoneyToGoal(IGoalsDb db, ILedgerPosting ledger, GoalNotifier notifier, IAuditLog audit, IClock clock)
    : IDomainEventHandler<LedgerOperationChanged>
{
    public async Task HandleAsync(LedgerOperationChanged domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.Change != LedgerOperationChange.Voided || domainEvent.FundedByGoalId is not { } goalId)
        {
            return;
        }

        // The person voids it from Transacciones whatever workspace they have open, and the filter of goals is that of
        // the open one. The goal is found by the expense and by its owner, who is who spent from it.
        var goal = await db.Goals.IgnoreQueryFilters().SingleOrDefaultAsync(
            candidate => candidate.Id == goalId
                         && candidate.Contributions.Any(contribution =>
                             contribution.ExpenseOperationId == domainEvent.OperationId && contribution.UserId == domainEvent.OwnerUserId),
            cancellationToken);
        if (goal?.RestoreSpent(domainEvent.OwnerUserId, domainEvent.OperationId, clock.UtcNow) is not { } withdrawal)
        {
            return;
        }

        // A failure here undoes the void of the expense too: the two never part.
        var undone = await ledger.VoidGoalMoveAsync(withdrawal.LedgerOperationId, $"Gasto anulado: vuelve a {goal.Name}", cancellationToken);
        if (undone.IsFailure)
        {
            throw new InvalidOperationException($"The withdrawal {withdrawal.Id} of goal {goal.Id} could not be voided: {undone.Error.Code}.");
        }

        audit.Record(new AuditEntry("goals", "goals.contribution.restored", "goal", goal.Id)
        {
            ScopeType = "goal", ScopeId = goal.Id, After = new { ContributionId = withdrawal.Id, withdrawal.Amount, goal.Currency, ExpenseId = domainEvent.OperationId }
        });
        notifier.Signal(goal, domainEvent.OwnerUserId);
    }
}
