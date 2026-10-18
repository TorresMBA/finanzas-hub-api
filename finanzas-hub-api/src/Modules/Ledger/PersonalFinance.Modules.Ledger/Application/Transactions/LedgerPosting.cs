using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Application.Accounts;
using PersonalFinance.Modules.Ledger.Application.ExchangeRates;
using PersonalFinance.Modules.Ledger.Application.Transfers;
using PersonalFinance.Modules.Ledger.Contracts;
using PersonalFinance.Modules.Ledger.Contracts.Events;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.Transactions;

/// <summary>
/// Registers for another module through the very use cases a person uses, so the same rules, the same audit and
/// the same ownership apply: everything here is about the accounts of the current person. Called inside a
/// command, it joins that command's transaction.
/// </summary>
internal sealed class LedgerPosting(
    ICommandHandler<PostTransactionCommand, TransactionResponse> postTransaction, ICommandHandler<VoidTransactionCommand, TransactionResponse> voidTransaction,
    TransferPoster transfers, TransactionReader reader, ExchangeRateBook rates,
    ILedgerDb db, LedgerScope scope, IAuditLog audit, IClock clock) : ILedgerPosting
{
    private const string GoalsModule = "goals";
    private const string InvestmentsModule = "investments";

    public async Task<Result<Guid>> PostTransactionAsync(LedgerTransactionRequest request, CancellationToken cancellationToken)
    {
        var posted = await postTransaction.HandleAsync(
            new PostTransactionCommand(
                request.IsExpense ? TransactionType.Expense : TransactionType.Income, request.AccountId, request.Amount, request.CategoryId, request.OccurredOn,
                request.Description, null, null, null, false, request.IdempotencyKey),
            cancellationToken);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        if (request.FundedByGoalId is { } goalId && await db.Operations.FindAsync([posted.Value.Id], cancellationToken) is { } operation)
        {
            operation.MarkFundedByGoal(goalId);
        }

        return posted.Value.Id;
    }

    public async Task<Result> VoidAsync(Guid operationId, string reason, CancellationToken cancellationToken)
    {
        var voided = await voidTransaction.HandleAsync(new VoidTransactionCommand(operationId, reason), cancellationToken);
        return voided.IsFailure ? voided.Error : Result.Success();
    }

    public async Task<Result> VoidLinkedAsync(Guid operationId, Guid ownerUserId, string reason, CancellationToken cancellationToken)
    {
        // The owner filter is that of the current person; the movement is of the other side, found by its id and its owner only.
        if (await db.Operations.IgnoreQueryFilters().Include(operation => operation.Entries)
                .SingleOrDefaultAsync(operation => operation.Id == operationId && operation.OwnerUserId == ownerUserId, cancellationToken) is not { } operation)
        {
            return LedgerErrors.TransactionNotFound;
        }

        var wasEffective = operation.IsEffective;
        var voided = operation.Void(reason, clock.UtcNow);
        if (voided.IsFailure || !wasEffective)
        {
            return voided;
        }

        // Not announced: the reactions to a movement (budgets, notices) read under the current person, who is not its owner.
        audit.Record(new AuditEntry("ledger", "ledger.transaction.voided", "operation", operation.Id)
        {
            OwnerUserId = operation.OwnerUserId,
            After = new { Status = operation.Status.ToString(), operation.VoidReason, VoidedByCounterpart = true }
        });
        return Result.Success();
    }

    public async Task<Result> VoidGoalMoveAsync(Guid operationId, string reason, CancellationToken cancellationToken)
    {
        // The owner filter leaves out a movement of someone else, and VoidFor one that the goals did not register.
        if (await db.FindOperationAsync(operationId, cancellationToken) is not { } operation)
        {
            return LedgerErrors.TransactionNotFound;
        }

        var wasEffective = operation.IsEffective;
        var voided = operation.VoidFor(GoalsModule, reason, clock.UtcNow);
        if (voided.IsFailure || !wasEffective)
        {
            return voided;
        }

        operation.Announce(LedgerOperationChange.Voided);
        audit.Record(new AuditEntry("ledger", "ledger.transaction.voided", "operation", operation.Id)
        {
            OwnerUserId = operation.OwnerUserId,
            After = new { Status = operation.Status.ToString(), operation.VoidReason, SourceModule = GoalsModule }
        });
        return Result.Success();
    }

    public async Task<Result<Guid?>> ReturnGoalMoneyAsync(
        Guid contributionOperationId, Guid ownerUserId, decimal amount, DateOnly occurredOn, string description, Guid sourceId, CancellationToken cancellationToken)
    {
        // The owner filter is that of the current person; the contribution may be of another participant, found by its id, its owner and its module.
        if (await db.Operations.IgnoreQueryFilters().AsNoTracking().Include(operation => operation.Entries)
                .SingleOrDefaultAsync(
                    operation => operation.Id == contributionOperationId && operation.OwnerUserId == ownerUserId && operation.SourceModule == GoalsModule
                                 && operation.Kind == OperationKind.Transfer,
                    cancellationToken) is not { } contribution)
        {
            return LedgerErrors.TransactionNotFound;
        }

        var (originId, fundId) = (
            contribution.Entries.Single(entry => entry.Direction == EntryDirection.Out).AccountId,
            contribution.Entries.Single(entry => entry.Direction == EntryDirection.In).AccountId);
        var accounts = await db.Accounts.IgnoreQueryFilters()
            .Where(account => (account.Id == originId || account.Id == fundId) && account.OwnerUserId == ownerUserId)
            .ToListAsync(cancellationToken);
        if (accounts.SingleOrDefault(account => account.Id == originId) is not { } origin || accounts.SingleOrDefault(account => account.Id == fundId) is not { } fund)
        {
            return LedgerErrors.AccountNotFound;
        }

        // Its owner archived it: the money stays in the fund, where they can still withdraw it to another account.
        if (origin.EnsureOpen().IsFailure)
        {
            return (Guid?)null;
        }

        var posted = Operation.PostTransfer(fund, origin, amount, null, occurredOn, description, null, clock.UtcNow, byModule: true);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        var operation = posted.Value;
        operation.AttributeTo(GoalsModule, "contribution", sourceId);
        // The rates are those of the current person: someone else's movement is left unvalued, as one without a rate.
        if (ownerUserId == scope.Owner?.UserId)
        {
            await rates.ValueAsync(operation, cancellationToken);
        }

        db.Operations.Add(operation);
        audit.Record(new AuditEntry("ledger", "ledger.transfer.posted", "operation", operation.Id)
        {
            OwnerUserId = operation.OwnerUserId,
            After = Snapshot.Of(operation)
        });
        return operation.Id;
    }

    public async Task<Result<Guid>> PostRefundAsync(
        Guid accountId, decimal amount, Guid categoryId, DateOnly occurredOn, string description, CancellationToken cancellationToken)
    {
        // The owner filter makes an account of someone else indistinguishable from one that does not exist.
        if (await db.Accounts.SingleOrDefaultAsync(candidate => candidate.Id == accountId, cancellationToken) is not { } account)
        {
            return LedgerErrors.AccountNotFound;
        }

        var category = await reader.ValidateCategoryAsync(TransactionType.Expense, categoryId, null, cancellationToken);
        if (category.IsFailure)
        {
            return category.Error;
        }

        var posted = Operation.PostRefund(account, amount, categoryId, occurredOn, description, clock.UtcNow);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        var operation = posted.Value;
        await rates.ValueAsync(operation, cancellationToken);
        db.Operations.Add(operation);
        audit.Record(new AuditEntry("ledger", "ledger.refund.posted", "operation", operation.Id)
        {
            OwnerUserId = operation.OwnerUserId,
            After = new { operation.CategoryId, amount, account.Currency, occurredOn }
        });
        return operation.Id;
    }

    public Task<Result<Guid>> OpenGoalFundAsync(string goalName, string currency, CancellationToken cancellationToken) =>
        // The money of a goal is still the person's: putting it aside does not lower the net worth, spending it does.
        OpenManagedAsync($"Meta: {goalName}", AccountType.GoalFund, currency, includeInNetWorth: true, cancellationToken);

    public Task<Result<Guid>> OpenInvestmentAccountAsync(string investmentName, string currency, CancellationToken cancellationToken) =>
        OpenManagedAsync($"Inversión: {investmentName}", AccountType.Investment, currency, includeInNetWorth: true, cancellationToken);

    public async Task<Result<Guid>> MoveInvestmentMoneyAsync(
        Guid fromAccountId, Guid toAccountId, decimal amount, DateOnly occurredOn, string description, Guid sourceId, CancellationToken cancellationToken)
    {
        var moved = await transfers.PostAsync(
            fromAccountId, toAccountId, amount, null, occurredOn, description, null,
            InvestmentMoveRule, cancellationToken, (InvestmentsModule, "movement", sourceId));
        return moved.IsFailure ? moved.Error : moved.Value.Id;
    }

    private static Result InvestmentMoveRule(Account from, Account to)
    {
        if (from.Currency != to.Currency)
        {
            return Error.BusinessRule("ledger.investment.currency_mismatch", "The account must have the same currency as the investment.");
        }

        if (from.IsCreditCard || to.IsCreditCard || from.IsGoalFund || to.IsGoalFund)
        {
            return Error.BusinessRule("ledger.investment.account_not_allowed", "An investment is funded from an account that holds money, not from a credit card or a goal.");
        }

        return from.IsInvestment == to.IsInvestment
            ? Error.BusinessRule("ledger.investment.account_required", "One side of the movement must be the account of the investment, and only one.")
            : Result.Success();
    }

    /// <summary>An account another module opens for itself. Its name is unique among those in use, so a number is added when taken.</summary>
    private async Task<Result<Guid>> OpenManagedAsync(string name, AccountType type, string currency, bool includeInNetWorth, CancellationToken cancellationToken)
    {
        if (scope.Owner is not (var workspaceId, var userId))
        {
            return LedgerErrors.Unauthenticated;
        }

        name = name.Length > Account.MaxNameLength - 3 ? name[..(Account.MaxNameLength - 3)] : name;
        for (var attempt = 1; ; attempt++)
        {
            var opened = Account.Open(
                workspaceId, userId, attempt == 1 ? name : $"{name} {attempt}", type, currency, null, clock.UtcNow, includeInNetWorth: includeInNetWorth);
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
                After = new { opened.Value.Name, Type = type.ToString(), opened.Value.Currency }
            });
            return opened.Value.Id;
        }
    }

    public async Task<Result<Guid>> MoveGoalMoneyAsync(
        Guid fromAccountId, Guid toAccountId, decimal amount, DateOnly occurredOn, string description, Guid sourceId, CancellationToken cancellationToken)
    {
        var moved = await transfers.PostAsync(
            fromAccountId, toAccountId, amount, null, occurredOn, description, null,
            GoalMoveRule, cancellationToken, (GoalsModule, "contribution", sourceId));
        return moved.IsFailure ? moved.Error : moved.Value.Id;
    }

    private static Result GoalMoveRule(Account from, Account to)
    {
        if (from.Currency != to.Currency)
        {
            return Error.BusinessRule("ledger.goal.currency_mismatch", "The account must have the same currency as the goal.");
        }

        if (from.IsCreditCard || to.IsCreditCard)
        {
            return Error.BusinessRule("ledger.goal.card_not_allowed", "A goal is funded from an account that holds money, not from a credit card.");
        }

        return from.IsGoalFund == to.IsGoalFund
            ? Error.BusinessRule("ledger.goal.fund_required", "One side of the movement must be the fund of the goal, and only one.")
            : Result.Success();
    }
}
