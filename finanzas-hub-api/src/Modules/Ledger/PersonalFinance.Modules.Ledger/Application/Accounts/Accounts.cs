using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Application.ExchangeRates;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.Accounts;

/// <param name="Used">What is owed on the card: never negative.</param>
/// <param name="Available">The line minus what is owed. Negative when the card is over its line.</param>
/// <param name="NextPaymentDate">The next day a payment is due, today included.</param>
public sealed record CreditCardResponse(decimal CreditLimit, decimal Used, decimal Available, int PaymentDay, int? ClosingDay, DateOnly NextPaymentDate);

/// <param name="Balance">Derived from the ledger at the moment of the answer; it is stored nowhere. Negative on a credit card with debt.</param>
/// <param name="IncludeInNetWorth">Whether the account counts in the net worth while it is not archived.</param>
/// <param name="CreditCard">Present exactly when the account is a credit card.</param>
public sealed record AccountResponse(
    Guid Id, string Name, AccountType Type, string Currency, string? Institution, decimal Balance, bool IncludeInNetWorth, CreditCardResponse? CreditCard,
    bool IsArchived, DateTimeOffset CreatedAt)
{
    internal static AccountResponse From(Account account, decimal balance, DateOnly today) =>
        new(account.Id, account.Name, account.Type, account.Currency, account.Institution, balance, account.IncludeInNetWorth,
            account.CreditCard is { } card
                ? new CreditCardResponse(card.CreditLimit, Math.Max(0, -balance), card.AvailableWith(balance), card.PaymentDay, card.ClosingDay, card.NextPaymentDate(today))
                : null,
            account.IsArchived, account.CreatedAt);
}

public sealed record ListAccountsQuery(bool IncludeArchived) : IQuery<IReadOnlyList<AccountResponse>>;

internal sealed class ListAccountsHandler(ILedgerDb db, AccountBalances balances, LedgerScope scope) : IQueryHandler<ListAccountsQuery, IReadOnlyList<AccountResponse>>
{
    public async Task<Result<IReadOnlyList<AccountResponse>>> HandleAsync(ListAccountsQuery query, CancellationToken cancellationToken)
    {
        // No owner condition here on purpose: the context's global filter already limits accounts and entries to the current person.
        var accounts = await db.Accounts.AsNoTracking()
            .Where(account => query.IncludeArchived || account.ArchivedAt == null)
            .OrderBy(account => account.CreatedAt)
            .ToListAsync(cancellationToken);
        var byAccount = await balances.AllAsync(cancellationToken);
        var today = scope.Today;
        return accounts.Select(account => AccountResponse.From(account, byAccount.GetValueOrDefault(account.Id), today)).ToList();
    }
}

public sealed record GetAccountQuery(Guid AccountId) : IQuery<AccountResponse>;

internal sealed class GetAccountHandler(ILedgerDb db, AccountBalances balances, LedgerScope scope) : IQueryHandler<GetAccountQuery, AccountResponse>
{
    public async Task<Result<AccountResponse>> HandleAsync(GetAccountQuery query, CancellationToken cancellationToken) =>
        await db.Accounts.AsNoTracking().SingleOrDefaultAsync(account => account.Id == query.AccountId, cancellationToken) is { } account
            ? AccountResponse.From(account, await balances.OfAsync(account.Id, cancellationToken), scope.Today)
            : LedgerErrors.AccountNotFound;
}

/// <param name="OpeningBalance">
/// What the account holds today. Zero opens it empty; a negative amount is money owed: an overdraft, or the debt a
/// credit card already carries.
/// </param>
/// <param name="CreditCard">Required for a credit card, and only allowed for one.</param>
public sealed record OpenAccountCommand(
    string Name, AccountType Type, string Currency, string? Institution, decimal OpeningBalance, bool IncludeInNetWorth, CreditCardTerms? CreditCard,
    string IdempotencyKey)
    : ICommand<AccountResponse>, IIdempotentCommand;

internal sealed class OpenAccountHandler(ILedgerDb db, LedgerScope scope, ExchangeRateBook rates, IAuditLog audit, IClock clock)
    : ICommandHandler<OpenAccountCommand, AccountResponse>
{
    public async Task<Result<AccountResponse>> HandleAsync(OpenAccountCommand command, CancellationToken cancellationToken)
    {
        if (scope.Owner is not (var workspaceId, var userId))
        {
            return LedgerErrors.Unauthenticated;
        }

        var opened = Account.Open(
            workspaceId, userId, command.Name, command.Type, command.Currency, command.Institution, clock.UtcNow, command.CreditCard, command.IncludeInNetWorth);
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

            await rates.ValueAsync(opening.Value, cancellationToken);
            db.Operations.Add(opening.Value);
            LedgerMetrics.OperationsPosted.Add(1, new KeyValuePair<string, object?>("kind", nameof(OperationKind.OpeningBalance)));
        }

        audit.Record(new AuditEntry("ledger", "ledger.account.opened", "account", account.Id)
        {
            OwnerUserId = userId,
            After = new { account.Name, Type = account.Type.ToString(), account.Currency, command.OpeningBalance, account.IncludeInNetWorth, command.CreditCard }
        });
        return AccountResponse.From(account, command.OpeningBalance, scope.Today);
    }
}

/// <param name="IncludeInNetWorth">Left out, the account keeps what it has.</param>
/// <param name="CreditCard">The line and the days of a credit card. Left out, the card keeps the ones it has.</param>
public sealed record UpdateAccountCommand(Guid AccountId, string Name, string? Institution, bool? IncludeInNetWorth, CreditCardTerms? CreditCard)
    : ICommand<AccountResponse>;

internal sealed class UpdateAccountHandler(ILedgerDb db, AccountBalances balances, LedgerScope scope, IAuditLog audit)
    : ICommandHandler<UpdateAccountCommand, AccountResponse>
{
    public async Task<Result<AccountResponse>> HandleAsync(UpdateAccountCommand command, CancellationToken cancellationToken)
    {
        if (await db.Accounts.SingleOrDefaultAsync(account => account.Id == command.AccountId, cancellationToken) is not { } account)
        {
            return LedgerErrors.AccountNotFound;
        }

        var before = Describe(account);
        var updated = account.Update(command.Name, command.Institution, command.IncludeInNetWorth ?? account.IncludeInNetWorth, command.CreditCard);
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
            After = Describe(account)
        });
        return AccountResponse.From(account, await balances.OfAsync(account.Id, cancellationToken), scope.Today);
    }

    private static object Describe(Account account) => new
    {
        account.Name,
        account.Institution,
        account.IncludeInNetWorth,
        account.CreditCard?.CreditLimit,
        account.CreditCard?.PaymentDay,
        account.CreditCard?.ClosingDay
    };
}

public sealed record ArchiveAccountCommand(Guid AccountId) : ICommand<AccountResponse>;

internal sealed class ArchiveAccountHandler(ILedgerDb db, AccountBalances balances, LedgerScope scope, IAuditLog audit, IClock clock)
    : ICommandHandler<ArchiveAccountCommand, AccountResponse>
{
    public async Task<Result<AccountResponse>> HandleAsync(ArchiveAccountCommand command, CancellationToken cancellationToken)
    {
        if (await db.Accounts.SingleOrDefaultAsync(account => account.Id == command.AccountId, cancellationToken) is not { } account)
        {
            return LedgerErrors.AccountNotFound;
        }

        if (account.IsGoalFund)
        {
            return Account.ManagedByGoal;
        }

        if (!account.IsArchived)
        {
            account.Archive(clock.UtcNow);
            audit.Record(new AuditEntry("ledger", "ledger.account.archived", "account", account.Id) { OwnerUserId = account.OwnerUserId });
        }

        return AccountResponse.From(account, await balances.OfAsync(account.Id, cancellationToken), scope.Today);
    }
}

public sealed record RestoreAccountCommand(Guid AccountId) : ICommand<AccountResponse>;

internal sealed class RestoreAccountHandler(ILedgerDb db, AccountBalances balances, LedgerScope scope, IAuditLog audit)
    : ICommandHandler<RestoreAccountCommand, AccountResponse>
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

        return AccountResponse.From(account, await balances.OfAsync(account.Id, cancellationToken), scope.Today);
    }
}

/// <summary>States what the account really holds. The ledger records the difference; nothing is overwritten.</summary>
/// <param name="ActualBalance">Negative for money owed, as the debt of a credit card.</param>
/// <param name="OccurredOn">The day the balance was checked. Today when left out.</param>
public sealed record AdjustAccountBalanceCommand(Guid AccountId, decimal ActualBalance, DateOnly? OccurredOn, string IdempotencyKey)
    : ICommand<AccountResponse>, IIdempotentCommand;

internal sealed class AdjustAccountBalanceHandler(ILedgerDb db, AccountBalances balances, LedgerScope scope, ExchangeRateBook rates, IAuditLog audit, IClock clock)
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
            return AccountResponse.From(account, current, scope.Today);
        }

        var adjustment = Operation.Adjust(account, difference, command.OccurredOn ?? scope.Today, clock.UtcNow);
        if (adjustment.IsFailure)
        {
            return adjustment.Error;
        }

        await rates.ValueAsync(adjustment.Value, cancellationToken);
        db.Operations.Add(adjustment.Value);
        LedgerMetrics.OperationsPosted.Add(1, new KeyValuePair<string, object?>("kind", nameof(OperationKind.Adjustment)));
        audit.Record(new AuditEntry("ledger", "ledger.account.balance_adjusted", "account", account.Id)
        {
            OwnerUserId = account.OwnerUserId,
            Before = new { Balance = current },
            After = new { Balance = command.ActualBalance, OperationId = adjustment.Value.Id }
        });
        return AccountResponse.From(account, command.ActualBalance, scope.Today);
    }
}

internal static class AccountQueries
{
    /// <summary>Whether another active account of the person already has this name.</summary>
    public static Task<bool> NameIsTakenAsync(this ILedgerDb db, Account account, CancellationToken cancellationToken) =>
        db.Accounts.AnyAsync(other => other.ArchivedAt == null && other.NameKey == account.NameKey && other.Id != account.Id, cancellationToken);
}
