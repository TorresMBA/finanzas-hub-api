using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.BuildingBlocks.Application.Events;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Infrastructure.Observability;
using PersonalFinance.Infrastructure.Persistence;

namespace PersonalFinance.Infrastructure.Messaging;

/// <summary>
/// Saves the command's changes and delivers the domain events they raised to their handlers, in process and
/// inside the command's transaction. A handler may change more aggregates and raise more events, so this
/// repeats until nothing is left. If a handler throws, the whole command is rolled back.
/// </summary>
internal sealed class DomainEventDispatcher(AppDbContext db, IServiceProvider services)
{
    // A reaction that keeps raising events is a cycle between handlers, not a long chain.
    private const int MaxRounds = 10;

    private static readonly ConcurrentDictionary<Type, Publisher> Publishers = new();

    public async Task SaveAndDispatchAsync(CancellationToken cancellationToken)
    {
        for (var round = 0; ; round++)
        {
            var pending = TakePendingEvents();

            // Saved before the handlers run, so their queries see what the command (or the previous round) wrote.
            await db.SaveChangesAsync(cancellationToken);
            if (pending.Count == 0)
            {
                return;
            }

            if (round == MaxRounds)
            {
                throw new InvalidOperationException(
                    $"Domain events are still being raised after {MaxRounds} rounds; the last one was {pending[^1].GetType().Name}. Two handlers are probably triggering each other.");
            }

            foreach (var domainEvent in pending)
            {
                var publisher = Publishers.GetOrAdd(domainEvent.GetType(),
                    static eventType => (Publisher)Activator.CreateInstance(typeof(Publisher<>).MakeGenericType(eventType))!);
                await publisher.PublishAsync(services, domainEvent, cancellationToken);
            }
        }
    }

    private List<IDomainEvent> TakePendingEvents()
    {
        var aggregates = db.ChangeTracker.Entries<IHasDomainEvents>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();
        var pending = aggregates.SelectMany(aggregate => aggregate.DomainEvents).ToList();
        aggregates.ForEach(aggregate => aggregate.ClearDomainEvents());
        return pending;
    }

    private abstract class Publisher
    {
        public abstract Task PublishAsync(IServiceProvider services, IDomainEvent domainEvent, CancellationToken cancellationToken);
    }

    private sealed class Publisher<TEvent> : Publisher where TEvent : IDomainEvent
    {
        public override async Task PublishAsync(IServiceProvider services, IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
            {
                using var activity = Telemetry.ActivitySource.StartActivity($"{UseCase.ModuleOf(handler.GetType())}.{handler.GetType().Name}");
                activity?.SetTag("domain_event", typeof(TEvent).Name);
                await handler.HandleAsync((TEvent)domainEvent, cancellationToken);
            }
        }
    }
}
