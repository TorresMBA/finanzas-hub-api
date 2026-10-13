using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Application.ExchangeRates;
using PersonalFinance.Modules.Ledger.Application.Transactions;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.Transfers;

/// <summary>Moves money between two accounts of the person. It is neither an income nor an expense, and net worth does not change.</summary>
/// <param name="Amount">What leaves the origin account, in its currency.</param>
/// <param name="DestinationAmount">What arrives, in the currency of the destination. Required when the two currencies differ.</param>
public sealed record PostTransferCommand(
    Guid FromAccountId, Guid ToAccountId, decimal Amount, decimal? DestinationAmount, DateOnly OccurredOn, string? Description, string? Note,
    string IdempotencyKey)
    : ICommand<TransactionResponse>, IIdempotentCommand;

internal sealed class PostTransferHandler(TransferPoster poster) : ICommandHandler<PostTransferCommand, TransactionResponse>
{
    public Task<Result<TransactionResponse>> HandleAsync(PostTransferCommand command, CancellationToken cancellationToken) =>
        poster.PostAsync(
            command.FromAccountId, command.ToAccountId, command.Amount, command.DestinationAmount, command.OccurredOn, command.Description, command.Note,
            (_, _) => Result.Success(), cancellationToken);
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
internal sealed class TransferPoster(ILedgerDb db, ExchangeRateBook rates, TransactionReader reader, IAuditLog audit, IClock clock)
{
    public async Task<Result<TransactionResponse>> PostAsync(
        Guid fromAccountId, Guid toAccountId, decimal amountOut, decimal? amountIn, DateOnly occurredOn, string? description, string? note,
        Func<Account, Account, Result> rule, CancellationToken cancellationToken)
    {
        // The owner filter makes an account of someone else indistinguishable from one that does not exist: both must be the person's.
        var accounts = await db.Accounts.Where(account => account.Id == fromAccountId || account.Id == toAccountId).ToListAsync(cancellationToken);
        var from = accounts.SingleOrDefault(account => account.Id == fromAccountId);
        var to = accounts.SingleOrDefault(account => account.Id == toAccountId);
        if (from is null || to is null)
        {
            return LedgerErrors.AccountNotFound;
        }

        var allowed = rule(from, to);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var posted = Operation.PostTransfer(from, to, amountOut, amountIn, occurredOn, description, note, clock.UtcNow);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        var operation = posted.Value;
        await rates.ValueAsync(operation, cancellationToken);
        db.Operations.Add(operation);
        LedgerMetrics.OperationsPosted.Add(1, new KeyValuePair<string, object?>("kind", nameof(OperationKind.Transfer)));
        audit.Record(new AuditEntry("ledger", "ledger.transfer.posted", "operation", operation.Id)
        {
            OwnerUserId = operation.OwnerUserId,
            After = Snapshot.Of(operation)
        });
        return await reader.DescribeAsync(operation, cancellationToken);
    }
}
