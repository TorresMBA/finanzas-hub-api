using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Application.ExchangeRates;
using PersonalFinance.Modules.Ledger.Application.Transactions;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.Transfers;

/// <summary>
/// Moves money from an account of the open book to one of another book of the same person, like from their personal
/// account to that of their business (ADR-023). Each book keeps one leg; in neither is it an income or an expense.
/// </summary>
/// <param name="ToWorkspaceId">The workspace whose book receives the money: for a Personal one, the person's personal book.</param>
/// <param name="Amount">What leaves the origin account, in its currency.</param>
/// <param name="DestinationAmount">What arrives, in the currency of the destination. Required when the two currencies differ.</param>
public sealed record PostBookTransferCommand(
    Guid FromAccountId, Guid ToWorkspaceId, Guid ToAccountId, decimal Amount, decimal? DestinationAmount, DateOnly OccurredOn, string? Description,
    string? Note, string IdempotencyKey)
    : ICommand<TransactionResponse>, IIdempotentCommand;

internal sealed class PostBookTransferHandler(ILedgerDb db, OtherBooks books, ExchangeRateBook rates, TransactionReader reader, IAuditLog audit, IClock clock)
    : ICommandHandler<PostBookTransferCommand, TransactionResponse>
{
    public async Task<Result<TransactionResponse>> HandleAsync(PostBookTransferCommand command, CancellationToken cancellationToken)
    {
        if (await db.Accounts.SingleOrDefaultAsync(account => account.Id == command.FromAccountId, cancellationToken) is not { } from)
        {
            return LedgerErrors.AccountNotFound;
        }

        // A workspace the person does not belong to, or an account that is not theirs in its book, is indistinguishable from one that does not exist.
        if (await books.ResolveAsync(command.ToWorkspaceId, cancellationToken) is not { } book
            || await books.FindAccountAsync(book.WorkspaceId, command.ToAccountId, cancellationToken) is not { } to)
        {
            return LedgerErrors.AccountNotFound;
        }

        var posted = Operation.PostBookTransfer(
            from, to, command.Amount, command.DestinationAmount, command.OccurredOn, command.Description, command.Note, clock.UtcNow);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        var (leaving, arriving) = posted.Value;
        await rates.ValueAsync(leaving, cancellationToken);
        ExchangeRateBook.Value(arriving, await books.RatesAsync(book, cancellationToken));
        db.Operations.Add(leaving);
        db.Operations.Add(arriving);
        LedgerMetrics.OperationsPosted.Add(2, new KeyValuePair<string, object?>("kind", nameof(OperationKind.Transfer)));
        foreach (var leg in new[] { leaving, arriving })
        {
            audit.Record(new AuditEntry("ledger", "ledger.book_transfer.posted", "operation", leg.Id)
            {
                OwnerUserId = leg.OwnerUserId,
                After = Snapshot.Of(leg)
            });
        }

        return await reader.DescribeAsync(leaving, cancellationToken);
    }
}

/// <summary>
/// The only way into another book of the signed-in person than the one open: the destination of a transfer between
/// books and its other leg. The owner filter covers the open book only, so every query here states the owner itself
/// and reaches nothing of anyone else.
/// </summary>
internal sealed class OtherBooks(ILedgerDb db, ICurrentUser currentUser, IWorkspaceAccess access)
{
    /// <summary>The book a workspace of the person works on, by the same rule as a request to it. Null when they are not a member.</summary>
    public async Task<BookInfo?> ResolveAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId && await access.GetActiveMembershipAsync(workspaceId, userId, cancellationToken) is { } membership
            ? membership.Personal ?? new BookInfo(membership.WorkspaceId, membership.BaseCurrency, membership.TimeZone)
            : null;

    public async Task<Account?> FindAccountAsync(Guid bookId, Guid accountId, CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId
            ? await db.Accounts.IgnoreQueryFilters()
                .SingleOrDefaultAsync(account => account.Id == accountId && account.OwnerUserId == userId && account.WorkspaceId == bookId, cancellationToken)
            : null;

    public async Task<RateTable> RatesAsync(BookInfo book, CancellationToken cancellationToken)
    {
        if (book.BaseCurrency is not { } baseCurrency || currentUser.UserId is not { } userId)
        {
            return new RateTable(book.BaseCurrency, []);
        }

        var rates = await db.ExchangeRates.IgnoreQueryFilters().AsNoTracking()
            .Where(rate => rate.OwnerUserId == userId && rate.WorkspaceId == book.WorkspaceId && rate.BaseCurrency == baseCurrency)
            .ToListAsync(cancellationToken);
        return new RateTable(baseCurrency, rates);
    }

    /// <summary>The other legs of these transfers between books, with the name of the account each one moved, to show where the money went.</summary>
    public async Task<IReadOnlyDictionary<Guid, (Entry Entry, string AccountName, Guid BookId)>> CounterpartsAsync(
        IReadOnlyCollection<Guid> counterpartIds, CancellationToken cancellationToken)
    {
        if (counterpartIds.Count == 0 || currentUser.UserId is not { } userId)
        {
            return new Dictionary<Guid, (Entry, string, Guid)>();
        }

        // A leg registered by the command in progress is not in the database yet.
        var legs = db.Operations.Local.Where(operation => counterpartIds.Contains(operation.Id)).ToList();
        var missing = counterpartIds.Except(legs.Select(leg => leg.Id)).ToList();
        if (missing.Count > 0)
        {
            legs.AddRange(await db.Operations.IgnoreQueryFilters().AsNoTracking().Include(operation => operation.Entries)
                .Where(operation => missing.Contains(operation.Id) && operation.OwnerUserId == userId)
                .ToListAsync(cancellationToken));
        }

        var entries = legs.Where(leg => leg.Entries.Count > 0).Select(leg => (leg.Id, leg.WorkspaceId, Entry: leg.Entries.First())).ToList();
        var accountIds = entries.Select(leg => leg.Entry.AccountId).Distinct().ToList();
        var names = await db.Accounts.IgnoreQueryFilters().AsNoTracking()
            .Where(account => accountIds.Contains(account.Id) && account.OwnerUserId == userId)
            .ToDictionaryAsync(account => account.Id, account => account.Name, cancellationToken);
        return entries.ToDictionary(leg => leg.Id, leg => (leg.Entry, names.GetValueOrDefault(leg.Entry.AccountId, string.Empty), leg.WorkspaceId));
    }

    /// <summary>The other leg of a transfer between books, to void it with the one the person voided.</summary>
    public async Task<Operation?> FindCounterpartAsync(Operation leg, CancellationToken cancellationToken) =>
        leg.CounterpartOperationId is { } counterpartId && currentUser.UserId is { } userId
            ? await db.Operations.IgnoreQueryFilters().Include(operation => operation.Entries)
                .SingleOrDefaultAsync(operation => operation.Id == counterpartId && operation.OwnerUserId == userId, cancellationToken)
            : null;
}
