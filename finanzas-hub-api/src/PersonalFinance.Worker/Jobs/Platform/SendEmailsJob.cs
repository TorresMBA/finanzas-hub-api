using PersonalFinance.Infrastructure.Email;

namespace PersonalFinance.Worker.Jobs.Platform;

/// <summary>Sends the emails that commands left in the queue: confirmations, password resets and invitations.</summary>
internal sealed class SendEmailsJob(IServiceScopeFactory scopeFactory, ILogger<SendEmailsJob> logger) : PeriodicJob(scopeFactory, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromSeconds(10);

    protected override async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var sent = await services.GetRequiredService<IEmailDispatcher>().SendPendingAsync(cancellationToken);
        if (sent > 0)
        {
            Logger.LogInformation("Sent {Count} email(s)", sent);
        }
    }
}
