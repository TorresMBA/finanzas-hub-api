using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Sample.Application.Abstractions;
using PersonalFinance.Modules.Sample.Domain;

namespace PersonalFinance.Modules.Sample.Application.CreateNote;

public sealed record NoteResponse(Guid Id, string Title, DateTimeOffset CreatedAt);

public sealed record CreateNoteCommand(string Title, string IdempotencyKey) : ICommand<NoteResponse>, IIdempotentCommand;

internal sealed class CreateNoteHandler(
    ISampleDb db, ICurrentUser currentUser, IWorkspaceContext workspace, IAuditLog audit, IClock clock)
    : ICommandHandler<CreateNoteCommand, NoteResponse>
{
    public Task<Result<NoteResponse>> HandleAsync(CreateNoteCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || workspace.WorkspaceId is not { } workspaceId)
        {
            return Task.FromResult<Result<NoteResponse>>(Error.Unauthorized("auth.unauthenticated", "Authentication is required."));
        }

        var created = Note.Create(workspaceId, userId, command.Title, clock.UtcNow);
        if (created.IsFailure)
        {
            return Task.FromResult<Result<NoteResponse>>(created.Error);
        }

        var note = created.Value;
        db.Notes.Add(note);
        audit.Record(new AuditEntry("sample", "sample.note.created", "note", note.Id)
        {
            OwnerUserId = userId,
            After = new { note.Title }
        });

        return Task.FromResult<Result<NoteResponse>>(new NoteResponse(note.Id, note.Title, note.CreatedAt));
    }
}
