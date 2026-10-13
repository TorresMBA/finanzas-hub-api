using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Modules.Identity.Application.Profile;
using PersonalFinance.Modules.Identity.Domain;

namespace PersonalFinance.Api.Controllers.Identity;

public sealed class UpdateProfileRequest
{
    [Required, StringLength(User.MaxDisplayNameLength, MinimumLength = 1)]
    public string DisplayName { get; init; } = string.Empty;

    [StringLength(User.MaxPhoneLength)]
    public string? Phone { get; init; }
}

public sealed class UpdatePreferencesRequest
{
    [Required, StringLength(10)]
    public string Locale { get; init; } = string.Empty;

    [Required, StringLength(20)]
    public string DateFormat { get; init; } = string.Empty;

    [Required]
    public WeekStart? WeekStart { get; init; }

    [Required]
    public ThemePreference? Theme { get; init; }

    public bool HideBalances { get; init; }

    public Guid? DefaultWorkspaceId { get; init; }

    [Range(0, 100)]
    public int? SavingsRateTarget { get; init; }
}

/// <summary>The signed-in person's own account. Nothing here belongs to a workspace.</summary>
[Tags("Me")]
[Route("api/v1/me")]
[Problem(StatusCodes.Status401Unauthorized)]
public sealed class MeController : ApiControllerBase
{
    [HttpGet(Name = "GetMe")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromServices] IQueryHandler<GetMeQuery, MeResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetMeQuery(), cancellationToken), Ok);

    [HttpPatch("profile", Name = "UpdateProfile")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateProfile(
        UpdateProfileRequest request, [FromServices] ICommandHandler<UpdateProfileCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new UpdateProfileCommand(request.DisplayName, request.Phone), cancellationToken), _ => NoContent());

    [HttpPut("preferences", Name = "UpdatePreferences")]
    [ProducesResponseType<PreferencesResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePreferences(
        UpdatePreferencesRequest request, [FromServices] ICommandHandler<UpdatePreferencesCommand, PreferencesResponse> handler, CancellationToken cancellationToken)
    {
        var command = new UpdatePreferencesCommand(
            request.Locale, request.DateFormat, request.WeekStart!.Value, request.Theme!.Value,
            request.HideBalances, request.DefaultWorkspaceId, request.SavingsRateTarget);
        return ToActionResult(await handler.HandleAsync(command, cancellationToken), Ok);
    }
}
