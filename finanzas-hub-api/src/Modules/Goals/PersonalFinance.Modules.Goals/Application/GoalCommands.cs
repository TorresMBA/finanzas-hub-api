using System.Globalization;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Goals.Application.Abstractions;
using PersonalFinance.Modules.Goals.Domain;
using PersonalFinance.Modules.Ledger.Contracts;
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

internal sealed class ContributeToGoalHandler(GoalReader reader, ILedgerPosting ledger, GoalNotifier notifier, IAuditLog audit, IClock clock)
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

internal sealed class CloseGoalHandler(GoalReader reader, GoalNotifier notifier, IAuditLog audit) : ICommandHandler<CloseGoalCommand, GoalResponse>
{
    public async Task<Result<GoalResponse>> HandleAsync(CloseGoalCommand command, CancellationToken cancellationToken)
    {
        if (reader.Owner is not var (_, userId) || await reader.FindAsync(command.GoalId, cancellationToken) is not { } goal)
        {
            return GoalErrors.NotFound;
        }

        var closed = goal.Close(userId, command.Cancel ? GoalStatus.Cancelled : GoalStatus.Archived);
        if (closed.IsFailure)
        {
            return closed.Error;
        }

        audit.Record(new AuditEntry("goals", "goals.goal.closed", "goal", goal.Id) { ScopeType = "goal", ScopeId = goal.Id, After = new { Status = goal.Status.ToString() } });
        notifier.Signal(goal, userId);
        var others = goal.Participants.Where(other => other.IsActive && other.UserId != userId).Select(other => other.UserId).ToList();
        await notifier.NotifyAsync(
            goal, others, NotificationSeverity.Info, $"La meta «{goal.Name}» se {(command.Cancel ? "canceló" : "archivó")}",
            "Lo que aportaste sigue en tu fondo: puedes retirarlo desde la meta.", $"goal-closed:{goal.Id}", cancellationToken);
        return await reader.DescribeAsync(goal, cancellationToken);
    }
}
