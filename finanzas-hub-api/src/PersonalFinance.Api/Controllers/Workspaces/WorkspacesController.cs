using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Modules.Workspaces.Application.Members;
using PersonalFinance.Modules.Workspaces.Application.Workspaces;
using PersonalFinance.Modules.Workspaces.Domain;

namespace PersonalFinance.Api.Controllers.Workspaces;

public sealed class CreateWorkspaceRequest
{
    [Required, StringLength(Workspace.MaxNameLength, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;
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
            await handler.HandleAsync(new CreateWorkspaceCommand(request.Name), cancellationToken),
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
}
