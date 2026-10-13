using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.BuildingBlocks.Application.Events;

/// <summary>
/// Reacts to something that happened in an aggregate. It runs in process, inside the transaction of the command
/// that caused it and after that command's changes were saved, so it can query them. If it throws, the command
/// is rolled back. It must not call anything outside the database: queue the work in a table instead.
/// </summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
