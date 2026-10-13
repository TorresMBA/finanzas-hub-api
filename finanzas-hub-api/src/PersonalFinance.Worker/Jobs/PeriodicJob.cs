namespace PersonalFinance.Worker.Jobs;

/// <summary>
/// A job that runs once at startup and then on a fixed interval. Each run gets its own DI scope, like a request.
/// A run that fails is logged and tried again soon; it never takes the process down.
/// A job holds no business rule: it sets who it acts for and calls a use case of its module.
/// </summary>
internal abstract class PeriodicJob(IServiceScopeFactory scopeFactory, ILogger logger) : BackgroundService
{
    // A failure is usually something that passes on its own (the database is restarting or still being
    // migrated), so a job that runs once a day must not wait a day to try again.
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

    protected ILogger Logger { get; } = logger;

    protected abstract TimeSpan Interval { get; }

    protected abstract Task RunAsync(IServiceProvider services, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var wait = Interval;
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await RunAsync(scope.ServiceProvider, stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    wait = Interval < RetryAfterFailure ? Interval : RetryAfterFailure;
                    Logger.LogError(exception, "{Job} failed; it will run again in {Wait}", GetType().Name, wait);
                }

                await Task.Delay(wait, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is shutting down.
        }
    }
}
