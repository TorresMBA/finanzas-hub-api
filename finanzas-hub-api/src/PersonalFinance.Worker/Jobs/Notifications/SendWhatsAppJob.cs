using PersonalFinance.Infrastructure.WhatsApp;

namespace PersonalFinance.Worker.Jobs.Notifications;

/// <summary>Sends the WhatsApp messages left in the queue: notices, verification codes and answers to commands.</summary>
internal sealed class SendWhatsAppJob(IServiceScopeFactory scopeFactory, ILogger<SendWhatsAppJob> logger) : PeriodicJob(scopeFactory, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromSeconds(10);

    protected override async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var sent = await services.GetRequiredService<IDeliveryDispatcher>().SendPendingAsync(cancellationToken);
        if (sent > 0)
        {
            Logger.LogInformation("Sent {Count} WhatsApp message(s)", sent);
        }
    }
}
