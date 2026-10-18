using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Goals.Application;
using PersonalFinance.Modules.Goals.Domain;

namespace PersonalFinance.Api.Controllers.Goals;

public sealed class CreateGoalRequest
{
    [Required, StringLength(Goal.MaxNameLength, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public decimal? TargetAmount { get; init; }

    /// <summary>Three-letter code. That of the workspace when left out.</summary>
    [StringLength(3, MinimumLength = 3)]
    public string? Currency { get; init; }

    public DateOnly? TargetDate { get; init; }

    /// <summary>Other members of the workspace who save for it too.</summary>
    public IReadOnlyList<Guid>? ParticipantUserIds { get; init; }
}

public sealed class UpdateGoalRequest
{
    [Required, StringLength(Goal.MaxNameLength, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public decimal? TargetAmount { get; init; }

    public DateOnly? TargetDate { get; init; }
}

public sealed class AddGoalParticipantRequest
{
    [Required]
    public Guid? UserId { get; init; }
}

public sealed class GoalMoneyRequest
{
    /// <summary>Positive, in the currency of the goal.</summary>
    [Required]
    public decimal? Amount { get; init; }

    /// <summary>An account of the person asking, in the currency of the goal: where the money comes from or goes back to.</summary>
    [Required]
    public Guid? AccountId { get; init; }

    /// <summary>Today when left out.</summary>
    public DateOnly? OccurredOn { get; init; }
}

public sealed class GoalSpendRequest
{
    /// <summary>Positive, in the currency of the goal; at most what the person asking put in.</summary>
    [Required]
    public decimal? Amount { get; init; }

    /// <summary>The account of the person asking that pays, in the currency of the goal.</summary>
    [Required]
    public Guid? AccountId { get; init; }

    /// <summary>An expense category of the person asking.</summary>
    [Required]
    public Guid? CategoryId { get; init; }

    /// <summary>What was bought. The name of the goal when left out.</summary>
    [StringLength(200)]
    public string? Description { get; init; }

    /// <summary>Today when left out.</summary>
    public DateOnly? OccurredOn { get; init; }
}

/// <summary>
/// The goals the signed-in person takes part in. A goal is shared by participation: a member of the workspace
/// who is not one of its participants gets a 404, as if it did not exist.
/// </summary>
[Tags("Goals")]
[Route("api/v1/workspaces/{workspaceId:guid}/goals")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class GoalsController : ApiControllerBase
{
    [HttpGet(Name = "ListGoals")]
    [ProducesResponseType<GoalsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid workspaceId, [FromServices] IQueryHandler<ListGoalsQuery, GoalsResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ListGoalsQuery(), cancellationToken), Ok);

    /// <summary>The goal with the contributions of every participant. It never says from which account anyone contributed.</summary>
    [HttpGet("{goalId:guid}", Name = "GetGoal")]
    [ProducesResponseType<GoalDetailResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        Guid workspaceId, Guid goalId, [FromServices] IQueryHandler<GetGoalDetailQuery, GoalDetailResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetGoalDetailQuery(goalId), cancellationToken), Ok);

    [HttpPost(Name = "CreateGoal")]
    [ProducesResponseType<GoalResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        Guid workspaceId, CreateGoalRequest request, [FromServices] ICommandHandler<CreateGoalCommand, GoalResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(
            await handler.HandleAsync(
                new CreateGoalCommand(request.Name, request.TargetAmount!.Value, request.Currency, request.TargetDate, request.ParticipantUserIds), cancellationToken),
            goal => Created($"/api/v1/workspaces/{workspaceId}/goals/{goal.Id}", goal));

    /// <summary>Only the owner of the goal.</summary>
    [HttpPatch("{goalId:guid}", Name = "UpdateGoal")]
    [ProducesResponseType<GoalResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(
        Guid workspaceId, Guid goalId, UpdateGoalRequest request, [FromServices] ICommandHandler<UpdateGoalCommand, GoalResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new UpdateGoalCommand(goalId, request.Name, request.TargetAmount!.Value, request.TargetDate), cancellationToken), Ok);

    /// <summary>Only the owner of the goal, and only members of the workspace.</summary>
    [HttpPost("{goalId:guid}/participants", Name = "AddGoalParticipant")]
    [ProducesResponseType<GoalResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AddParticipant(
        Guid workspaceId, Guid goalId, AddGoalParticipantRequest request, [FromServices] ICommandHandler<AddGoalParticipantCommand, GoalResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new AddGoalParticipantCommand(goalId, request.UserId!.Value), cancellationToken), Ok);

    /// <summary>The owner removes someone, or someone leaves by naming themselves. Whoever still has money in the goal withdraws it first.</summary>
    [HttpDelete("{goalId:guid}/participants/{userId:guid}", Name = "RemoveGoalParticipant")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RemoveParticipant(
        Guid workspaceId, Guid goalId, Guid userId, [FromServices] ICommandHandler<RemoveGoalParticipantCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new RemoveGoalParticipantCommand(goalId, userId), cancellationToken), _ => NoContent());

    /// <summary>Moves money from an account of the person asking to their fund for this goal. It is a transfer, not an expense.</summary>
    [HttpPost("{goalId:guid}/contributions", Name = "ContributeToGoal")]
    [ProducesResponseType<GoalResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Contribute(
        Guid workspaceId, Guid goalId, GoalMoneyRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] ICommandHandler<ContributeToGoalCommand, GoalResponse> handler, CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem;
        }

        return ToActionResult(
            await handler.HandleAsync(new ContributeToGoalCommand(goalId, request.Amount!.Value, request.AccountId!.Value, request.OccurredOn, key), cancellationToken),
            goal => Created($"/api/v1/workspaces/{workspaceId}/goals/{goal.Id}", goal));
    }

    /// <summary>Voids a contribution of the person asking, with its transfer. Not possible once that money was withdrawn or spent.</summary>
    [HttpPost("{goalId:guid}/contributions/{contributionId:guid}/void", Name = "VoidGoalContribution")]
    [ProducesResponseType<GoalResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> VoidContribution(
        Guid workspaceId, Guid goalId, Guid contributionId, [FromServices] ICommandHandler<VoidGoalContributionCommand, GoalResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new VoidGoalContributionCommand(goalId, contributionId), cancellationToken), Ok);

    /// <summary>Takes back part of what the person asking put in, to one of their accounts.</summary>
    [HttpPost("{goalId:guid}/withdrawals", Name = "WithdrawFromGoal")]
    [ProducesResponseType<GoalResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Withdraw(
        Guid workspaceId, Guid goalId, GoalMoneyRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] ICommandHandler<WithdrawFromGoalCommand, GoalResponse> handler, CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem;
        }

        return ToActionResult(
            await handler.HandleAsync(new WithdrawFromGoalCommand(goalId, request.Amount!.Value, request.AccountId!.Value, request.OccurredOn, key), cancellationToken),
            goal => Created($"/api/v1/workspaces/{workspaceId}/goals/{goal.Id}", goal));
    }

    /// <summary>
    /// Spends what the person asking saved for the goal: it comes back to one of their accounts and is registered there
    /// as an expense of that day, marked as paid with the goal.
    /// </summary>
    [HttpPost("{goalId:guid}/spend", Name = "SpendFromGoal")]
    [ProducesResponseType<GoalResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Spend(
        Guid workspaceId, Guid goalId, GoalSpendRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] ICommandHandler<SpendFromGoalCommand, GoalResponse> handler, CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem;
        }

        return ToActionResult(
            await handler.HandleAsync(
                new SpendFromGoalCommand(goalId, request.Amount!.Value, request.AccountId!.Value, request.CategoryId!.Value, request.Description, request.OccurredOn, key),
                cancellationToken),
            goal => Created($"/api/v1/workspaces/{workspaceId}/goals/{goal.Id}", goal));
    }

    /// <summary>Archives a goal that is done. Only its owner. Nothing moves: each one withdraws what is theirs.</summary>
    [HttpPost("{goalId:guid}/archive", Name = "ArchiveGoal")]
    [ProducesResponseType<GoalResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Archive(
        Guid workspaceId, Guid goalId, [FromServices] ICommandHandler<CloseGoalCommand, GoalResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new CloseGoalCommand(goalId, false), cancellationToken), Ok);

    /// <summary>Gives a goal up. Only its owner. Nothing moves: each one withdraws what is theirs.</summary>
    [HttpPost("{goalId:guid}/cancel", Name = "CancelGoal")]
    [ProducesResponseType<GoalResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Cancel(
        Guid workspaceId, Guid goalId, [FromServices] ICommandHandler<CloseGoalCommand, GoalResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new CloseGoalCommand(goalId, true), cancellationToken), Ok);
}
