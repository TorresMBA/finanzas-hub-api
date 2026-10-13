using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Events;
using PersonalFinance.Modules.Sample.Application.Abstractions;
using PersonalFinance.Modules.Sample.Domain;

namespace PersonalFinance.Modules.Sample.Application.Events;

/// <summary>Reaction to a new note. Its write is saved in the same transaction as the note.</summary>
internal sealed class RecordNoteReceiptHandler(ISampleDb db, IClock clock) : IDomainEventHandler<NoteCreated>
{
    public Task HandleAsync(NoteCreated domainEvent, CancellationToken cancellationToken)
    {
        db.NoteReceipts.Add(new NoteReceipt(domainEvent.NoteId, clock.UtcNow));
        return Task.CompletedTask;
    }
}
