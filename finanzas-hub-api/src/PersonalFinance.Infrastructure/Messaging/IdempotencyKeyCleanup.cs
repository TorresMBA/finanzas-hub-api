using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.Infrastructure.Persistence;

namespace PersonalFinance.Infrastructure.Messaging;

/// <summary>Housekeeping for the stored responses of idempotent commands. Called by a Worker job.</summary>
public interface IIdempotencyKeyCleanup
{
    /// <summary>Deletes the keys past their retention and returns how many there were.</summary>
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken);
}

internal sealed class IdempotencyKeyCleanup(AppDbContext db, IClock clock) : IIdempotencyKeyCleanup
{
    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        return db.IdempotencyKeys.Where(record => record.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
    }
}
