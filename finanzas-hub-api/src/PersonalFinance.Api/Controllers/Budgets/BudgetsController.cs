using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Budgets.Application;

namespace PersonalFinance.Api.Controllers.Budgets;

public sealed class CreateBudgetRequest
{
    /// <summary>An expense category of the first level. Its subcategories count towards the limit.</summary>
    [Required]
    public Guid? CategoryId { get; init; }

    /// <summary>The monthly limit, in the base currency of the workspace.</summary>
    [Required]
    public decimal? Amount { get; init; }

    /// <summary>75, 90 or 100.</summary>
    public int AlertThresholdPercent { get; init; } = 90;

    /// <summary>A payment that is the same every month, like rent.</summary>
    public bool IsFixed { get; init; }
}

public sealed class AdjustBudgetRequest
{
    [Required]
    public decimal? Amount { get; init; }

    /// <summary>75, 90 or 100.</summary>
    public int AlertThresholdPercent { get; init; } = 90;

    public bool IsFixed { get; init; }
}

/// <summary>The budgets of the signed-in person in this workspace. Nobody else's are reachable from here.</summary>
[Tags("Budgets")]
[Route("api/v1/workspaces/{workspaceId:guid}/budgets")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class BudgetsController : ApiControllerBase
{
    /// <summary>The budgets that apply to a month, each with what was spent and how it is going.</summary>
    /// <param name="month">As 2026-10. The current month when left out.</param>
    [HttpGet(Name = "ListBudgets")]
    [ProducesResponseType<BudgetsResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        Guid workspaceId, [FromQuery] string? month, [FromServices] IQueryHandler<GetBudgetsQuery, BudgetsResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetBudgetsQuery(month), cancellationToken), Ok);

    /// <summary>One budget in a month, with the expenses that count towards it and what was spent each day.</summary>
    /// <param name="month">As 2026-10. The current month when left out.</param>
    [HttpGet("{budgetId:guid}", Name = "GetBudget")]
    [ProducesResponseType<BudgetDetailResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(
        Guid workspaceId, Guid budgetId, [FromQuery] string? month, [FromServices] IQueryHandler<GetBudgetDetailQuery, BudgetDetailResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetBudgetDetailQuery(budgetId, month), cancellationToken), Ok);

    /// <summary>Sets a monthly limit for a category, from the current month on.</summary>
    [HttpPost(Name = "CreateBudget")]
    [ProducesResponseType<BudgetResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        Guid workspaceId, CreateBudgetRequest request, [FromServices] ICommandHandler<CreateBudgetCommand, BudgetResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(
            await handler.HandleAsync(
                new CreateBudgetCommand(request.CategoryId!.Value, request.Amount!.Value, request.AlertThresholdPercent, request.IsFixed), cancellationToken),
            budget => Created($"/api/v1/workspaces/{workspaceId}/budgets/{budget.Id}", budget));

    /// <summary>Changes the limit from the current month on. Past months keep theirs, so the id in the answer may be a new one.</summary>
    [HttpPatch("{budgetId:guid}", Name = "AdjustBudget")]
    [ProducesResponseType<BudgetResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Adjust(
        Guid workspaceId, Guid budgetId, AdjustBudgetRequest request, [FromServices] ICommandHandler<AdjustBudgetCommand, BudgetResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(
            await handler.HandleAsync(new AdjustBudgetCommand(budgetId, request.Amount!.Value, request.AlertThresholdPercent, request.IsFixed), cancellationToken), Ok);

    /// <summary>Stops the budget from the current month on. Past months keep it.</summary>
    [HttpDelete("{budgetId:guid}", Name = "EndBudget")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> End(
        Guid workspaceId, Guid budgetId, [FromServices] ICommandHandler<EndBudgetCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new EndBudgetCommand(budgetId), cancellationToken), _ => NoContent());
}
