using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Scheduling.Application;
using PersonalFinance.Modules.Scheduling.Domain;

namespace PersonalFinance.Api.Controllers.Scheduling;

public sealed class CreateRecurringRuleRequest
{
    [Required]
    public RuleType? Type { get; init; }

    [Required]
    public Guid? AccountId { get; init; }

    [Required]
    public Guid? CategoryId { get; init; }

    /// <summary>Positive, in the currency of the account.</summary>
    [Required]
    public decimal? Amount { get; init; }

    [Required, StringLength(RecurringRule.MaxDescriptionLength, MinimumLength = 1)]
    public string Description { get; init; } = string.Empty;

    [Required]
    public RuleFrequency? Frequency { get; init; }

    /// <summary>The first date. The following ones fall on the same day of the month.</summary>
    [Required]
    public DateOnly? StartsOn { get; init; }

    public DateOnly? EndsOn { get; init; }

    /// <summary>Automatic registers it on its date; confirm waits for the person.</summary>
    public PostingMode PostingMode { get; init; } = PostingMode.Automatic;

    /// <summary>0, 1, 3 or 7 days before. Leave it out for no reminder.</summary>
    public int? ReminderDaysBefore { get; init; }
}

public sealed class ConfirmOccurrenceRequest
{
    /// <summary>The real amount when it differs from the planned one.</summary>
    public decimal? Amount { get; init; }
}

/// <summary>The scheduled and recurring movements of the signed-in person in this workspace, and their calendar.</summary>
[Tags("Scheduling")]
[Route("api/v1/workspaces/{workspaceId:guid}")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class SchedulingController : ApiControllerBase
{
    /// <summary>What happened and what is expected between two days: movements of the ledger, dates of the rules and credit cards falling due.</summary>
    [HttpGet("calendar", Name = "GetCalendar")]
    [ProducesResponseType<CalendarResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Calendar(
        Guid workspaceId, [FromQuery, Required] DateOnly from, [FromQuery, Required] DateOnly to,
        [FromServices] IQueryHandler<GetCalendarQuery, CalendarResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetCalendarQuery(from, to), cancellationToken), Ok);

    [HttpGet("recurring-rules", Name = "ListRecurringRules")]
    [ProducesResponseType<IReadOnlyList<RecurringRuleResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid workspaceId, [FromServices] IQueryHandler<ListRecurringRulesQuery, IReadOnlyList<RecurringRuleResponse>> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ListRecurringRulesQuery(), cancellationToken), Ok);

    /// <summary>Schedules an income or an expense: once, every month or every year.</summary>
    [HttpPost("recurring-rules", Name = "CreateRecurringRule")]
    [ProducesResponseType<RecurringRuleResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        Guid workspaceId, CreateRecurringRuleRequest request, [FromServices] ICommandHandler<CreateRecurringRuleCommand, RecurringRuleResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(
            await handler.HandleAsync(
                new CreateRecurringRuleCommand(
                    request.Type!.Value, request.AccountId!.Value, request.CategoryId!.Value, request.Amount!.Value, request.Description, request.Frequency!.Value,
                    request.StartsOn!.Value, request.EndsOn, request.PostingMode, request.ReminderDaysBefore),
                cancellationToken),
            rule => Created($"/api/v1/workspaces/{workspaceId}/recurring-rules", rule));

    /// <summary>A paused rule expects nothing until it is resumed.</summary>
    [HttpPost("recurring-rules/{ruleId:guid}/pause", Name = "PauseRecurringRule")]
    [ProducesResponseType<RecurringRuleResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public Task<IActionResult> Pause(
        Guid workspaceId, Guid ruleId, [FromServices] ICommandHandler<ChangeRuleStatusCommand, RecurringRuleResponse> handler, CancellationToken cancellationToken) =>
        ChangeAsync(ruleId, RuleStatusChange.Pause, handler, cancellationToken);

    [HttpPost("recurring-rules/{ruleId:guid}/resume", Name = "ResumeRecurringRule")]
    [ProducesResponseType<RecurringRuleResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public Task<IActionResult> Resume(
        Guid workspaceId, Guid ruleId, [FromServices] ICommandHandler<ChangeRuleStatusCommand, RecurringRuleResponse> handler, CancellationToken cancellationToken) =>
        ChangeAsync(ruleId, RuleStatusChange.Resume, handler, cancellationToken);

    /// <summary>Stops the rule for good. What it already registered stays.</summary>
    [HttpPost("recurring-rules/{ruleId:guid}/end", Name = "EndRecurringRule")]
    [ProducesResponseType<RecurringRuleResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public Task<IActionResult> End(
        Guid workspaceId, Guid ruleId, [FromServices] ICommandHandler<ChangeRuleStatusCommand, RecurringRuleResponse> handler, CancellationToken cancellationToken) =>
        ChangeAsync(ruleId, RuleStatusChange.End, handler, cancellationToken);

    /// <summary>Registers in the ledger a date of the rule that is due. Sending it again registers nothing more.</summary>
    [HttpPost("recurring-rules/{ruleId:guid}/occurrences/{date}/confirm", Name = "ConfirmOccurrence")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Confirm(
        Guid workspaceId, Guid ruleId, DateOnly date, ConfirmOccurrenceRequest? request, [FromServices] ICommandHandler<ConfirmOccurrenceCommand, Unit> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ConfirmOccurrenceCommand(ruleId, date, request?.Amount), cancellationToken), _ => NoContent());

    /// <summary>Leaves a date of the rule without a movement.</summary>
    [HttpPost("recurring-rules/{ruleId:guid}/occurrences/{date}/skip", Name = "SkipOccurrence")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Skip(
        Guid workspaceId, Guid ruleId, DateOnly date, [FromServices] ICommandHandler<SkipOccurrenceCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new SkipOccurrenceCommand(ruleId, date), cancellationToken), _ => NoContent());

    private async Task<IActionResult> ChangeAsync(
        Guid ruleId, RuleStatusChange change, ICommandHandler<ChangeRuleStatusCommand, RecurringRuleResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ChangeRuleStatusCommand(ruleId, change), cancellationToken), Ok);
}
