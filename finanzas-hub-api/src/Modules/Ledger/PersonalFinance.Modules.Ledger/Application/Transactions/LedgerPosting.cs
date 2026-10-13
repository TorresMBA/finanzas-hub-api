using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Ledger.Contracts;

namespace PersonalFinance.Modules.Ledger.Application.Transactions;

/// <summary>
/// Registers for another module through the very use case a person uses, so the same rules, the same audit and
/// the same idempotency apply. Called inside a command, it joins that command's transaction.
/// </summary>
internal sealed class LedgerPosting(ICommandHandler<PostTransactionCommand, TransactionResponse> postTransaction) : ILedgerPosting
{
    public async Task<Result<Guid>> PostTransactionAsync(LedgerTransactionRequest request, CancellationToken cancellationToken)
    {
        var posted = await postTransaction.HandleAsync(
            new PostTransactionCommand(
                request.IsExpense ? TransactionType.Expense : TransactionType.Income, request.AccountId, request.Amount, request.CategoryId, request.OccurredOn,
                request.Description, null, null, null, false, request.IdempotencyKey),
            cancellationToken);
        return posted.IsFailure ? posted.Error : posted.Value.Id;
    }
}
