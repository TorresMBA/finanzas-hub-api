using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Modules.Notifications.Application;

namespace PersonalFinance.Worker.Jobs.Notifications;

/// <summary>Once a day, deletes the old notices, the WhatsApp messages that already went and the ids of old webhook events.</summary>
internal sealed class PurgeNotificationHistoryJob(IServiceScopeFactory scopeFactory, ILogger<PurgeNotificationHistoryJob> logger) : PeriodicJob(scopeFactory, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(24);

    protected override async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var result = await services.GetRequiredService<ICommandHandler<PurgeNotificationHistoryCommand, PurgeNotificationHistoryResponse>>()
            .HandleAsync(new PurgeNotificationHistoryCommand(), cancellationToken);
        if (result.IsSuccess && Logger.IsEnabled(LogLevel.Information))
        {
            Logger.LogInformation(
                "Removed {Notifications} old notice(s), {Deliveries} WhatsApp message(s) and {Events} webhook event id(s)",
                result.Value.Notifications, result.Value.Deliveries, result.Value.WebhookEvents);
        }
    }
}
