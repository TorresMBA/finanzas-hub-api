using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.Transactions;

/// <param name="Amount">Positive, in the currency of the account.</param>
/// <param name="Tags">Names of tags. A name that is not a tag yet becomes one.</param>
/// <param name="Pending">Registered but still to be reviewed. It counts in balances all the same.</param>
public sealed record PostTransactionCommand(
    TransactionType Type, Guid AccountId, decimal Amount, Guid CategoryId, DateOnly OccurredOn, string Description, string? Merchant, string? Note,
    IReadOnlyList<string>? Tags, bool Pending, string IdempotencyKey)
    : ICommand<TransactionResponse>, IIdempotentCommand;

internal sealed class PostTransactionHandler(ILedgerDb db, TransactionReader reader, IAuditLog audit, IClock clock)
    : ICommandHandler<PostTransactionCommand, TransactionResponse>
{
    public async Task<Result<TransactionResponse>> HandleAsync(PostTransactionCommand command, CancellationToken cancellationToken)
    {
        // The owner filter makes an account of someone else indistinguishable from one that does not exist.
        if (await db.Accounts.SingleOrDefaultAsync(account => account.Id == command.AccountId, cancellationToken) is not { } account)
        {
            return LedgerErrors.AccountNotFound;
        }

        var category = await reader.ValidateCategoryAsync(command.Type, command.CategoryId, null, cancellationToken);
        if (category.IsFailure)
        {
            return category.Error;
        }

        var posted = Operation.PostTransaction(
            command.Type.ToKind(), account, command.Amount, command.CategoryId, command.OccurredOn, command.Description, command.Merchant, command.Note,
            command.Pending, clock.UtcNow);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        var operation = posted.Value;
        var tagged = await reader.SetTagsAsync(operation, command.Tags, cancellationToken);
        if (tagged.IsFailure)
        {
            return tagged.Error;
        }

        db.Operations.Add(operation);
        LedgerMetrics.OperationsPosted.Add(1, new KeyValuePair<string, object?>("kind", operation.Kind.ToString()));
        audit.Record(new AuditEntry("ledger", "ledger.transaction.posted", "operation", operation.Id)
        {
            OwnerUserId = operation.OwnerUserId,
            After = Snapshot.Of(operation)
        });
        return await reader.DescribeAsync(operation, cancellationToken);
    }
}

/// <summary>Replaces what a transaction says. Amount, account and date can change too; the audit trail keeps both versions.</summary>
public sealed record EditTransactionCommand(
    Guid TransactionId, TransactionType Type, Guid AccountId, decimal Amount, Guid CategoryId, DateOnly OccurredOn, string Description, string? Merchant,
    string? Note, IReadOnlyList<string>? Tags)
    : ICommand<TransactionResponse>;

internal sealed class EditTransactionHandler(ILedgerDb db, TransactionReader reader, IAuditLog audit) : ICommandHandler<EditTransactionCommand, TransactionResponse>
{
    public async Task<Result<TransactionResponse>> HandleAsync(EditTransactionCommand command, CancellationToken cancellationToken)
    {
        if (await db.FindOperationAsync(command.TransactionId, cancellationToken) is not { } operation)
        {
            return LedgerErrors.TransactionNotFound;
        }

        if (await db.Accounts.SingleOrDefaultAsync(account => account.Id == command.AccountId, cancellationToken) is not { } account)
        {
            return LedgerErrors.AccountNotFound;
        }

        var category = await reader.ValidateCategoryAsync(command.Type, command.CategoryId, operation.CategoryId, cancellationToken);
        if (category.IsFailure)
        {
            return category.Error;
        }

        var before = Snapshot.Of(operation);
        var edited = operation.Edit(
            command.Type.ToKind(), account, command.Amount, command.CategoryId, command.OccurredOn, command.Description, command.Merchant, command.Note);
        if (edited.IsFailure)
        {
            return edited.Error;
        }

        var tagged = await reader.SetTagsAsync(operation, command.Tags, cancellationToken);
        if (tagged.IsFailure)
        {
            return tagged.Error;
        }

        audit.Record(new AuditEntry("ledger", "ledger.transaction.edited", "operation", operation.Id)
        {
            OwnerUserId = operation.OwnerUserId,
            Before = before,
            After = Snapshot.Of(operation)
        });
        return await reader.DescribeAsync(operation, cancellationToken);
    }
}

public sealed record ConfirmTransactionCommand(Guid TransactionId) : ICommand<TransactionResponse>;

internal sealed class ConfirmTransactionHandler(ILedgerDb db, TransactionReader reader, IAuditLog audit) : ICommandHandler<ConfirmTransactionCommand, TransactionResponse>
{
    public async Task<Result<TransactionResponse>> HandleAsync(ConfirmTransactionCommand command, CancellationToken cancellationToken)
    {
        if (await db.FindOperationAsync(command.TransactionId, cancellationToken) is not { } operation)
        {
            return LedgerErrors.TransactionNotFound;
        }

        var confirmed = operation.Confirm();
        if (confirmed.IsFailure)
        {
            return confirmed.Error;
        }

        audit.Record(new AuditEntry("ledger", "ledger.transaction.confirmed", "operation", operation.Id) { OwnerUserId = operation.OwnerUserId });
        return await reader.DescribeAsync(operation, cancellationToken);
    }
}

/// <summary>Takes the transaction out of every sum. It stays in the list of voided ones, with who and why.</summary>
public sealed record VoidTransactionCommand(Guid TransactionId, string? Reason) : ICommand<TransactionResponse>;

internal sealed class VoidTransactionHandler(ILedgerDb db, TransactionReader reader, IAuditLog audit, IClock clock) : ICommandHandler<VoidTransactionCommand, TransactionResponse>
{
    public async Task<Result<TransactionResponse>> HandleAsync(VoidTransactionCommand command, CancellationToken cancellationToken)
    {
        if (await db.FindOperationAsync(command.TransactionId, cancellationToken) is not { } operation)
        {
            return LedgerErrors.TransactionNotFound;
        }

        var wasEffective = operation.IsEffective;
        var before = Snapshot.Of(operation);
        var voided = operation.Void(command.Reason, clock.UtcNow);
        if (voided.IsFailure)
        {
            return voided.Error;
        }

        if (wasEffective)
        {
            audit.Record(new AuditEntry("ledger", "ledger.transaction.voided", "operation", operation.Id)
            {
                OwnerUserId = operation.OwnerUserId,
                Before = before,
                After = new { Status = operation.Status.ToString(), operation.VoidReason }
            });
        }

        return await reader.DescribeAsync(operation, cancellationToken);
    }
}

public sealed record GetTransactionQuery(Guid TransactionId) : IQuery<TransactionResponse>;

internal sealed class GetTransactionHandler(ILedgerDb db, TransactionReader reader) : IQueryHandler<GetTransactionQuery, TransactionResponse>
{
    public async Task<Result<TransactionResponse>> HandleAsync(GetTransactionQuery query, CancellationToken cancellationToken) =>
        await db.Operations.AsNoTracking().Include(operation => operation.Entries).SingleOrDefaultAsync(operation => operation.Id == query.TransactionId, cancellationToken)
            is { } operation
            ? await reader.DescribeAsync(operation, cancellationToken)
            : LedgerErrors.TransactionNotFound;
}

internal static class Snapshot
{
    /// <summary>What the audit trail keeps of an operation: every field a person can change.</summary>
    public static object Of(Operation operation)
    {
        var entry = operation.Entries.First();
        return new
        {
            Kind = operation.Kind.ToString(),
            Status = operation.Status.ToString(),
            entry.Amount,
            entry.Currency,
            entry.AccountId,
            operation.OccurredOn,
            operation.CategoryId,
            operation.Description,
            operation.Merchant,
            operation.Note,
            Tags = operation.Tags.Select(tag => tag.TagId).ToList()
        };
    }
}

internal static class TransactionMapping
{
    public static OperationKind ToKind(this TransactionType type) => type == TransactionType.Income ? OperationKind.Income : OperationKind.Expense;

    public static Task<Operation?> FindOperationAsync(this ILedgerDb db, Guid operationId, CancellationToken cancellationToken) =>
        db.Operations.Include(operation => operation.Entries).SingleOrDefaultAsync(operation => operation.Id == operationId, cancellationToken);
}
