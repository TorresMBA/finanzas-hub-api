using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Ledger.Contracts;
using PersonalFinance.Modules.Scheduling.Application.Abstractions;
using PersonalFinance.Modules.Scheduling.Domain;

namespace PersonalFinance.Modules.Scheduling.Application;

/// <summary>
/// Turns a date of a rule into a movement of the ledger and leaves it written on the rule. The key it gives the
/// ledger is made of the rule and the date, so the same date is never registered twice, whoever asks and
/// however many times.
/// </summary>
internal sealed class OccurrencePoster(ILedgerPosting posting, IClock clock)
{
    // Same meter name as the platform's, which is the one the hosts export.
    private static readonly Counter<long> Occurrences =
        new Meter("PersonalFinance").CreateCounter<long>("scheduler.occurrences", description: "Dates of recurring rules settled, by outcome.");

    public async Task<Result<Occurrence>> PostAsync(RecurringRule rule, DateOnly date, decimal amount, CancellationToken cancellationToken)
    {
        if (!rule.IsScheduledOn(date))
        {
            return Error.NotFound("scheduling.occurrence.not_found", "The rule has nothing on that date.");
        }

        if (rule.OccurrenceOn(date) is not null)
        {
            return Error.Conflict("scheduling.occurrence.already_recorded", "That date was already registered or skipped.");
        }

        var posted = await posting.PostTransactionAsync(
            new LedgerTransactionRequest(
                rule.Type == RuleType.Expense, rule.AccountId, amount, rule.CategoryId, date, rule.Description, $"scheduling:{rule.Id}:{date:yyyy-MM-dd}"),
            cancellationToken);
        Occurrences.Add(1, new KeyValuePair<string, object?>("outcome", posted.IsSuccess ? "posted" : "failed"));
        return posted.IsFailure ? posted.Error : rule.Record(date, OccurrenceStatus.Posted, posted.Value, amount, clock.UtcNow);
    }
}

/// <param name="EndsOn">The last day a date can fall on. Null: until it is ended.</param>
/// <param name="ReminderDaysBefore">0, 1, 3 or 7. Null: no reminder.</param>
public sealed record CreateRecurringRuleCommand(
    RuleType Type, Guid AccountId, Guid CategoryId, decimal Amount, string Description, RuleFrequency Frequency, DateOnly StartsOn, DateOnly? EndsOn,
    PostingMode PostingMode, int? ReminderDaysBefore) : ICommand<RecurringRuleResponse>;

internal sealed class CreateRecurringRuleHandler(
    ISchedulingDb db, ScheduleReader reader, ILedgerQueries ledger, ICategoryLookup categories, IAuditLog audit, IClock clock)
    : ICommandHandler<CreateRecurringRuleCommand, RecurringRuleResponse>
{
    public async Task<Result<RecurringRuleResponse>> HandleAsync(CreateRecurringRuleCommand command, CancellationToken cancellationToken)
    {
        if (reader.Owner is not (var workspaceId, var userId))
        {
            return SchedulingErrors.Unauthenticated;
        }

        // Both are asked to their modules, which only know those of the current person: an account or a category of someone else is not found.
        if ((await ledger.GetAccountBalancesAsync(cancellationToken)).FirstOrDefault(candidate => candidate.Id == command.AccountId) is not { } account)
        {
            return Error.NotFound("scheduling.account_not_found", "The account was not found.");
        }

        var category = await categories.FindAsync(command.CategoryId, cancellationToken);
        if (category is null || category.IsArchived || category.Use != (command.Type == RuleType.Income ? CategoryUse.Income : CategoryUse.Expense))
        {
            return Error.BusinessRule("scheduling.category_invalid", "Choose a category in use of the same kind as the movement.");
        }

        var created = RecurringRule.Create(
            workspaceId, userId, command.Type, account.Id, account.Currency, command.CategoryId, command.Amount, command.Description, command.Frequency,
            command.StartsOn, command.EndsOn, command.PostingMode, command.ReminderDaysBefore, clock.UtcNow);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var rule = created.Value;
        db.RecurringRules.Add(rule);
        audit.Record(new AuditEntry("scheduling", "scheduling.rule.created", "recurring_rule", rule.Id)
        {
            OwnerUserId = userId,
            After = new
            {
                Type = rule.Type.ToString(), rule.AccountId, rule.CategoryId, rule.Amount, rule.Currency, Frequency = rule.Frequency.ToString(), rule.StartsOn,
                rule.EndsOn, PostingMode = rule.PostingMode.ToString()
            }
        });
        return (await reader.DescribeAsync([rule], cancellationToken))[0];
    }
}

public enum RuleStatusChange
{
    Pause,
    Resume,
    End
}

public sealed record ChangeRuleStatusCommand(Guid RuleId, RuleStatusChange Change) : ICommand<RecurringRuleResponse>;

internal sealed class ChangeRuleStatusHandler(ISchedulingDb db, ScheduleReader reader, IAuditLog audit) : ICommandHandler<ChangeRuleStatusCommand, RecurringRuleResponse>
{
    public async Task<Result<RecurringRuleResponse>> HandleAsync(ChangeRuleStatusCommand command, CancellationToken cancellationToken)
    {
        if (await db.RecurringRules.SingleOrDefaultAsync(candidate => candidate.Id == command.RuleId, cancellationToken) is not { } rule)
        {
            return SchedulingErrors.RuleNotFound;
        }

        var before = rule.Status.ToString();
        var changed = command.Change switch
        {
            RuleStatusChange.Pause => rule.Pause(),
            RuleStatusChange.Resume => rule.Resume(),
            _ => rule.End(reader.Today())
        };
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        audit.Record(new AuditEntry("scheduling", "scheduling.rule.status_changed", "recurring_rule", rule.Id)
        {
            OwnerUserId = rule.OwnerUserId,
            Before = new { Status = before },
            After = new { Status = rule.Status.ToString(), rule.EndsOn }
        });
        return (await reader.DescribeAsync([rule], cancellationToken))[0];
    }
}

/// <summary>Registers in the ledger a date of a rule that is due. The amount can be the real one when it differs from the planned.</summary>
public sealed record ConfirmOccurrenceCommand(Guid RuleId, DateOnly Date, decimal? Amount) : ICommand<Unit>, IIdempotentCommand
{
    // The date of a rule is settled once: asking again answers what the first time answered.
    public string IdempotencyKey => $"scheduling:confirm:{RuleId}:{Date:yyyy-MM-dd}";
}

internal sealed class ConfirmOccurrenceHandler(ISchedulingDb db, ScheduleReader reader, OccurrencePoster poster, IAuditLog audit)
    : ICommandHandler<ConfirmOccurrenceCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ConfirmOccurrenceCommand command, CancellationToken cancellationToken)
    {
        if (await db.RecurringRules.SingleOrDefaultAsync(candidate => candidate.Id == command.RuleId, cancellationToken) is not { } rule)
        {
            return SchedulingErrors.RuleNotFound;
        }

        if (command.Date > reader.Today())
        {
            return Error.BusinessRule("scheduling.occurrence.in_future", "A date still to come cannot be registered yet.");
        }

        var posted = await poster.PostAsync(rule, command.Date, command.Amount ?? rule.Amount, cancellationToken);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        audit.Record(new AuditEntry("scheduling", "scheduling.occurrence.confirmed", "recurring_rule", rule.Id)
        {
            OwnerUserId = rule.OwnerUserId,
            After = new { command.Date, posted.Value.Amount, posted.Value.OperationId }
        });
        return Unit.Value;
    }
}

/// <summary>Leaves a date of a rule without a movement: this time it did not happen.</summary>
public sealed record SkipOccurrenceCommand(Guid RuleId, DateOnly Date) : ICommand<Unit>;

internal sealed class SkipOccurrenceHandler(ISchedulingDb db, IAuditLog audit, IClock clock) : ICommandHandler<SkipOccurrenceCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SkipOccurrenceCommand command, CancellationToken cancellationToken)
    {
        if (await db.RecurringRules.SingleOrDefaultAsync(candidate => candidate.Id == command.RuleId, cancellationToken) is not { } rule)
        {
            return SchedulingErrors.RuleNotFound;
        }

        var skipped = rule.Record(command.Date, OccurrenceStatus.Skipped, null, 0m, clock.UtcNow);
        if (skipped.IsFailure)
        {
            return skipped.Error;
        }

        audit.Record(new AuditEntry("scheduling", "scheduling.occurrence.skipped", "recurring_rule", rule.Id) { OwnerUserId = rule.OwnerUserId, After = new { command.Date } });
        return Unit.Value;
    }
}
