using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Application.ExchangeRates;
using PersonalFinance.Modules.Ledger.Application.Transactions;
using PersonalFinance.Modules.Ledger.Contracts.Events;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.Transfers;

/// <summary>Moves money between two accounts of the person. It is neither an income nor an expense, and net worth does not change.</summary>
/// <param name="Amount">What leaves the origin account, in its currency.</param>
/// <param name="DestinationAmount">What arrives, in the currency of the destination. Required when the two currencies differ.</param>
/// <param name="Fee">
/// What the bank charged apart, in the currency of the origin. It is an expense of the origin account in the category
/// "Comisiones", voided with the transfer. What the exchange itself cost is not a fee: it is already in the amounts.
/// </param>
public sealed record PostTransferCommand(
    Guid FromAccountId, Guid ToAccountId, decimal Amount, decimal? DestinationAmount, DateOnly OccurredOn, string? Description, string? Note,
    string IdempotencyKey, decimal? Fee = null)
    : ICommand<TransactionResponse>, IIdempotentCommand;

internal sealed class PostTransferHandler(TransferPoster poster) : ICommandHandler<PostTransferCommand, TransactionResponse>
{
    public Task<Result<TransactionResponse>> HandleAsync(PostTransferCommand command, CancellationToken cancellationToken) =>
        poster.PostAsync(
            command.FromAccountId, command.ToAccountId, command.Amount, command.DestinationAmount, command.OccurredOn, command.Description, command.Note,
            (_, _) => Result.Success(), cancellationToken, fee: command.Fee);
}

/// <summary>Pays a credit card from another account: a transfer whose destination is the card.</summary>
/// <param name="Amount">What the card is paid, in the currency of the card.</param>
/// <param name="SourceAmount">What leaves the paying account, in its currency. Required when it differs from the card's.</param>
public sealed record PayCreditCardCommand(
    Guid CardAccountId, Guid FromAccountId, decimal Amount, decimal? SourceAmount, DateOnly OccurredOn, string? Note, string IdempotencyKey)
    : ICommand<TransactionResponse>, IIdempotentCommand;

internal sealed class PayCreditCardHandler(TransferPoster poster) : ICommandHandler<PayCreditCardCommand, TransactionResponse>
{
    public Task<Result<TransactionResponse>> HandleAsync(PayCreditCardCommand command, CancellationToken cancellationToken) =>
        poster.PostAsync(
            command.FromAccountId, command.CardAccountId, command.SourceAmount ?? command.Amount, command.Amount, command.OccurredOn, "Pago de tarjeta",
            command.Note,
            (from, card) =>
            {
                if (!card.IsCreditCard)
                {
                    return Error.BusinessRule("ledger.card.not_a_card", "Only a credit card is paid this way.");
                }

                if (from.IsCreditCard)
                {
                    return Error.BusinessRule("ledger.card.paid_from_card", "A credit card is paid from an account that holds money, not from another card.");
                }

                return from.Currency != card.Currency && command.SourceAmount is null
                    ? Error.Validation("ledger.card.source_amount_required", "The paying account has another currency: state how much leaves it.")
                    : Result.Success();
            },
            cancellationToken);
}

/// <summary>What registering a transfer and paying a card have in common.</summary>
internal sealed class TransferPoster(ILedgerDb db, ExchangeRateBook rates, TransactionReader reader, ICategoryDirectory categories, IAuditLog audit, IClock clock)
{
    public async Task<Result<TransactionResponse>> PostAsync(
        Guid fromAccountId, Guid toAccountId, decimal amountOut, decimal? amountIn, DateOnly occurredOn, string? description, string? note,
        Func<Account, Account, Result> rule, CancellationToken cancellationToken, (string Module, string Type, Guid Id)? source = null, decimal? fee = null)
    {
        // The owner filter makes an account of someone else indistinguishable from one that does not exist: both must be the person's.
        var accounts = await db.Accounts.Where(account => account.Id == fromAccountId || account.Id == toAccountId).ToListAsync(cancellationToken);
        // An account opened earlier in this same command is not in the database yet: it is found among the tracked ones.
        var from = accounts.SingleOrDefault(account => account.Id == fromAccountId) ?? db.Accounts.Local.SingleOrDefault(account => account.Id == fromAccountId);
        var to = accounts.SingleOrDefault(account => account.Id == toAccountId) ?? db.Accounts.Local.SingleOrDefault(account => account.Id == toAccountId);
        if (from is null || to is null)
        {
            return LedgerErrors.AccountNotFound;
        }

        var allowed = rule(from, to);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var posted = Operation.PostTransfer(from, to, amountOut, amountIn, occurredOn, description, note, clock.UtcNow, byModule: source is not null);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        var operation = posted.Value;
        if (source is var (module, type, id))
        {
            operation.AttributeTo(module, type, id);
        }

        await rates.ValueAsync(operation, cancellationToken);
        db.Operations.Add(operation);
        LedgerMetrics.OperationsPosted.Add(1, new KeyValuePair<string, object?>("kind", nameof(OperationKind.Transfer)));
        audit.Record(new AuditEntry("ledger", "ledger.transfer.posted", "operation", operation.Id)
        {
            OwnerUserId = operation.OwnerUserId,
            After = Snapshot.Of(operation)
        });

        if (fee is { } charged)
        {
            var feePosted = await ChargeFeeAsync(operation, from, charged, cancellationToken);
            if (feePosted.IsFailure)
            {
                return feePosted.Error;
            }
        }

        return await reader.DescribeAsync(operation, cancellationToken);
    }

    /// <summary>
    /// The fee is real money that left the origin account, so it is an expense there, attributed to the transfer:
    /// it cannot be edited or voided on its own, and voiding the transfer voids it too.
    /// </summary>
    private async Task<Result> ChargeFeeAsync(Operation transfer, Account from, decimal amount, CancellationToken cancellationToken)
    {
        var category = await categories.EnsureAsync(CategoryUse.Expense, TransferFees.Category, null, cancellationToken);
        if (category.IsFailure)
        {
            return category.Error;
        }

        var posted = Operation.PostTransaction(
            OperationKind.Expense, from, amount, category.Value, transfer.OccurredOn, TransferFees.Description, null, null, false, clock.UtcNow);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        var fee = posted.Value;
        fee.AttributeTo(TransferFees.Module, TransferFees.Type, transfer.Id);
        await rates.ValueAsync(fee, cancellationToken);
        fee.Announce(LedgerOperationChange.Posted);
        db.Operations.Add(fee);
        LedgerMetrics.OperationsPosted.Add(1, new KeyValuePair<string, object?>("kind", nameof(OperationKind.Expense)));
        audit.Record(new AuditEntry("ledger", "ledger.transfer.fee_posted", "operation", fee.Id)
        {
            OwnerUserId = fee.OwnerUserId,
            After = new { TransferId = transfer.Id, Amount = amount, Currency = from.Currency }
        });
        return Result.Success();
    }
}
