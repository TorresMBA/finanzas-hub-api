using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Application.Accounts;
using PersonalFinance.Modules.Ledger.Application.Transfers;
using PersonalFinance.Modules.Ledger.Contracts;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.Transactions;

/// <summary>
/// Registers for another module through the very use cases a person uses, so the same rules, the same audit and
/// the same ownership apply: everything here is about the accounts of the current person. Called inside a
/// command, it joins that command's transaction.
/// </summary>
internal sealed class LedgerPosting(
    ICommandHandler<PostTransactionCommand, TransactionResponse> postTransaction, TransferPoster transfers, ILedgerDb db, LedgerScope scope, IAuditLog audit,
    IClock clock) : ILedgerPosting
{
    private const string GoalsModule = "goals";

    public async Task<Result<Guid>> PostTransactionAsync(LedgerTransactionRequest request, CancellationToken cancellationToken)
    {
        var posted = await postTransaction.HandleAsync(
            new PostTransactionCommand(
                request.IsExpense ? TransactionType.Expense : TransactionType.Income, request.AccountId, request.Amount, request.CategoryId, request.OccurredOn,
                request.Description, null, null, null, false, request.IdempotencyKey),
            cancellationToken);
        return posted.IsFailure ? posted.Error : posted.Value.Id;
    }

    public async Task<Result<Guid>> OpenGoalFundAsync(string goalName, string currency, CancellationToken cancellationToken)
    {
        if (scope.Owner is not (var workspaceId, var userId))
        {
            return LedgerErrors.Unauthenticated;
        }

        // An account name is unique among those in use; a goal may be called like another account or another goal.
        var name = $"Meta: {goalName}";
        name = name.Length > Account.MaxNameLength - 3 ? name[..(Account.MaxNameLength - 3)] : name;
        for (var attempt = 1; ; attempt++)
        {
            var opened = Account.Open(
                workspaceId, userId, attempt == 1 ? name : $"{name} {attempt}", AccountType.GoalFund, currency, null, clock.UtcNow, includeInNetWorth: false);
            if (opened.IsFailure)
            {
                return opened.Error;
            }

            if (await db.NameIsTakenAsync(opened.Value, cancellationToken) && attempt < 9)
            {
                continue;
            }

            db.Accounts.Add(opened.Value);
            audit.Record(new AuditEntry("ledger", "ledger.account.opened", "account", opened.Value.Id)
            {
                OwnerUserId = userId,
                After = new { opened.Value.Name, Type = nameof(AccountType.GoalFund), opened.Value.Currency }
            });
            return opened.Value.Id;
        }
    }

    public async Task<Result<Guid>> MoveGoalMoneyAsync(
        Guid fromAccountId, Guid toAccountId, decimal amount, DateOnly occurredOn, string description, Guid sourceId, CancellationToken cancellationToken)
    {
        var moved = await transfers.PostAsync(
            fromAccountId, toAccountId, amount, null, occurredOn, description, null,
            (from, to) => from.Currency != to.Currency
                ? Error.BusinessRule("ledger.goal.currency_mismatch", "The account must have the same currency as the goal.")
                : from.IsCreditCard || to.IsCreditCard
                    ? Error.BusinessRule("ledger.goal.card_not_allowed", "A goal is funded from an account that holds money, not from a credit card.")
                : from.IsGoalFund == to.IsGoalFund
                    ? Error.BusinessRule("ledger.goal.fund_required", "One side of the movement must be the fund of the goal, and only one.")
                    : Result.Success(),
            cancellationToken, (GoalsModule, "contribution", sourceId));
        return moved.IsFailure ? moved.Error : moved.Value.Id;
    }
}
