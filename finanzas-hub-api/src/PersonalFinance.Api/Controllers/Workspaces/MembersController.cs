using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Workspaces.Application.Invitations;
using PersonalFinance.Modules.Workspaces.Application.Members;

namespace PersonalFinance.Api.Controllers.Workspaces;

public sealed class ChangeMemberRoleRequest
{
    [Required]
    public WorkspaceRole? Role { get; init; }
}

public sealed class InviteMemberRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required]
    public WorkspaceRole? Role { get; init; }
}

[Tags("Members")]
[Route("api/v1/workspaces/{workspaceId:guid}/members")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class MembersController : ApiControllerBase
{
    [HttpGet(Name = "ListMembers")]
    [ProducesResponseType<IReadOnlyList<MemberResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid workspaceId, [FromServices] IQueryHandler<ListMembersQuery, IReadOnlyList<MemberResponse>> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ListMembersQuery(), cancellationToken), Ok);

    [HttpPatch("{userId:guid}", Name = "ChangeMemberRole")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangeRole(
        Guid workspaceId, Guid userId, ChangeMemberRoleRequest request, [FromServices] ICommandHandler<ChangeMemberRoleCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ChangeMemberRoleCommand(userId, request.Role!.Value), cancellationToken), _ => NoContent());

    /// <summary>Removes a member. With your own id, you leave the workspace.</summary>
    [HttpDelete("{userId:guid}", Name = "RemoveMember")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Remove(
        Guid workspaceId, Guid userId, [FromServices] ICommandHandler<RemoveMemberCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new RemoveMemberCommand(userId), cancellationToken), _ => NoContent());
}

[Tags("Invitations")]
[Route("api/v1/workspaces/{workspaceId:guid}/invitations")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status403Forbidden)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class InvitationsController : ApiControllerBase
{
    [HttpGet(Name = "ListInvitations")]
    [ProducesResponseType<IReadOnlyList<InvitationResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid workspaceId, [FromServices] IQueryHandler<ListInvitationsQuery, IReadOnlyList<InvitationResponse>> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ListInvitationsQuery(), cancellationToken), Ok);

    [HttpPost(Name = "InviteMember")]
    [ProducesResponseType<InvitationResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Invite(
        Guid workspaceId, InviteMemberRequest request, [FromServices] ICommandHandler<InviteMemberCommand, InvitationResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(
            await handler.HandleAsync(new InviteMemberCommand(request.Email, request.Role!.Value), cancellationToken),
            invitation => Created($"/api/v1/workspaces/{workspaceId}/invitations/{invitation.Id}", invitation));

    [HttpDelete("{invitationId:guid}", Name = "RevokeInvitation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Revoke(
        Guid workspaceId, Guid invitationId, [FromServices] ICommandHandler<RevokeInvitationCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new RevokeInvitationCommand(invitationId), cancellationToken), _ => NoContent());
}

/// <summary>The link that was emailed. Whoever holds the token can see what it offers and, once signed in, accept it.</summary>
[Tags("Invitations")]
[Route("api/v1/invitations/{token}")]
[Problem(StatusCodes.Status404NotFound)]
public sealed class InvitationLinksController : ApiControllerBase
{
    [AllowAnonymous]
    [HttpGet(Name = "GetInvitationPreview")]
    [ProducesResponseType<InvitationPreviewResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(
        [StringLength(100)] string token, [FromServices] IQueryHandler<GetInvitationPreviewQuery, InvitationPreviewResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetInvitationPreviewQuery(token), cancellationToken), Ok);

    [HttpPost("accept", Name = "AcceptInvitation")]
    [ProducesResponseType<AcceptedInvitationResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status401Unauthorized)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Accept(
        [StringLength(100)] string token, [FromServices] ICommandHandler<AcceptInvitationCommand, AcceptedInvitationResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new AcceptInvitationCommand(token), cancellationToken), Ok);
}
