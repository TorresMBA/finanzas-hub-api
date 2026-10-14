using System.Globalization;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Ledger.Contracts;
using PersonalFinance.Modules.Notifications.Contracts;
using PersonalFinance.Modules.SharedExpenses.Application.Abstractions;
using PersonalFinance.Modules.SharedExpenses.Domain;

namespace PersonalFinance.Modules.SharedExpenses.Application;

/// <summary>
/// Tells the other members of a group that it changed: a signal so their open pages reload, and a notice to
/// whom it concerns. Nobody outside the group hears anything.
/// </summary>
internal sealed class SharedNotifier(INotificationPublisher notifications, IRealtimeNotifier realtime)
{
    /// <summary>The name the browsers listen to on the hub. Only "this group changed" travels; the page asks again over HTTP.</summary>
    public const string ChangedEvent = "resource.changed";

    public void Signal(SharedGroup group, Guid exceptUserId)
    {
        foreach (var member in group.Members.Where(member => member.IsActive && member.UserId != exceptUserId))
        {
            realtime.ToUser(member.UserId, ChangedEvent, new { Resource = "shared-group", group.Id });
        }
    }

    public Task NotifyAsync(SharedGroup group, Guid userId, string title, string body, string? key, CancellationToken cancellationToken) =>
        notifications.PublishAsync(
            new NotificationRequest(userId, group.WorkspaceId, NotificationCategory.Shared, NotificationSeverity.Info, title, body, "/shared", key), cancellationToken);

    public static string Money(decimal amount, string currency) => $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";
}

/// <param name="Currency">That of the workspace when left out.</param>
/// <param name="MemberUserIds">Other members of the workspace to split with. The creator is always in.</param>
public sealed record CreateSharedGroupCommand(string Name, string? Currency, IReadOnlyList<Guid>? MemberUserIds) : ICommand<SharedGroupDetailResponse>;

internal sealed class CreateSharedGroupHandler(ISharedExpensesDb db, SharedGroupReader reader, SharedNotifier notifier, IAuditLog audit, IClock clock)
    : ICommandHandler<CreateSharedGroupCommand, SharedGroupDetailResponse>
{
    public async Task<Result<SharedGroupDetailResponse>> HandleAsync(CreateSharedGroupCommand command, CancellationToken cancellationToken)
    {
        if (reader.UserId is not { } userId || reader.WorkspaceId is not { } workspaceId)
        {
            return SharedErrors.NotFound;
        }

        var currency = (command.Currency ?? reader.BaseCurrency)?.Trim().ToUpperInvariant();
        if (currency is not { Length: 3 })
        {
            return Error.Validation("shared.group.currency_invalid", "State the currency of the group with its three-letter code.");
        }

        var created = SharedGroup.Create(workspaceId, userId, command.Name, currency, clock.UtcNow);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var group = created.Value;
        var names = await reader.MemberNamesAsync(cancellationToken);
        var others = (command.MemberUserIds ?? []).Where(id => id != userId).Distinct().ToList();
        foreach (var other in others)
        {
            // Only someone who belongs to the workspace can be brought into one of its groups.
            if (!names.ContainsKey(other))
            {
                return Error.BusinessRule("shared.member.not_a_workspace_member", "Only members of the workspace can be in a group.");
            }

            group.AddMember(userId, other, clock.UtcNow);
        }

        db.SharedGroups.Add(group);
        audit.Record(new AuditEntry("shared-expenses", "shared.group.created", "shared_group", group.Id)
        {
            ScopeType = "shared-group", ScopeId = group.Id, After = new { group.Name, group.Currency, Members = others }
        });
        foreach (var other in others)
        {
            await notifier.NotifyAsync(
                group, other, $"Te sumaron al grupo «{group.Name}»", $"{SharedGroupReader.NameOf(names, userId)} te añadió para dividir gastos.",
                $"shared-joined:{group.Id}", cancellationToken);
        }

        return await reader.DescribeAsync(group, cancellationToken);
    }
}

public enum GroupMembershipChange
{
    Add,
    Remove
}

/// <summary>Brings a member of the workspace into the group, or takes someone out: who created it removes, anyone else leaves by naming themselves.</summary>
public sealed record ChangeSharedGroupMemberCommand(Guid GroupId, Guid UserId, GroupMembershipChange Change) : ICommand<SharedGroupDetailResponse>;

internal sealed class ChangeSharedGroupMemberHandler(SharedGroupReader reader, SharedNotifier notifier, IAuditLog audit, IClock clock)
    : ICommandHandler<ChangeSharedGroupMemberCommand, SharedGroupDetailResponse>
{
    public async Task<Result<SharedGroupDetailResponse>> HandleAsync(ChangeSharedGroupMemberCommand command, CancellationToken cancellationToken)
    {
        if (reader.UserId is not { } userId || await reader.FindAsync(command.GroupId, cancellationToken) is not { } group)
        {
            return SharedErrors.NotFound;
        }

        var names = await reader.MemberNamesAsync(cancellationToken);
        if (command.Change == GroupMembershipChange.Add && !names.ContainsKey(command.UserId))
        {
            return Error.BusinessRule("shared.member.not_a_workspace_member", "Only members of the workspace can be in a group.");
        }

        var changed = command.Change == GroupMembershipChange.Add
            ? group.AddMember(userId, command.UserId, clock.UtcNow)
            : group.RemoveMember(userId, command.UserId);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        audit.Record(new AuditEntry("shared-expenses", $"shared.member.{(command.Change == GroupMembershipChange.Add ? "added" : "removed")}", "shared_group", group.Id)
        {
            ScopeType = "shared-group", ScopeId = group.Id, After = new { command.UserId }
        });
        notifier.Signal(group, userId);
        if (command.Change == GroupMembershipChange.Add)
        {
            await notifier.NotifyAsync(
                group, command.UserId, $"Te sumaron al grupo «{group.Name}»", $"{SharedGroupReader.NameOf(names, userId)} te añadió para dividir gastos.", null,
                cancellationToken);
        }

        return await reader.DescribeAsync(group, cancellationToken);
    }
}

public sealed record ArchiveSharedGroupCommand(Guid GroupId) : ICommand<SharedGroupDetailResponse>;

internal sealed class ArchiveSharedGroupHandler(SharedGroupReader reader, SharedNotifier notifier, IAuditLog audit)
    : ICommandHandler<ArchiveSharedGroupCommand, SharedGroupDetailResponse>
{
    public async Task<Result<SharedGroupDetailResponse>> HandleAsync(ArchiveSharedGroupCommand command, CancellationToken cancellationToken)
    {
        if (reader.UserId is not { } userId || await reader.FindAsync(command.GroupId, cancellationToken) is not { } group)
        {
            return SharedErrors.NotFound;
        }

        var archived = group.Archive(userId);
        if (archived.IsFailure)
        {
            return archived.Error;
        }

        audit.Record(new AuditEntry("shared-expenses", "shared.group.archived", "shared_group", group.Id) { ScopeType = "shared-group", ScopeId = group.Id });
        notifier.Signal(group, userId);
        return await reader.DescribeAsync(group, cancellationToken);
    }
}

/// <summary>
/// Registers what one member paid for several. When who registers it is who paid and says from which account
/// and under which category, the whole expense also goes into their own ledger, in the same transaction.
/// </summary>
/// <param name="OccurredOn">Today when left out.</param>
/// <param name="AccountId">An account of who paid. Only they can give it.</param>
public sealed record CreateSharedExpenseCommand(
    Guid GroupId, string Description, decimal TotalAmount, Guid PaidByUserId, DateOnly? OccurredOn, SplitType SplitType, IReadOnlyList<SplitInput> Participants,
    Guid? AccountId, Guid? CategoryId, string IdempotencyKey) : ICommand<SharedGroupDetailResponse>, IIdempotentCommand;

internal sealed class CreateSharedExpenseHandler(SharedGroupReader reader, ILedgerPosting ledger, SharedNotifier notifier, IAuditLog audit, IClock clock)
    : ICommandHandler<CreateSharedExpenseCommand, SharedGroupDetailResponse>
{
    public async Task<Result<SharedGroupDetailResponse>> HandleAsync(CreateSharedExpenseCommand command, CancellationToken cancellationToken)
    {
        if (reader.UserId is not { } userId || await reader.FindAsync(command.GroupId, cancellationToken) is not { } group)
        {
            return SharedErrors.NotFound;
        }

        var occurredOn = command.OccurredOn ?? reader.Today();
        var added = group.AddExpense(
            userId, command.Description, command.TotalAmount, command.PaidByUserId, occurredOn, command.SplitType, command.Participants, clock.UtcNow);
        if (added.IsFailure)
        {
            return added.Error;
        }

        var expense = added.Value;
        if (command.AccountId is { } accountId)
        {
            // Nobody writes in the ledger of someone else: the account can only be given by who paid.
            if (command.PaidByUserId != userId || command.CategoryId is not { } categoryId)
            {
                return Error.BusinessRule(
                    "shared.expense.account_of_payer_only", "Only who paid can register the expense in one of their accounts, and it needs a category.");
            }

            var posted = await ledger.PostTransactionAsync(
                new LedgerTransactionRequest(true, accountId, expense.TotalAmount, categoryId, occurredOn, expense.Description, $"shared:expense:{expense.Id}"),
                cancellationToken);
            if (posted.IsFailure)
            {
                return posted.Error;
            }

            group.LinkPayerOperation(expense, posted.Value, categoryId);
        }

        audit.Record(new AuditEntry("shared-expenses", "shared.expense.created", "shared_group", group.Id)
        {
            ScopeType = "shared-group",
            ScopeId = group.Id,
            After = new
            {
                ExpenseId = expense.Id, expense.Description, expense.TotalAmount, group.Currency, expense.PaidByUserId, SplitType = expense.SplitType.ToString(),
                Shares = expense.Shares.Select(share => new { share.UserId, share.Amount })
            }
        });

        var names = await reader.MemberNamesAsync(cancellationToken);
        notifier.Signal(group, userId);
        foreach (var share in expense.Shares.Where(share => share.UserId != userId && share.UserId != expense.PaidByUserId))
        {
            await notifier.NotifyAsync(
                group, share.UserId, $"{SharedGroupReader.NameOf(names, expense.PaidByUserId)} pagó «{expense.Description}»",
                $"Tu parte es {SharedNotifier.Money(share.Amount, group.Currency)} de {SharedNotifier.Money(expense.TotalAmount, group.Currency)} en «{group.Name}».",
                null, cancellationToken);
        }

        return await reader.DescribeAsync(group, cancellationToken);
    }
}

public sealed record CancelSharedExpenseCommand(Guid GroupId, Guid ExpenseId) : ICommand<SharedGroupDetailResponse>;

internal sealed class CancelSharedExpenseHandler(SharedGroupReader reader, ILedgerPosting ledger, SharedNotifier notifier, IAuditLog audit, IClock clock)
    : ICommandHandler<CancelSharedExpenseCommand, SharedGroupDetailResponse>
{
    public async Task<Result<SharedGroupDetailResponse>> HandleAsync(CancelSharedExpenseCommand command, CancellationToken cancellationToken)
    {
        if (reader.UserId is not { } userId || await reader.FindAsync(command.GroupId, cancellationToken) is not { } group)
        {
            return SharedErrors.NotFound;
        }

        var cancelled = group.CancelExpense(userId, command.ExpenseId, clock.UtcNow);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        // The expense of who paid is in their own ledger. When it is they who cancel, it goes too; when it is someone
        // else, nobody touches the ledger of another person: who paid is told.
        var expense = cancelled.Value;
        if (expense.PayerOperationId is { } operationId)
        {
            if (expense.PaidByUserId == userId)
            {
                var voided = await ledger.VoidAsync(operationId, $"Gasto compartido cancelado en {group.Name}", cancellationToken);
                if (voided.IsFailure)
                {
                    return voided.Error;
                }
            }
            else
            {
                await notifier.NotifyAsync(
                    group, expense.PaidByUserId, $"Se canceló «{expense.Description}»",
                    "Lo habías registrado en una de tus cuentas: anula ese movimiento en Transacciones.", null, cancellationToken);
            }
        }

        audit.Record(new AuditEntry("shared-expenses", "shared.expense.cancelled", "shared_group", group.Id)
        {
            ScopeType = "shared-group", ScopeId = group.Id, After = new { command.ExpenseId }
        });
        notifier.Signal(group, userId);
        return await reader.DescribeAsync(group, cancellationToken);
    }
}

/// <summary>
/// One member repays another. Either of the two registers it. With an account of their own, it also goes into
/// their ledger: an expense for who pays, a refund for who is paid, so being repaid is never an income.
/// </summary>
/// <param name="FromUserId">Who pays: the one who owed.</param>
/// <param name="CategoryId">For who pays, the category of their expense. For who is paid, left out to use the one the expense was registered under.</param>
public sealed record RecordSettlementCommand(
    Guid GroupId, Guid FromUserId, Guid ToUserId, decimal Amount, DateOnly? OccurredOn, Guid? AccountId, Guid? CategoryId, string IdempotencyKey)
    : ICommand<SharedGroupDetailResponse>, IIdempotentCommand;

internal sealed class RecordSettlementHandler(
    SharedGroupReader reader, SettlementLedger settlementLedger, SharedNotifier notifier, IAuditLog audit, IClock clock)
    : ICommandHandler<RecordSettlementCommand, SharedGroupDetailResponse>
{
    public async Task<Result<SharedGroupDetailResponse>> HandleAsync(RecordSettlementCommand command, CancellationToken cancellationToken)
    {
        if (reader.UserId is not { } userId || await reader.FindAsync(command.GroupId, cancellationToken) is not { } group)
        {
            return SharedErrors.NotFound;
        }

        var recorded = group.RecordSettlement(userId, command.FromUserId, command.ToUserId, command.Amount, command.OccurredOn ?? reader.Today(), clock.UtcNow);
        if (recorded.IsFailure)
        {
            return recorded.Error;
        }

        var settlement = recorded.Value;
        if (command.AccountId is { } accountId)
        {
            var linked = await settlementLedger.PostAsync(group, settlement, userId, accountId, command.CategoryId, cancellationToken);
            if (linked.IsFailure)
            {
                return linked.Error;
            }
        }

        audit.Record(new AuditEntry("shared-expenses", "shared.settlement.recorded", "shared_group", group.Id)
        {
            ScopeType = "shared-group",
            ScopeId = group.Id,
            After = new { SettlementId = settlement.Id, settlement.FromUserId, settlement.ToUserId, settlement.Amount, group.Currency, settlement.OccurredOn }
        });

        var names = await reader.MemberNamesAsync(cancellationToken);
        var other = userId == settlement.FromUserId ? settlement.ToUserId : settlement.FromUserId;
        notifier.Signal(group, userId);
        await notifier.NotifyAsync(
            group, other,
            userId == settlement.FromUserId
                ? $"{SharedGroupReader.NameOf(names, userId)} te pagó {SharedNotifier.Money(settlement.Amount, group.Currency)}"
                : $"{SharedGroupReader.NameOf(names, userId)} registró tu pago de {SharedNotifier.Money(settlement.Amount, group.Currency)}",
            $"En «{group.Name}». Puedes registrarlo también en una de tus cuentas.", null, cancellationToken);
        return await reader.DescribeAsync(group, cancellationToken);
    }
}

/// <summary>
/// Puts a payment in the ledger of one of its two sides, the current person. Who pays registers an expense;
/// who is paid, a refund against the category the expense was registered under. Each side does it once.
/// </summary>
internal sealed class SettlementLedger(ILedgerPosting ledger)
{
    public async Task<Result> PostAsync(SharedGroup group, Settlement settlement, Guid userId, Guid accountId, Guid? categoryId, CancellationToken cancellationToken)
    {
        Result<Guid> posted;
        if (userId == settlement.FromUserId)
        {
            if (categoryId is not { } expenseCategory)
            {
                return Error.Validation("shared.settlement.category_required", "Choose the category of this expense.");
            }

            posted = await ledger.PostTransactionAsync(
                new LedgerTransactionRequest(
                    true, accountId, settlement.Amount, expenseCategory, settlement.OccurredOn, $"Pago en {group.Name}", $"shared:settlement:{settlement.Id}:from"),
                cancellationToken);
        }
        else
        {
            if ((categoryId ?? group.CategoryRepaidBy(settlement)) is not { } refundCategory)
            {
                return Error.Validation("shared.settlement.category_required", "Choose the category the expense was registered under.");
            }

            posted = await ledger.PostRefundAsync(accountId, settlement.Amount, refundCategory, settlement.OccurredOn, $"Reembolso en {group.Name}", cancellationToken);
        }

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        group.LinkOperation(settlement, userId, posted.Value);
        return Result.Success();
    }
}

/// <summary>The other side of a payment, or the same one later, registers it in one of their accounts.</summary>
public sealed record LinkSettlementAccountCommand(Guid GroupId, Guid SettlementId, Guid AccountId, Guid? CategoryId, string IdempotencyKey)
    : ICommand<SharedGroupDetailResponse>, IIdempotentCommand;

internal sealed class LinkSettlementAccountHandler(SharedGroupReader reader, SettlementLedger settlementLedger, IAuditLog audit)
    : ICommandHandler<LinkSettlementAccountCommand, SharedGroupDetailResponse>
{
    public async Task<Result<SharedGroupDetailResponse>> HandleAsync(LinkSettlementAccountCommand command, CancellationToken cancellationToken)
    {
        if (reader.UserId is not { } userId || await reader.FindAsync(command.GroupId, cancellationToken) is not { } group)
        {
            return SharedErrors.NotFound;
        }

        var settlement = group.SettlementToLink(userId, command.SettlementId);
        if (settlement.IsFailure)
        {
            return settlement.Error;
        }

        var linked = await settlementLedger.PostAsync(group, settlement.Value, userId, command.AccountId, command.CategoryId, cancellationToken);
        if (linked.IsFailure)
        {
            return linked.Error;
        }

        audit.Record(new AuditEntry("shared-expenses", "shared.settlement.linked", "shared_group", group.Id)
        {
            ScopeType = "shared-group", ScopeId = group.Id, After = new { command.SettlementId }
        });
        return await reader.DescribeAsync(group, cancellationToken);
    }
}

public sealed record VoidSettlementCommand(Guid GroupId, Guid SettlementId) : ICommand<SharedGroupDetailResponse>;

internal sealed class VoidSettlementHandler(SharedGroupReader reader, ILedgerPosting ledger, SharedNotifier notifier, IAuditLog audit, IClock clock)
    : ICommandHandler<VoidSettlementCommand, SharedGroupDetailResponse>
{
    public async Task<Result<SharedGroupDetailResponse>> HandleAsync(VoidSettlementCommand command, CancellationToken cancellationToken)
    {
        if (reader.UserId is not { } userId || await reader.FindAsync(command.GroupId, cancellationToken) is not { } group)
        {
            return SharedErrors.NotFound;
        }

        var voided = group.VoidSettlement(userId, command.SettlementId, clock.UtcNow);
        if (voided.IsFailure)
        {
            return voided.Error;
        }

        // The movement the person had put in their account for this payment goes with it, so paying again does not
        // leave two. The other side's movement, if they registered one, is theirs to void: they are told.
        var settlement = voided.Value;
        var (mine, theirs, other) = userId == settlement.FromUserId
            ? (settlement.FromOperationId, settlement.ToOperationId, settlement.ToUserId)
            : (settlement.ToOperationId, settlement.FromOperationId, settlement.FromUserId);
        if (mine is { } operationId)
        {
            var undone = await ledger.VoidAsync(operationId, $"Pago anulado en {group.Name}", cancellationToken);
            if (undone.IsFailure)
            {
                return undone.Error;
            }
        }

        var names = await reader.MemberNamesAsync(cancellationToken);
        await notifier.NotifyAsync(
            group, other, $"{SharedGroupReader.NameOf(names, userId)} anuló un pago de {SharedNotifier.Money(settlement.Amount, group.Currency)}",
            theirs is null
                ? $"En «{group.Name}». Lo que cubría se vuelve a deber."
                : $"En «{group.Name}». Lo habías registrado en una de tus cuentas: anula ese movimiento en Transacciones.",
            null, cancellationToken);

        audit.Record(new AuditEntry("shared-expenses", "shared.settlement.voided", "shared_group", group.Id)
        {
            ScopeType = "shared-group", ScopeId = group.Id, After = new { command.SettlementId }
        });
        notifier.Signal(group, userId);
        return await reader.DescribeAsync(group, cancellationToken);
    }
}

/// <summary>Who is owed reminds who owes. Once a day per pair, however many times it is asked.</summary>
public sealed record RemindSharedMemberCommand(Guid GroupId, Guid UserId) : ICommand<Unit>;

internal sealed class RemindSharedMemberHandler(SharedGroupReader reader, SharedNotifier notifier) : ICommandHandler<RemindSharedMemberCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RemindSharedMemberCommand command, CancellationToken cancellationToken)
    {
        if (reader.UserId is not { } userId || await reader.FindAsync(command.GroupId, cancellationToken) is not { } group)
        {
            return SharedErrors.NotFound;
        }

        var owed = group.Owed(command.UserId, userId);
        if (owed <= 0)
        {
            return Error.BusinessRule("shared.reminder.nothing_owed", "That person owes you nothing in this group.");
        }

        var names = await reader.MemberNamesAsync(cancellationToken);
        await notifier.NotifyAsync(
            group, command.UserId, $"{SharedGroupReader.NameOf(names, userId)} te recuerda un pago",
            $"Le debes {SharedNotifier.Money(owed, group.Currency)} en «{group.Name}».",
            $"shared-reminder:{group.Id}:{userId}:{command.UserId}:{reader.Today():yyyy-MM-dd}", cancellationToken);
        return Unit.Value;
    }
}
