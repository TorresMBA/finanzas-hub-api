using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Sample.Application.Abstractions;
using PersonalFinance.Modules.Sample.Application.CreateNote;

namespace PersonalFinance.Modules.Sample.Application.GetNotes;

public sealed record GetNotesQuery : IQuery<IReadOnlyList<NoteResponse>>;

internal sealed class GetNotesHandler(ISampleDb db) : IQueryHandler<GetNotesQuery, IReadOnlyList<NoteResponse>>
{
    public async Task<Result<IReadOnlyList<NoteResponse>>> HandleAsync(GetNotesQuery query, CancellationToken cancellationToken)
    {
        // No owner condition here on purpose: the context's global filter already limits notes to the current user.
        var notes = await db.Notes.AsNoTracking()
            .OrderByDescending(note => note.CreatedAt)
            .Select(note => new NoteResponse(note.Id, note.Title, note.CreatedAt))
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<NoteResponse>>(notes);
    }
}
