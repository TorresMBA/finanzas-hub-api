using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Modules.Sample.Application.CreateNote;
using PersonalFinance.Modules.Sample.Application.GetNotes;
using PersonalFinance.Modules.Sample.Domain;

namespace PersonalFinance.Api.Controllers.Sample;

public sealed class CreateNoteRequest
{
    [Required, StringLength(Note.MaxTitleLength, MinimumLength = 1)]
    public string Title { get; init; } = string.Empty;
}

[Tags("Sample")]
[Route("api/v1/workspaces/{workspaceId:guid}/sample-notes")]
public sealed class NotesController : ApiControllerBase
{
    [HttpPost(Name = "CreateSampleNote")]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Create(Guid workspaceId, CreateNoteRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] ICommandHandler<CreateNoteCommand, NoteResponse> handler, CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem;
        }

        var result = await handler.HandleAsync(new CreateNoteCommand(request.Title, key), cancellationToken);
        return ToActionResult(result, note => Created($"/api/v1/workspaces/{workspaceId}/sample-notes/{note.Id}", note));
    }

    [HttpGet(Name = "ListSampleNotes")]
    [ProducesResponseType<IReadOnlyList<NoteResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> List(
        Guid workspaceId, [FromServices] IQueryHandler<GetNotesQuery, IReadOnlyList<NoteResponse>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetNotesQuery(), cancellationToken);
        return ToActionResult(result, Ok);
    }
}
