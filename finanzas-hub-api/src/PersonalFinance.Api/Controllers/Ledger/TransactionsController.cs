using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Modules.Ledger.Application.Reports;
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

    /// <summary>
    /// The price in the currency something was sold in, when the account was charged in its own: US$ 15.49 of a
    /// subscription paid with a card in soles. Sent with <see cref="OriginalCurrency"/>; only a reference.
    /// </summary>
    public decimal? OriginalAmount { get; init; }

    [StringLength(3, MinimumLength = 3)]
    public string? OriginalCurrency { get; init; }

    /// <summary>
    /// Units of the base currency per unit of the account's, when the person wants this rate instead of the one of
    /// their table for that day. Only for an account in a currency other than the base one.
    /// </summary>
    public decimal? FxRate { get; init; }

    internal ForeignTerms Foreign => new(OriginalAmount, OriginalCurrency, FxRate);
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

/// <summary>The movements of the signed-in person in this workspace: income, expenses, transfers and what the application registers on an account.</summary>
[Tags("Ledger")]
[Route("api/v1/workspaces/{workspaceId:guid}/transactions")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class TransactionsController : ApiControllerBase
{
    /// <summary>Newest first, by pages. The totals cover everything the filter matches, per currency and in the base currency.</summary>
    [HttpGet(Name = "ListTransactions")]
    [ProducesResponseType<TransactionPageResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        Guid workspaceId, [FromQuery] OperationKind? type, [FromQuery] OperationStatus? status, [FromQuery] Guid? accountId, [FromQuery] Guid? categoryId,
        [FromQuery] string? tag, [FromQuery(Name = "q")] string? text, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? cursor,
        [FromQuery] int? limit, [FromQuery] string? currency, [FromServices] IQueryHandler<ListTransactionsQuery, TransactionPageResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(
            await handler.HandleAsync(new ListTransactionsQuery(type, status, accountId, categoryId, tag, text, from, to, cursor, limit, currency), cancellationToken), Ok);

    /// <summary>What each category moved in a period, in the base currency. Transfers count for no category.</summary>
    [HttpGet("category-totals", Name = "GetCategoryTotals")]
    [ProducesResponseType<CategoryTotalsResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CategoryTotals(
        Guid workspaceId, [FromQuery, Required] DateOnly? from, [FromQuery, Required] DateOnly? to,
        [FromServices] IQueryHandler<GetCategoryTotalsQuery, CategoryTotalsResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetCategoryTotalsQuery(from!.Value, to!.Value), cancellationToken), Ok);

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
            request.Merchant, request.Note, request.Tags, request.Pending, key, request.Foreign);
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
            request.Description, request.Merchant, request.Note, request.Tags, request.Foreign);
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
