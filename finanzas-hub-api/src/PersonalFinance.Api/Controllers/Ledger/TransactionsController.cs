using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Modules.Ledger.Application.Transactions;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Api.Controllers.Ledger;

/// <summary>What a transaction says. Posting and editing send the same fields.</summary>
public class TransactionRequest
{
    [Required]
    public TransactionType? Type { get; init; }

    [Required]
    public Guid? AccountId { get; init; }

    /// <summary>Positive, in the currency of the account.</summary>
    [Required]
    public decimal? Amount { get; init; }

    [Required]
    public Guid? CategoryId { get; init; }

    [Required]
    public DateOnly? OccurredOn { get; init; }

    [Required, StringLength(Operation.MaxDescriptionLength, MinimumLength = 1)]
    public string Description { get; init; } = string.Empty;

    [StringLength(Operation.MaxMerchantLength)]
    public string? Merchant { get; init; }

    [StringLength(Operation.MaxNoteLength)]
    public string? Note { get; init; }

    /// <summary>Names of tags. A name that is not a tag yet becomes one.</summary>
    [MaxLength(Operation.MaxTags)]
    public IReadOnlyList<string>? Tags { get; init; }
}

public sealed class PostTransactionRequest : TransactionRequest
{
    /// <summary>Registered but still to be reviewed. It counts in balances all the same.</summary>
    public bool Pending { get; init; }
}

public sealed class EditTransactionRequest : TransactionRequest;

public sealed class VoidTransactionRequest
{
    [StringLength(Operation.MaxVoidReasonLength)]
    public string? Reason { get; init; }
}

/// <summary>The income and expenses of the signed-in person in this workspace.</summary>
[Tags("Ledger")]
[Route("api/v1/workspaces/{workspaceId:guid}/transactions")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class TransactionsController : ApiControllerBase
{
    /// <summary>Newest first, by pages. The totals cover everything the filter matches, per currency.</summary>
    [HttpGet(Name = "ListTransactions")]
    [ProducesResponseType<TransactionPageResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        Guid workspaceId, [FromQuery] TransactionType? type, [FromQuery] OperationStatus? status, [FromQuery] Guid? accountId, [FromQuery] Guid? categoryId,
        [FromQuery] string? tag, [FromQuery(Name = "q")] string? text, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? cursor,
        [FromQuery] int? limit, [FromServices] IQueryHandler<ListTransactionsQuery, TransactionPageResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ListTransactionsQuery(type, status, accountId, categoryId, tag, text, from, to, cursor, limit), cancellationToken), Ok);

    [HttpGet("{transactionId:guid}", Name = "GetTransaction")]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        Guid workspaceId, Guid transactionId, [FromServices] IQueryHandler<GetTransactionQuery, TransactionResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetTransactionQuery(transactionId), cancellationToken), Ok);

    /// <summary>Registers an income or an expense. Repeating the request with the same Idempotency-Key registers it once.</summary>
    [HttpPost(Name = "PostTransaction")]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Post(
        Guid workspaceId, PostTransactionRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] ICommandHandler<PostTransactionCommand, TransactionResponse> handler, CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem;
        }

        var command = new PostTransactionCommand(
            request.Type!.Value, request.AccountId!.Value, request.Amount!.Value, request.CategoryId!.Value, request.OccurredOn!.Value, request.Description,
            request.Merchant, request.Note, request.Tags, request.Pending, key);
        return ToActionResult(
            await handler.HandleAsync(command, cancellationToken),
            transaction => Created($"/api/v1/workspaces/{workspaceId}/transactions/{transaction.Id}", transaction));
    }

    /// <summary>Replaces what the transaction says, amount, account and date included. The audit trail keeps both versions.</summary>
    [HttpPatch("{transactionId:guid}", Name = "EditTransaction")]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Edit(
        Guid workspaceId, Guid transactionId, EditTransactionRequest request,
        [FromServices] ICommandHandler<EditTransactionCommand, TransactionResponse> handler, CancellationToken cancellationToken)
    {
        var command = new EditTransactionCommand(
            transactionId, request.Type!.Value, request.AccountId!.Value, request.Amount!.Value, request.CategoryId!.Value, request.OccurredOn!.Value,
            request.Description, request.Merchant, request.Note, request.Tags);
        return ToActionResult(await handler.HandleAsync(command, cancellationToken), Ok);
    }

    [HttpPost("{transactionId:guid}/confirm", Name = "ConfirmTransaction")]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Confirm(
        Guid workspaceId, Guid transactionId, [FromServices] ICommandHandler<ConfirmTransactionCommand, TransactionResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ConfirmTransactionCommand(transactionId), cancellationToken), Ok);

    /// <summary>Takes the transaction out of every balance and total. Nothing is deleted, and voiding twice changes nothing.</summary>
    [HttpPost("{transactionId:guid}/void", Name = "VoidTransaction")]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Void(
        Guid workspaceId, Guid transactionId, VoidTransactionRequest? request, [FromServices] ICommandHandler<VoidTransactionCommand, TransactionResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new VoidTransactionCommand(transactionId, request?.Reason), cancellationToken), Ok);
}
