using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Ledger.Application.Abstractions;

namespace PersonalFinance.Modules.Ledger.Application;

internal static class LedgerErrors
{
    public static readonly Error Unauthenticated = Error.Unauthorized("auth.unauthenticated", "Authentication is required.");

    public static readonly Error AccountNotFound = Error.NotFound("ledger.account.not_found", "The account was not found.");

    public static readonly Error AccountNameTaken = Error.Conflict("ledger.account.name_taken", "There is already an active account with that name.");

    public static readonly Error TransactionNotFound = Error.NotFound("ledger.transaction.not_found", "The transaction was not found.");

    public static readonly Error CategoryNotFound = Error.NotFound("ledger.category_not_found", "The category was not found.");
}

internal static class LedgerMetrics
{
    // Same meter name as the platform's, which is the one the hosts export.
    private static readonly Meter Meter = new("PersonalFinance");

    public static readonly Counter<long> OperationsPosted =
        Meter.CreateCounter<long>("ledger.operations.posted", description: "Operations registered in the ledger, by kind.");
}

/// <summary>Whose books the current request works on, and what day it is for that person.</summary>
internal sealed class LedgerScope(ICurrentUser currentUser, IPersonalSpace workspace, IClock clock)
{
    /// <summary>Null when there is no signed-in member of the workspace.</summary>
    public (Guid WorkspaceId, Guid UserId)? Owner =>
        currentUser.UserId is { } userId && workspace.WorkspaceId is { } workspaceId ? (workspaceId, userId) : null;

    /// <summary>Today in the workspace's time zone: at 11 pm in Lima it is still today, whatever UTC says.</summary>
    public DateOnly Today
    {
        get
        {
            var zone = workspace.TimeZone is { } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found) ? found : TimeZoneInfo.Utc;
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
        }
    }
}

/// <summary>The only place a balance comes from: the sum of the effective entries of an account.</summary>
internal sealed class AccountBalances(ILedgerDb db)
{
    /// <param name="asOf">Leaves out what happened after that day. Null counts everything.</param>
    public async Task<IReadOnlyDictionary<Guid, decimal>> AllAsync(CancellationToken cancellationToken, DateOnly? asOf = null) =>
        await db.Entries.AsNoTracking()
            .Where(entry => entry.IsEffective && (asOf == null || entry.OccurredOn <= asOf))
            .GroupBy(entry => entry.AccountId)
            .Select(group => new { AccountId = group.Key, Balance = group.Sum(entry => entry.SignedAmount) })
            .ToDictionaryAsync(row => row.AccountId, row => row.Balance, cancellationToken);

    public async Task<decimal> OfAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.Entries.AsNoTracking()
            .Where(entry => entry.IsEffective && entry.AccountId == accountId)
            .SumAsync(entry => entry.SignedAmount, cancellationToken);
}
