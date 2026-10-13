using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Sample.Domain;

/// <summary>
/// Throwaway aggregate that exercises the platform end to end: private ownership, a domain event with its
/// reaction and the audit trail. The module is removed when the first real module replaces it.
/// </summary>
public sealed class Note : AggregateRoot, IOwned
{
    public const int MaxTitleLength = 120;

    private Note() { }

    private Note(Guid id, Guid workspaceId, Guid ownerUserId, string title, DateTimeOffset createdAt) : base(id)
    {
        WorkspaceId = workspaceId;
        OwnerUserId = ownerUserId;
        Title = title;
        CreatedAt = createdAt;
    }

    public Guid WorkspaceId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public string Title { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<Note> Create(Guid workspaceId, Guid ownerUserId, string? title, DateTimeOffset now)
    {
        var normalized = title?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > MaxTitleLength)
        {
            return Error.Validation("sample.note.title_invalid", $"A note title must have between 1 and {MaxTitleLength} characters.");
        }

        var note = new Note(NewId(), workspaceId, ownerUserId, normalized, now);
        note.Raise(new NoteCreated(note.Id, workspaceId, ownerUserId));
        return note;
    }
}

public sealed record NoteCreated(Guid NoteId, Guid WorkspaceId, Guid OwnerUserId) : IDomainEvent;

/// <summary>Written by the reaction to <see cref="NoteCreated"/>; proves the reaction is saved together with the note.</summary>
public sealed class NoteReceipt
{
    private NoteReceipt()
    {
    }

    public NoteReceipt(Guid noteId, DateTimeOffset receivedAt)
    {
        NoteId = noteId;
        ReceivedAt = receivedAt;
    }

    public Guid NoteId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }
}
