using System.Diagnostics;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.Infrastructure.Observability;
using PersonalFinance.Infrastructure.Security;
using PersonalFinance.Modules.Scheduling.Application;

namespace PersonalFinance.Worker.Jobs.Scheduling;

/// <summary>
/// Every quarter of an hour, runs what is due today for each person in each of their workspaces. It acts as
/// that person, in a scope of its own, so everything it reads and writes is limited to their data exactly as
/// if they had asked for it; a failure with one person does not stop the others.
/// </summary>
internal sealed class RunDueOccurrencesJob(IServiceScopeFactory scopeFactory, ILogger<RunDueOccurrencesJob> logger) : PeriodicJob(scopeFactory, logger)
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    protected override TimeSpan Interval => TimeSpan.FromMinutes(15);

    protected override async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var owners = await services.GetRequiredService<IQueryHandler<ListScheduleOwnersQuery, IReadOnlyList<ScheduleOwner>>>()
            .HandleAsync(new ListScheduleOwnersQuery(), cancellationToken);
        var (posted, failed, notices) = (0, 0, 0);
        foreach (var owner in owners.Value)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var actor = scope.ServiceProvider.GetRequiredService<IActorContextInitializer>();
                actor.ActAs(owner.UserId, ActorType.System, "Worker");
                actor.EnterWorkspace(new WorkspaceMembershipInfo(owner.WorkspaceId, owner.UserId, owner.Role, owner.BaseCurrency, owner.TimeZone));
                var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RunScheduleCommand, RunScheduleResponse>>()
                    .HandleAsync(new RunScheduleCommand(), cancellationToken);
                if (result.IsSuccess)
                {
                    posted += result.Value.Posted;
                    failed += result.Value.Failed;
                    notices += result.Value.Notices;
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                Logger.LogError(exception, "The schedule of user {UserId} in workspace {WorkspaceId} could not be run", owner.UserId, owner.WorkspaceId);
            }
        }

        Telemetry.ScheduleCycleDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        if (posted + failed + notices > 0)
        {
            Logger.LogInformation("Schedule cycle: {Posted} registered, {Failed} failed, {Notices} notice(s)", posted, failed, notices);
        }
    }
}
