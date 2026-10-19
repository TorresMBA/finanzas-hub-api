using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Modules.Ledger.Application.ExchangeRates;
using PersonalFinance.Modules.Ledger.Application.Transactions;
using PersonalFinance.Modules.Ledger.Application.Transfers;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Api.Controllers.Ledger;

public sealed class PostTransferRequest
{
    [Required]
    public Guid? FromAccountId { get; init; }

    [Required]
    public Guid? ToAccountId { get; init; }

    /// <summary>What leaves the origin account, in its currency.</summary>
    [Required]
    public decimal? Amount { get; init; }

    /// <summary>What arrives, in the currency of the destination. Required when the two currencies differ.</summary>
    public decimal? DestinationAmount { get; init; }

    [Required]
    public DateOnly? OccurredOn { get; init; }

    /// <summary>"Transferencia" when left out.</summary>
    [StringLength(Operation.MaxDescriptionLength)]
    public string? Description { get; init; }

    [StringLength(Operation.MaxNoteLength)]
    public string? Note { get; init; }

    /// <summary>
    /// What the bank charged apart for the transfer, in the currency of the origin. It is registered as an expense of the
    /// origin account in "Comisiones" and voided with the transfer. What the exchange itself cost is already in the amounts.
    /// </summary>
    public decimal? Fee { get; init; }
}

/// <summary>Money moving between two accounts of the signed-in person. It is listed, read and voided as a transaction.</summary>
[Tags("Ledger")]
[Route("api/v1/workspaces/{workspaceId:guid}/transfers")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class TransfersController : ApiControllerBase
{
    /// <summary>Registers a transfer: one movement out and one in. It is neither an income nor an expense, and net worth does not change.</summary>
    [HttpPost(Name = "PostTransfer")]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Post(
        Guid workspaceId, PostTransferRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] ICommandHandler<PostTransferCommand, TransactionResponse> handler, CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem;
        }

        var command = new PostTransferCommand(
            request.FromAccountId!.Value, request.ToAccountId!.Value, request.Amount!.Value, request.DestinationAmount, request.OccurredOn!.Value,
            request.Description, request.Note, key, request.Fee);
        return ToActionResult(
            await handler.HandleAsync(command, cancellationToken),
            transfer => Created($"/api/v1/workspaces/{workspaceId}/transactions/{transfer.Id}", transfer));
    }
}

public sealed class SetExchangeRateRequest
{
    /// <summary>Units of the base currency of the workspace for one unit of the currency.</summary>
    [Required]
    public decimal? Rate { get; init; }

    /// <summary>The first day the rate applies. Today when left out.</summary>
    public DateOnly? AsOf { get; init; }
}

/// <summary>
/// The reference exchange rates of the signed-in person, towards the base currency of the workspace. They value
/// balances and reports; they never move money.
/// </summary>
[Tags("Ledger")]
[Route("api/v1/workspaces/{workspaceId:guid}/exchange-rates")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class ExchangeRatesController : ApiControllerBase
{
    /// <summary>The rate in force for each currency that has one.</summary>
    [HttpGet(Name = "ListExchangeRates")]
    [ProducesResponseType<IReadOnlyList<ExchangeRateResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid workspaceId, [FromServices] IQueryHandler<ListExchangeRatesQuery, IReadOnlyList<ExchangeRateResponse>> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ListExchangeRatesQuery(), cancellationToken), Ok);

    /// <summary>States the rate of a currency from a day on. Giving it again for the same day replaces it.</summary>
    [HttpPut("{currency:length(3)}", Name = "SetExchangeRate")]
    [ProducesResponseType<ExchangeRateResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Set(
        Guid workspaceId, string currency, SetExchangeRateRequest request, [FromServices] ICommandHandler<SetExchangeRateCommand, ExchangeRateResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new SetExchangeRateCommand(currency, request.Rate!.Value, request.AsOf), cancellationToken), Ok);
}
