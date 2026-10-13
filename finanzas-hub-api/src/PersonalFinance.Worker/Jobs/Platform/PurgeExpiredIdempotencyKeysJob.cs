using PersonalFinance.Infrastructure.Messaging;

namespace PersonalFinance.Worker.Jobs.Platform;

/// <summary>Deletes the stored responses of idempotent commands once their retention has passed.</summary>
internal sealed class PurgeExpiredIdempotencyKeysJob(IServiceScopeFactory scopeFactory, ILogger<PurgeExpiredIdempotencyKeysJob> logger)
    : PeriodicJob(scopeFactory, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(24);

    protected override async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var removed = await services.GetRequiredService<IIdempotencyKeyCleanup>().PurgeExpiredAsync(cancellationToken);
        Logger.LogInformation("Removed {Count} expired idempotency key(s)", removed);
    }
}
