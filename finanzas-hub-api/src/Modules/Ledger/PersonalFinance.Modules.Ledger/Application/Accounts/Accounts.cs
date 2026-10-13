using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.Accounts;

/// <param name="Balance">Derived from the ledger at the moment of the answer; it is stored nowhere.</param>
public sealed record AccountResponse(
    Guid Id, string Name, AccountType Type, string Currency, string? Institution, decimal Balance, bool IsArchived, DateTimeOffset CreatedAt)
{
    internal static AccountResponse From(Account account, decimal balance) =>
        new(account.Id, account.Name, account.Type, account.Currency, account.Institution, balance, account.IsArchived, account.CreatedAt);
}

public sealed record ListAccountsQuery(bool IncludeArchived) : IQuery<IReadOnlyList<AccountResponse>>;

internal sealed class ListAccountsHandler(ILedgerDb db, AccountBalances balances) : IQueryHandler<ListAccountsQuery, IReadOnlyList<AccountResponse>>
{
    public async Task<Result<IReadOnlyList<AccountResponse>>> HandleAsync(ListAccountsQuery query, CancellationToken cancellationToken)
    {
        // No owner condition here on purpose: the context's global filter already limits accounts and entries to the current person.
        var accounts = await db.Accounts.AsNoTracking()
            .Where(account => query.IncludeArchived || account.ArchivedAt == null)
            .OrderBy(account => account.CreatedAt)
            .ToListAsync(cancellationToken);
        var byAccount = await balances.AllAsync(cancellationToken);
        return accounts.Select(account => AccountResponse.From(account, byAccount.GetValueOrDefault(account.Id))).ToList();
    }
}

public sealed record GetAccountQuery(Guid AccountId) : IQuery<AccountResponse>;

internal sealed class GetAccountHandler(ILedgerDb db, AccountBalances balances) : IQueryHandler<GetAccountQuery, AccountResponse>
{
    public async Task<Result<AccountResponse>> HandleAsync(GetAccountQuery query, CancellationToken cancellationToken) =>
        await db.Accounts.AsNoTracking().SingleOrDefaultAsync(account => account.Id == query.AccountId, cancellationToken) is { } account
            ? AccountResponse.From(account, await balances.OfAsync(account.Id, cancellationToken))
            : LedgerErrors.AccountNotFound;
}

/// <param name="OpeningBalance">What the account holds today. Zero opens it empty; a negative amount is an overdraft.</param>
public sealed record OpenAccountCommand(string Name, AccountType Type, string Currency, string? Institution, decimal OpeningBalance, string IdempotencyKey)
    : ICommand<AccountResponse>, IIdempotentCommand;

internal sealed class OpenAccountHandler(ILedgerDb db, LedgerScope scope, IAuditLog audit, IClock clock) : ICommandHandler<OpenAccountCommand, AccountResponse>
{
    public async Task<Result<AccountResponse>> HandleAsync(OpenAccountCommand command, CancellationToken cancellationToken)
    {
        if (scope.Owner is not (var workspaceId, var userId))
        {
            return LedgerErrors.Unauthenticated;
        }

        var opened = Account.Open(workspaceId, userId, command.Name, command.Type, command.Currency, command.Institution, clock.UtcNow);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        var account = opened.Value;
        if (await db.NameIsTakenAsync(account, cancellationToken))
        {
            return LedgerErrors.AccountNameTaken;
        }

        db.Accounts.Add(account);
        if (command.OpeningBalance != 0)
        {
            // The starting balance is an entry like any other, so the balance stays a plain sum.
            var opening = Operation.OpenBalance(account, command.OpeningBalance, scope.Today, clock.UtcNow);
            if (opening.IsFailure)
            {
                return opening.Error;
            }

            db.Operations.Add(opening.Value);
            LedgerMetrics.OperationsPosted.Add(1, new KeyValuePair<string, object?>("kind", nameof(OperationKind.OpeningBalance)));
        }

        audit.Record(new AuditEntry("ledger", "ledger.account.opened", "account", account.Id)
        {
            OwnerUserId = userId,
            After = new { account.Name, Type = account.Type.ToString(), account.Currency, command.OpeningBalance }
        });
        return AccountResponse.From(account, command.OpeningBalance);
    }
}

public sealed record UpdateAccountCommand(Guid AccountId, string Name, string? Institution) : ICommand<AccountResponse>;

internal sealed class UpdateAccountHandler(ILedgerDb db, AccountBalances balances, IAuditLog audit) : ICommandHandler<UpdateAccountCommand, AccountResponse>
{
    public async Task<Result<AccountResponse>> HandleAsync(UpdateAccountCommand command, CancellationToken cancellationToken)
    {
        if (await db.Accounts.SingleOrDefaultAsync(account => account.Id == command.AccountId, cancellationToken) is not { } account)
        {
            return LedgerErrors.AccountNotFound;
        }

        var before = new { account.Name, account.Institution };
        var updated = account.Update(command.Name, command.Institution);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        if (await db.NameIsTakenAsync(account, cancellationToken))
        {
            return LedgerErrors.AccountNameTaken;
        }

        audit.Record(new AuditEntry("ledger", "ledger.account.updated", "account", account.Id)
        {
            OwnerUserId = account.OwnerUserId,
            Before = before,
            After = new { account.Name, account.Institution }
        });
        return AccountResponse.From(account, await balances.OfAsync(account.Id, cancellationToken));
    }
}

public sealed record ArchiveAccountCommand(Guid AccountId) : ICommand<AccountResponse>;

internal sealed class ArchiveAccountHandler(ILedgerDb db, AccountBalances balances, IAuditLog audit, IClock clock) : ICommandHandler<ArchiveAccountCommand, AccountResponse>
{
    public async Task<Result<AccountResponse>> HandleAsync(ArchiveAccountCommand command, CancellationToken cancellationToken)
    {
        if (await db.Accounts.SingleOrDefaultAsync(account => account.Id == command.AccountId, cancellationToken) is not { } account)
        {
            return LedgerErrors.AccountNotFound;
        }

        if (!account.IsArchived)
        {
            account.Archive(clock.UtcNow);
            audit.Record(new AuditEntry("ledger", "ledger.account.archived", "account", account.Id) { OwnerUserId = account.OwnerUserId });
        }

        return AccountResponse.From(account, await balances.OfAsync(account.Id, cancellationToken));
    }
}

public sealed record RestoreAccountCommand(Guid AccountId) : ICommand<AccountResponse>;

internal sealed class RestoreAccountHandler(ILedgerDb db, AccountBalances balances, IAuditLog audit) : ICommandHandler<RestoreAccountCommand, AccountResponse>
{
    public async Task<Result<AccountResponse>> HandleAsync(RestoreAccountCommand command, CancellationToken cancellationToken)
    {
        if (await db.Accounts.SingleOrDefaultAsync(account => account.Id == command.AccountId, cancellationToken) is not { } account)
        {
            return LedgerErrors.AccountNotFound;
        }

        if (account.IsArchived)
        {
            if (await db.NameIsTakenAsync(account, cancellationToken))
            {
                return LedgerErrors.AccountNameTaken;
            }

            account.Restore();
            audit.Record(new AuditEntry("ledger", "ledger.account.restored", "account", account.Id) { OwnerUserId = account.OwnerUserId });
        }

        return AccountResponse.From(account, await balances.OfAsync(account.Id, cancellationToken));
    }
}

/// <summary>States what the account really holds. The ledger records the difference; nothing is overwritten.</summary>
/// <param name="OccurredOn">The day the balance was checked. Today when left out.</param>
public sealed record AdjustAccountBalanceCommand(Guid AccountId, decimal ActualBalance, DateOnly? OccurredOn, string IdempotencyKey)
    : ICommand<AccountResponse>, IIdempotentCommand;

internal sealed class AdjustAccountBalanceHandler(ILedgerDb db, AccountBalances balances, LedgerScope scope, IAuditLog audit, IClock clock)
    : ICommandHandler<AdjustAccountBalanceCommand, AccountResponse>
{
    public async Task<Result<AccountResponse>> HandleAsync(AdjustAccountBalanceCommand command, CancellationToken cancellationToken)
    {
        if (await db.Accounts.SingleOrDefaultAsync(account => account.Id == command.AccountId, cancellationToken) is not { } account)
        {
            return LedgerErrors.AccountNotFound;
        }

        var current = await balances.OfAsync(account.Id, cancellationToken);
        var difference = command.ActualBalance - current;
        if (difference == 0)
        {
            return AccountResponse.From(account, current);
        }

        var adjustment = Operation.Adjust(account, difference, command.OccurredOn ?? scope.Today, clock.UtcNow);
        if (adjustment.IsFailure)
        {
            return adjustment.Error;
        }

        db.Operations.Add(adjustment.Value);
        LedgerMetrics.OperationsPosted.Add(1, new KeyValuePair<string, object?>("kind", nameof(OperationKind.Adjustment)));
        audit.Record(new AuditEntry("ledger", "ledger.account.balance_adjusted", "account", account.Id)
        {
            OwnerUserId = account.OwnerUserId,
            Before = new { Balance = current },
            After = new { Balance = command.ActualBalance, OperationId = adjustment.Value.Id }
        });
        return AccountResponse.From(account, command.ActualBalance);
    }
}

internal static class AccountQueries
{
    /// <summary>Whether another active account of the person already has this name.</summary>
    public static Task<bool> NameIsTakenAsync(this ILedgerDb db, Account account, CancellationToken cancellationToken) =>
        db.Accounts.AnyAsync(other => other.ArchivedAt == null && other.NameKey == account.NameKey && other.Id != account.Id, cancellationToken);
}
