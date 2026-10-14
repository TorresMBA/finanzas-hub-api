using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Integrations.Application;
using PersonalFinance.Modules.Integrations.Domain;

namespace PersonalFinance.Api.Controllers.Integrations;

public sealed class MapImportAccountRequest
{
    /// <summary>One of the person's accounts.</summary>
    public Guid? AccountId { get; init; }

    /// <summary>Instead of an account: open a new one of this kind, named as in the file, when the import is confirmed.</summary>
    public NewAccountType? CreateAs { get; init; }
}

public sealed class MapImportCategoryRequest
{
    /// <summary>One of the person's categories.</summary>
    public Guid? CategoryId { get; init; }

    /// <summary>Instead of a category: create one named as in the file when the import is confirmed.</summary>
    public bool Create { get; init; }
}

public sealed class ReviewImportRecordRequest
{
    /// <summary>Left out, the category of the line stays as it is.</summary>
    public Guid? CategoryId { get; init; }

    /// <summary>Left out, it stays as it is. False on a duplicate says it is another movement and brings it in.</summary>
    public bool? Excluded { get; init; }
}

public sealed class SaveCategorizationRuleRequest
{
    /// <summary>The text a description must contain. Accents and upper case do not matter.</summary>
    [Required]
    public string? Pattern { get; init; }

    [Required]
    public Guid? CategoryId { get; init; }

    public bool IsActive { get; init; } = true;
}

/// <summary>
/// Files of movements the signed-in person imports into their own ledger: a bank statement (CSV or OFX) or
/// the export of Bluecoins. Nobody else's imports are reachable from here.
/// </summary>
[Tags("Imports")]
[Route("api/v1/workspaces/{workspaceId:guid}")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class ImportsController : ApiControllerBase
{
    /// <summary>The latest imports, newest first.</summary>
    [HttpGet("imports", Name = "ListImports")]
    [ProducesResponseType<IReadOnlyList<ImportSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid workspaceId, [FromServices] IQueryHandler<ListImportsQuery, IReadOnlyList<ImportSummaryResponse>> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ListImportsQuery(), cancellationToken), Ok);

    /// <summary>
    /// Reads a file and leaves its lines waiting for review; nothing enters the ledger yet. The body is the file
    /// itself, at most 10 MB. An OFX statement, a Bluecoins export and a table from a bank are told apart by their content.
    /// </summary>
    /// <param name="fileName">The name of the file, to recognise the import later.</param>
    /// <param name="accountId">The account a bank statement is of. A Bluecoins export brings its own accounts.</param>
    [HttpPost("imports", Name = "UploadImport")]
    [Consumes("application/octet-stream", "text/csv", "text/plain")]
    [RequestSizeLimit(ImportSession.MaxFileBytes + 1024)]
    [ProducesResponseType<ImportDetailResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(
        Guid workspaceId, [FromQuery] string? fileName, [FromQuery] Guid? accountId,
        [FromServices] ICommandHandler<UploadImportCommand, ImportDetailResponse> handler, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream();
        await Request.Body.CopyToAsync(content, cancellationToken);
        return ToActionResult(
            await handler.HandleAsync(new UploadImportCommand(fileName, content.ToArray(), accountId), cancellationToken),
            detail => Created($"/api/v1/workspaces/{workspaceId}/imports/{detail.Import.Id}", detail));
    }

    /// <summary>An import with its accounts, its categories and one page of its lines.</summary>
    /// <param name="filter">pending, ready, duplicate, excluded, invalid or imported. Every line when left out.</param>
    /// <param name="offset">How many lines of the filter to skip. A page has 100.</param>
    [HttpGet("imports/{importId:guid}", Name = "GetImport")]
    [ProducesResponseType<ImportDetailResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        Guid workspaceId, Guid importId, [FromQuery] string? filter, [FromQuery] int offset,
        [FromServices] IQueryHandler<GetImportQuery, ImportDetailResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetImportQuery(importId, filter, offset), cancellationToken), Ok);

    /// <summary>Says which of the person's accounts an account of the file is, or that a new one is to be opened for it.</summary>
    [HttpPut("imports/{importId:guid}/accounts/{mappingId:guid}", Name = "MapImportAccount")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MapAccount(
        Guid workspaceId, Guid importId, Guid mappingId, MapImportAccountRequest request, [FromServices] ICommandHandler<MapImportAccountCommand, Unit> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new MapImportAccountCommand(importId, mappingId, request.AccountId, request.CreateAs), cancellationToken), _ => NoContent());

    /// <summary>Says which of the person's categories a category of the file is, or that it is to be created.</summary>
    [HttpPut("imports/{importId:guid}/categories/{mappingId:guid}", Name = "MapImportCategory")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> MapCategory(
        Guid workspaceId, Guid importId, Guid mappingId, MapImportCategoryRequest request, [FromServices] ICommandHandler<MapImportCategoryCommand, Unit> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new MapImportCategoryCommand(importId, mappingId, request.CategoryId, request.Create), cancellationToken), _ => NoContent());

    /// <summary>Changes the category of one line, leaves it out or brings it back in.</summary>
    [HttpPatch("imports/{importId:guid}/records/{recordId:guid}", Name = "ReviewImportRecord")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ReviewRecord(
        Guid workspaceId, Guid importId, Guid recordId, ReviewImportRecordRequest request, [FromServices] ICommandHandler<ReviewImportRecordCommand, Unit> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(
            await handler.HandleAsync(new ReviewImportRecordCommand(importId, recordId, request.CategoryId, request.Excluded), cancellationToken), _ => NoContent());

    /// <summary>
    /// Registers in the ledger every line that is ready, all of them or none. Lines that still need an account or
    /// a category stop it; duplicates and excluded lines stay out. An import is confirmed once.
    /// </summary>
    [HttpPost("imports/{importId:guid}/commit", Name = "CommitImport")]
    [ProducesResponseType<ImportDetailResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Commit(
        Guid workspaceId, Guid importId, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] ICommandHandler<CommitImportCommand, ImportDetailResponse> handler, CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem;
        }

        return ToActionResult(await handler.HandleAsync(new CommitImportCommand(importId, key), cancellationToken), Ok);
    }

    /// <summary>Throws away an import that was not confirmed.</summary>
    [HttpDelete("imports/{importId:guid}", Name = "DiscardImport")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Discard(
        Guid workspaceId, Guid importId, [FromServices] ICommandHandler<DiscardImportCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new DiscardImportCommand(importId), cancellationToken), _ => NoContent());

    /// <summary>The rules that classify the lines of a file by what their description contains.</summary>
    [HttpGet("categorization-rules", Name = "ListCategorizationRules")]
    [ProducesResponseType<IReadOnlyList<CategorizationRuleResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRules(
        Guid workspaceId, [FromServices] IQueryHandler<ListCategorizationRulesQuery, IReadOnlyList<CategorizationRuleResponse>> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ListCategorizationRulesQuery(), cancellationToken), Ok);

    [HttpPost("categorization-rules", Name = "CreateCategorizationRule")]
    [ProducesResponseType<CategorizationRuleResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateRule(
        Guid workspaceId, SaveCategorizationRuleRequest request, [FromServices] ICommandHandler<SaveCategorizationRuleCommand, CategorizationRuleResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(
            await handler.HandleAsync(new SaveCategorizationRuleCommand(null, request.Pattern!, request.CategoryId!.Value, true), cancellationToken),
            rule => Created($"/api/v1/workspaces/{workspaceId}/categorization-rules/{rule.Id}", rule));

    /// <summary>Changes the text or the category of a rule, or switches it on and off.</summary>
    [HttpPut("categorization-rules/{ruleId:guid}", Name = "UpdateCategorizationRule")]
    [ProducesResponseType<CategorizationRuleResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateRule(
        Guid workspaceId, Guid ruleId, SaveCategorizationRuleRequest request,
        [FromServices] ICommandHandler<SaveCategorizationRuleCommand, CategorizationRuleResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(
            await handler.HandleAsync(new SaveCategorizationRuleCommand(ruleId, request.Pattern!, request.CategoryId!.Value, request.IsActive), cancellationToken), Ok);

    [HttpDelete("categorization-rules/{ruleId:guid}", Name = "DeleteCategorizationRule")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteRule(
        Guid workspaceId, Guid ruleId, [FromServices] ICommandHandler<DeleteCategorizationRuleCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new DeleteCategorizationRuleCommand(ruleId), cancellationToken), _ => NoContent());
}
