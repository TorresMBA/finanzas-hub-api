using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Modules.Investments.Application;
using PersonalFinance.Modules.Investments.Domain;

namespace PersonalFinance.Api.Controllers.Investments;

public sealed class OpenHoldingRequest
{
    [Required]
    public InvestmentType? Type { get; init; }

    [Required, StringLength(Holding.MaxNameLength, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [StringLength(Holding.MaxTickerLength)]
    public string? Ticker { get; init; }

    [StringLength(Holding.MaxInstitutionLength)]
    public string? Institution { get; init; }

    /// <summary>What goes in, in the currency of the account it comes from, which becomes that of the investment.</summary>
    [Required]
    public decimal? Amount { get; init; }

    /// <summary>Units bought, for shares, ETFs and crypto.</summary>
    public decimal? Quantity { get; init; }

    [Required]
    public DateOnly? OpenedOn { get; init; }

    [Required]
    public Guid? FromAccountId { get; init; }

    /// <summary>The TEA of a term deposit, in percent: 6.5 is 6.5 % a year.</summary>
    public decimal? AnnualRate { get; init; }

    /// <summary>The term of a term deposit, in days.</summary>
    public int? TermDays { get; init; }
}

public sealed class HoldingMoneyRequest
{
    [Required]
    public decimal? Amount { get; init; }

    /// <summary>Units bought or sold, for shares, ETFs and crypto.</summary>
    public decimal? Quantity { get; init; }

    [Required]
    public DateOnly? OccurredOn { get; init; }

    /// <summary>Where the money comes from (a contribution) or goes to (a redemption).</summary>
    [Required]
    public Guid? AccountId { get; init; }
}

public sealed class RecordValuationRequest
{
    [Required]
    public DateOnly? OnDate { get; init; }

    /// <summary>What it is worth. Or, for shares, ETFs and crypto, the price of one unit.</summary>
    public decimal? Value { get; init; }

    public decimal? UnitPrice { get; init; }
}

/// <summary>The investments of the signed-in person. Only that person's: an investment of someone else is not found.</summary>
[Tags("Investments")]
[Route("api/v1/workspaces/{workspaceId:guid}")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class InvestmentsController : ApiControllerBase
{
    /// <summary>What the portfolio is worth in the base currency, by type and investment by investment.</summary>
    /// <param name="type">Only the investments of this type.</param>
    [HttpGet("investments/summary", Name = "GetInvestmentSummary")]
    [ProducesResponseType<InvestmentSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(
        Guid workspaceId, [FromQuery] InvestmentType? type, [FromServices] IQueryHandler<GetInvestmentSummaryQuery, InvestmentSummaryResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetInvestmentSummaryQuery(type), cancellationToken), Ok);

    [HttpGet("holdings/{holdingId:guid}", Name = "GetHolding")]
    [ProducesResponseType<HoldingDetailResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        Guid workspaceId, Guid holdingId, [FromServices] IQueryHandler<GetHoldingQuery, HoldingDetailResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetHoldingQuery(holdingId), cancellationToken), Ok);

    /// <summary>Registers an investment with the money it starts with, moved from one of the person's accounts. It is a transfer, not an expense.</summary>
    [HttpPost("holdings", Name = "OpenHolding")]
    [ProducesResponseType<HoldingDetailResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Open(
        Guid workspaceId, OpenHoldingRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] ICommandHandler<OpenHoldingCommand, HoldingDetailResponse> handler, CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem;
        }

        return ToActionResult(
            await handler.HandleAsync(
                new OpenHoldingCommand(
                    request.Type!.Value, request.Name, request.Ticker, request.Institution, request.Amount!.Value, request.Quantity, request.OpenedOn!.Value,
                    request.FromAccountId!.Value, request.AnnualRate, request.TermDays, key),
                cancellationToken),
            detail => Created($"/api/v1/workspaces/{workspaceId}/holdings/{detail.Holding.Id}", detail));
    }

    /// <summary>Puts more money into the investment from one of the person's accounts.</summary>
    [HttpPost("holdings/{holdingId:guid}/contributions", Name = "ContributeToHolding")]
    [ProducesResponseType<HoldingDetailResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Contribute(
        Guid workspaceId, Guid holdingId, HoldingMoneyRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] ICommandHandler<ContributeToHoldingCommand, HoldingDetailResponse> handler, CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem;
        }

        return ToActionResult(
            await handler.HandleAsync(
                new ContributeToHoldingCommand(holdingId, request.Amount!.Value, request.Quantity, request.OccurredOn!.Value, request.AccountId!.Value, key), cancellationToken),
            Ok);
    }

    /// <summary>Redeems money into one of the person's accounts. What it earned on top of the capital enters as an income.</summary>
    [HttpPost("holdings/{holdingId:guid}/withdrawals", Name = "WithdrawFromHolding")]
    [ProducesResponseType<HoldingDetailResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Withdraw(
        Guid workspaceId, Guid holdingId, HoldingMoneyRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] ICommandHandler<WithdrawFromHoldingCommand, HoldingDetailResponse> handler, CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem;
        }

        return ToActionResult(
            await handler.HandleAsync(
                new WithdrawFromHoldingCommand(holdingId, request.Amount!.Value, request.Quantity, request.OccurredOn!.Value, request.AccountId!.Value, key), cancellationToken),
            Ok);
    }

    /// <summary>Writes down what the investment is worth today or on a day before. No money moves.</summary>
    [HttpPost("holdings/{holdingId:guid}/valuations", Name = "RecordHoldingValuation")]
    [ProducesResponseType<HoldingDetailResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Value(
        Guid workspaceId, Guid holdingId, RecordValuationRequest request, [FromServices] ICommandHandler<RecordValuationCommand, HoldingDetailResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new RecordValuationCommand(holdingId, request.OnDate!.Value, request.Value, request.UnitPrice), cancellationToken), Ok);
}
