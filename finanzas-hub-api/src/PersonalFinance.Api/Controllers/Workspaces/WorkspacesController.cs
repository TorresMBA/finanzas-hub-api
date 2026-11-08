using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Workspaces.Application.Members;
using PersonalFinance.Modules.Workspaces.Application.Workspaces;
using PersonalFinance.Modules.Workspaces.Domain;

namespace PersonalFinance.Api.Controllers.Workspaces;

public sealed class CreateWorkspaceRequest
{
    [Required, StringLength(Workspace.MaxNameLength, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Personal (to share with others) by default. Business and Other keep their own books, for their owner alone.</summary>
    public WorkspaceKind Kind { get; init; } = WorkspaceKind.Personal;
}

public sealed class UpdateWorkspaceRequest
{
    [Required, StringLength(Workspace.MaxNameLength, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required, StringLength(64)]
    public string TimeZone { get; init; } = string.Empty;
}

public sealed class CompleteOnboardingRequest
{
    [Required, StringLength(3, MinimumLength = 3)]
    public string BaseCurrency { get; init; } = string.Empty;

    [Required, StringLength(64)]
    public string TimeZone { get; init; } = string.Empty;
}

public sealed class TransferOwnershipRequest
{
    [Required]
    public Guid? UserId { get; init; }
}

/// <summary>The workspaces the signed-in person belongs to.</summary>
[Tags("Workspaces")]
[Route("api/v1/me/workspaces")]
[Problem(StatusCodes.Status401Unauthorized)]
public sealed class MyWorkspacesController : ApiControllerBase
{
    [HttpGet(Name = "ListMyWorkspaces")]
    [ProducesResponseType<IReadOnlyList<WorkspaceResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromServices] IQueryHandler<GetMyWorkspacesQuery, IReadOnlyList<WorkspaceResponse>> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetMyWorkspacesQuery(), cancellationToken), Ok);
}

[Tags("Workspaces")]
[Route("api/v1/workspaces")]
[Problem(StatusCodes.Status401Unauthorized)]
public sealed class WorkspacesController : ApiControllerBase
{
    [HttpPost(Name = "CreateWorkspace")]
    [ProducesResponseType<WorkspaceResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        CreateWorkspaceRequest request, [FromServices] ICommandHandler<CreateWorkspaceCommand, WorkspaceResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(
            await handler.HandleAsync(new CreateWorkspaceCommand(request.Name, request.Kind), cancellationToken),
            workspace => Created($"/api/v1/workspaces/{workspace.Id}", workspace));

    [HttpGet("{workspaceId:guid}", Name = "GetWorkspace")]
    [ProducesResponseType<WorkspaceResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid workspaceId, [FromServices] IQueryHandler<GetWorkspaceQuery, WorkspaceResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetWorkspaceQuery(), cancellationToken), Ok);

    [HttpPatch("{workspaceId:guid}", Name = "UpdateWorkspace")]
    [ProducesResponseType<WorkspaceResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status404NotFound)]
    [Problem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid workspaceId, UpdateWorkspaceRequest request, [FromServices] ICommandHandler<UpdateWorkspaceCommand, WorkspaceResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new UpdateWorkspaceCommand(request.Name, request.TimeZone), cancellationToken), Ok);

    /// <summary>The owner chooses the base currency and the time zone. Until then the workspace cannot hold data.</summary>
    [HttpPost("{workspaceId:guid}/onboarding/complete", Name = "CompleteOnboarding")]
    [ProducesResponseType<WorkspaceResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status404NotFound)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CompleteOnboarding(
        Guid workspaceId, CompleteOnboardingRequest request, [FromServices] ICommandHandler<CompleteOnboardingCommand, WorkspaceResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new CompleteOnboardingCommand(request.BaseCurrency, request.TimeZone), cancellationToken), Ok);

    [HttpPost("{workspaceId:guid}/ownership-transfer", Name = "TransferOwnership")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status404NotFound)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> TransferOwnership(
        Guid workspaceId, TransferOwnershipRequest request, [FromServices] ICommandHandler<TransferOwnershipCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new TransferOwnershipCommand(request.UserId!.Value), cancellationToken), _ => NoContent());

    /// <summary>Whether the person asking can delete the workspace now, and the code of the rule that stops it when not.</summary>
    [HttpGet("{workspaceId:guid}/deletion", Name = "GetWorkspaceDeletion")]
    [ProducesResponseType<WorkspaceDeletionResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDeletion(
        Guid workspaceId, [FromServices] IQueryHandler<GetWorkspaceDeletionQuery, WorkspaceDeletionResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetWorkspaceDeletionQuery(), cancellationToken), Ok);

    /// <summary>
    /// Only its owner, never the personal one, once nobody else is in it and nothing was registered in it. Its suggested
    /// categories, notices and invitations go with it. One with data is archived.
    /// </summary>
    [HttpDelete("{workspaceId:guid}", Name = "DeleteWorkspace")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status404NotFound)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(Guid workspaceId, [FromServices] ICommandHandler<DeleteWorkspaceCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new DeleteWorkspaceCommand(), cancellationToken), _ => NoContent());
}
