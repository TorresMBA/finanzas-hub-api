using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.SharedExpenses.Application;
using PersonalFinance.Modules.SharedExpenses.Domain;

namespace PersonalFinance.Api.Controllers.SharedExpenses;

public sealed class CreateSharedGroupRequest
{
    [Required, StringLength(SharedGroup.MaxNameLength, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Three-letter code. That of the workspace when left out.</summary>
    [StringLength(3, MinimumLength = 3)]
    public string? Currency { get; init; }

    /// <summary>Other members of the workspace to split with.</summary>
    public IReadOnlyList<Guid>? MemberUserIds { get; init; }

    /// <summary>The name of a glyph of the icon font. A generic one when left out.</summary>
    [StringLength(SharedGroup.MaxIconLength)]
    public string? Icon { get; init; }
}

public sealed class ChangeSharedGroupIconRequest
{
    [Required, StringLength(SharedGroup.MaxIconLength, MinimumLength = 1)]
    public string Icon { get; init; } = string.Empty;
}

public sealed class AddSharedGroupMemberRequest
{
    [Required]
    public Guid? UserId { get; init; }
}

public sealed class SplitParticipantRequest
{
    [Required]
    public Guid? UserId { get; init; }

    /// <summary>The percentage, the amount or the parts, by the kind of split. Left out for an equal one.</summary>
    public decimal? Value { get; init; }
}

public sealed class CreateSharedExpenseRequest
{
    [Required, StringLength(SharedGroup.MaxDescriptionLength, MinimumLength = 1)]
    public string Description { get; init; } = string.Empty;

    /// <summary>Positive, in the currency of the group.</summary>
    [Required]
    public decimal? TotalAmount { get; init; }

    [Required]
    public Guid? PaidByUserId { get; init; }

    /// <summary>Today when left out.</summary>
    public DateOnly? OccurredOn { get; init; }

    public SplitType SplitType { get; init; } = SplitType.Equal;

    [Required, MinLength(1)]
    public IReadOnlyList<SplitParticipantRequest> Participants { get; init; } = [];

    /// <summary>An account of who paid, to register the whole expense in their ledger too. Only who paid can give it, with a category.</summary>
    public Guid? AccountId { get; init; }

    public Guid? CategoryId { get; init; }
}

public sealed class RecordSettlementRequest
{
    /// <summary>Who pays: the one who owed.</summary>
    [Required]
    public Guid? FromUserId { get; init; }

    [Required]
    public Guid? ToUserId { get; init; }

    /// <summary>Positive, in the currency of the group, and no more than what is owed.</summary>
    [Required]
    public decimal? Amount { get; init; }

    /// <summary>Today when left out.</summary>
    public DateOnly? OccurredOn { get; init; }

    /// <summary>An account of the person registering it, to put the payment in their ledger too.</summary>
    public Guid? AccountId { get; init; }

    /// <summary>For who pays, the category of the expense. For who is paid, left out to use the one of the original expense.</summary>
    public Guid? CategoryId { get; init; }
}

public sealed class LinkSettlementAccountRequest
{
    [Required]
    public Guid? AccountId { get; init; }

    public Guid? CategoryId { get; init; }
}

/// <summary>
/// The groups the signed-in person splits expenses in. A group is shared by membership of the group: a member
/// of the workspace who is not in it gets a 404, as if it did not exist.
/// </summary>
[Tags("SharedExpenses")]
[Route("api/v1/workspaces/{workspaceId:guid}/shared-groups")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class SharedGroupsController : ApiControllerBase
{
    /// <summary>The groups of the person, each with what they are owed or owe in it, and the totals per currency.</summary>
    [HttpGet(Name = "ListSharedGroups")]
    [ProducesResponseType<SharedGroupsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid workspaceId, [FromServices] IQueryHandler<ListSharedGroupsQuery, SharedGroupsResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ListSharedGroupsQuery(), cancellationToken), Ok);

    /// <summary>Members with their balance, who owes whom, expenses and payments. It never says from which account anyone paid.</summary>
    [HttpGet("{groupId:guid}", Name = "GetSharedGroup")]
    [ProducesResponseType<SharedGroupDetailResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        Guid workspaceId, Guid groupId, [FromServices] IQueryHandler<GetSharedGroupQuery, SharedGroupDetailResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetSharedGroupQuery(groupId), cancellationToken), Ok);

    [HttpPost(Name = "CreateSharedGroup")]
    [ProducesResponseType<SharedGroupDetailResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        Guid workspaceId, CreateSharedGroupRequest request, [FromServices] ICommandHandler<CreateSharedGroupCommand, SharedGroupDetailResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(
            await handler.HandleAsync(new CreateSharedGroupCommand(request.Name, request.Currency, request.MemberUserIds, request.Icon), cancellationToken),
            detail => Created($"/api/v1/workspaces/{workspaceId}/shared-groups/{detail.Group.Id}", detail));

    /// <summary>Only who created the group, and only members of the workspace.</summary>
    [HttpPost("{groupId:guid}/members", Name = "AddSharedGroupMember")]
    [ProducesResponseType<SharedGroupDetailResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AddMember(
        Guid workspaceId, Guid groupId, AddSharedGroupMemberRequest request,
        [FromServices] ICommandHandler<ChangeSharedGroupMemberCommand, SharedGroupDetailResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ChangeSharedGroupMemberCommand(groupId, request.UserId!.Value, GroupMembershipChange.Add), cancellationToken), Ok);

    /// <summary>Who created the group removes someone, or someone leaves by naming themselves. Not while they owe or are owed.</summary>
    [HttpDelete("{groupId:guid}/members/{userId:guid}", Name = "RemoveSharedGroupMember")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RemoveMember(
        Guid workspaceId, Guid groupId, Guid userId, [FromServices] ICommandHandler<ChangeSharedGroupMemberCommand, SharedGroupDetailResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ChangeSharedGroupMemberCommand(groupId, userId, GroupMembershipChange.Remove), cancellationToken), _ => NoContent());

    /// <summary>Only who created the group, once everything is repaid.</summary>
    [HttpPut("{groupId:guid}/icon", Name = "ChangeSharedGroupIcon")]
    [ProducesResponseType<SharedGroupDetailResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ChangeIcon(
        Guid workspaceId, Guid groupId, ChangeSharedGroupIconRequest request,
        [FromServices] ICommandHandler<ChangeSharedGroupIconCommand, SharedGroupDetailResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ChangeSharedGroupIconCommand(groupId, request.Icon), cancellationToken), Ok);

    [HttpPost("{groupId:guid}/archive", Name = "ArchiveSharedGroup")]
    [ProducesResponseType<SharedGroupDetailResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Archive(
        Guid workspaceId, Guid groupId, [FromServices] ICommandHandler<ArchiveSharedGroupCommand, SharedGroupDetailResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ArchiveSharedGroupCommand(groupId), cancellationToken), Ok);

    /// <summary>Registers what one member paid for several, split equally, by percentage, by amount or by parts.</summary>
    [HttpPost("{groupId:guid}/expenses", Name = "CreateSharedExpense")]
    [ProducesResponseType<SharedGroupDetailResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateExpense(
        Guid workspaceId, Guid groupId, CreateSharedExpenseRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] ICommandHandler<CreateSharedExpenseCommand, SharedGroupDetailResponse> handler, CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem;
        }

        return ToActionResult(
            await handler.HandleAsync(
                new CreateSharedExpenseCommand(
                    groupId, request.Description, request.TotalAmount!.Value, request.PaidByUserId!.Value, request.OccurredOn, request.SplitType,
                    request.Participants.Select(participant => new SplitInput(participant.UserId!.Value, participant.Value)).ToList(), request.AccountId,
                    request.CategoryId, key),
                cancellationToken),
            detail => Created($"/api/v1/workspaces/{workspaceId}/shared-groups/{groupId}", detail));
    }

    /// <summary>Who registered the expense or who paid it. Not once someone repaid part of it.</summary>
    [HttpPost("{groupId:guid}/expenses/{expenseId:guid}/cancel", Name = "CancelSharedExpense")]
    [ProducesResponseType<SharedGroupDetailResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CancelExpense(
        Guid workspaceId, Guid groupId, Guid expenseId, [FromServices] ICommandHandler<CancelSharedExpenseCommand, SharedGroupDetailResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new CancelSharedExpenseCommand(groupId, expenseId), cancellationToken), Ok);

    /// <summary>One member repays another. Registered by either of the two; never more than what is owed.</summary>
    [HttpPost("{groupId:guid}/settlements", Name = "RecordSettlement")]
    [ProducesResponseType<SharedGroupDetailResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RecordSettlement(
        Guid workspaceId, Guid groupId, RecordSettlementRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] ICommandHandler<RecordSettlementCommand, SharedGroupDetailResponse> handler, CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem;
        }

        return ToActionResult(
            await handler.HandleAsync(
                new RecordSettlementCommand(
                    groupId, request.FromUserId!.Value, request.ToUserId!.Value, request.Amount!.Value, request.OccurredOn, request.AccountId, request.CategoryId, key),
                cancellationToken),
            detail => Created($"/api/v1/workspaces/{workspaceId}/shared-groups/{groupId}", detail));
    }

    /// <summary>One of the two sides of a payment puts it in one of their accounts: an expense for who paid, a refund for who was paid.</summary>
    [HttpPost("{groupId:guid}/settlements/{settlementId:guid}/link-account", Name = "LinkSettlementAccount")]
    [ProducesResponseType<SharedGroupDetailResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> LinkSettlementAccount(
        Guid workspaceId, Guid groupId, Guid settlementId, LinkSettlementAccountRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] ICommandHandler<LinkSettlementAccountCommand, SharedGroupDetailResponse> handler, CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem;
        }

        return ToActionResult(
            await handler.HandleAsync(new LinkSettlementAccountCommand(groupId, settlementId, request.AccountId!.Value, request.CategoryId, key), cancellationToken), Ok);
    }

    /// <summary>Only who registered the payment. What it had covered is owed again.</summary>
    [HttpPost("{groupId:guid}/settlements/{settlementId:guid}/void", Name = "VoidSettlement")]
    [ProducesResponseType<SharedGroupDetailResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> VoidSettlement(
        Guid workspaceId, Guid groupId, Guid settlementId, [FromServices] ICommandHandler<VoidSettlementCommand, SharedGroupDetailResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new VoidSettlementCommand(groupId, settlementId), cancellationToken), Ok);

    /// <summary>Who is owed reminds who owes, at most once a day.</summary>
    [HttpPost("{groupId:guid}/members/{userId:guid}/remind", Name = "RemindSharedGroupMember")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Remind(
        Guid workspaceId, Guid groupId, Guid userId, [FromServices] ICommandHandler<RemindSharedMemberCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new RemindSharedMemberCommand(groupId, userId), cancellationToken), _ => NoContent());
}
