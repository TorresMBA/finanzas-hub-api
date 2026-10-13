using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Modules.Identity.Application.Login;
using PersonalFinance.Modules.Identity.Application.Passwords;
using PersonalFinance.Modules.Identity.Application.Register;
using PersonalFinance.Modules.Identity.Domain;

namespace PersonalFinance.Api.Controllers.Identity;

public sealed class RegisterRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;

    [Required, StringLength(User.MaxDisplayNameLength, MinimumLength = 1)]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Present when the person arrived from an invitation link.</summary>
    [StringLength(100)]
    public string? InvitationToken { get; init; }
}

public sealed class LoginRequest
{
    [Required, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; init; } = string.Empty;
}

public sealed class ConfirmEmailRequest
{
    [Required]
    public Guid UserId { get; init; }

    [Required, StringLength(2000)]
    public string Token { get; init; } = string.Empty;
}

public sealed class ForgotPasswordRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(2000)]
    public string Token { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string NewPassword { get; init; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [Required, StringLength(128)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string NewPassword { get; init; } = string.Empty;
}

[Tags("Auth")]
[Route("api/v1/auth")]
[EnableRateLimiting(HttpSetup.AuthRateLimit)]
[Problem(StatusCodes.Status400BadRequest)]
[Problem(StatusCodes.Status429TooManyRequests)]
public sealed class AuthController : ApiControllerBase
{
    /// <summary>Creates the account with its personal workspace and starts the session.</summary>
    [AllowAnonymous]
    [HttpPost("register", Name = "Register")]
    [ProducesResponseType<AuthenticatedUser>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status403Forbidden)]
    [Problem(StatusCodes.Status404NotFound)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Register(
        RegisterRequest request, [FromServices] ICommandHandler<RegisterUserCommand, AuthenticatedUser> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new RegisterUserCommand(request.Email, request.Password, request.DisplayName, request.InvitationToken), cancellationToken);
        if (result.IsSuccess)
        {
            await Session.SignInAsync(HttpContext, result.Value.UserId);
        }

        return ToActionResult(result, Ok);
    }

    [AllowAnonymous]
    [HttpPost("login", Name = "Login")]
    [ProducesResponseType<AuthenticatedUser>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        LoginRequest request, [FromServices] ICommandHandler<LoginCommand, LoginOutcome> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new LoginCommand(request.Email, request.Password), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem(HttpContext);
        }

        if (result.Value.Error is { } rejected)
        {
            return rejected.ToProblem(HttpContext);
        }

        var userId = result.Value.UserId!.Value;
        await Session.SignInAsync(HttpContext, userId);
        return Ok(new AuthenticatedUser(userId));
    }

    [AllowAnonymous]
    [HttpPost("logout", Name = "Logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await Session.SignOutAsync(HttpContext);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("confirm-email", Name = "ConfirmEmail")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ConfirmEmail(
        ConfirmEmailRequest request, [FromServices] ICommandHandler<ConfirmEmailCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ConfirmEmailCommand(request.UserId, request.Token), cancellationToken), _ => NoContent());

    /// <summary>Always answers the same, whether or not the email has an account.</summary>
    [AllowAnonymous]
    [HttpPost("forgot-password", Name = "ForgotPassword")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest request, [FromServices] ICommandHandler<RequestPasswordResetCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new RequestPasswordResetCommand(request.Email), cancellationToken), _ => Accepted());

    [AllowAnonymous]
    [HttpPost("reset-password", Name = "ResetPassword")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request, [FromServices] ICommandHandler<ResetPasswordCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ResetPasswordCommand(request.Email, request.Token, request.NewPassword), cancellationToken), _ => NoContent());

    [HttpPost("change-password", Name = "ChangePassword")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request, [FromServices] ICommandHandler<ChangePasswordCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ChangePasswordCommand(request.CurrentPassword, request.NewPassword), cancellationToken), _ => NoContent());
}
