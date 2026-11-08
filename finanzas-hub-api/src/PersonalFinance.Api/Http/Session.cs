using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Modules.Identity.Application.Login;

namespace PersonalFinance.Api.Http;

/// <summary>
/// The session is a cookie the browser cannot read from script. It carries the user id and the account's security
/// stamp: everything else (workspace, role) is resolved on each request, so a removed member loses access at once,
/// and a cookie issued before the password changed stops working.
/// </summary>
internal static class Session
{
    /// <summary>How long a session lasts without use. Each use within it extends it.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private const string StampClaim = "ph.stamp";

    public static async Task SignInAsync(HttpContext httpContext, Guid userId)
    {
        var stamp = await httpContext.RequestServices.GetRequiredService<ISessionStamps>().CurrentAsync(userId, httpContext.RequestAborted);
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(StampClaim, stamp ?? string.Empty)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    public static Task SignOutAsync(HttpContext httpContext) => httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    /// <summary>
    /// Runs on every request that brings the cookie. A session whose account is gone, or whose stamp changed
    /// (password changed or reset), is refused and the cookie removed.
    /// </summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            && principal!.FindFirstValue(StampClaim) is { Length: > 0 } stamp)
        {
            var current = await context.HttpContext.RequestServices.GetRequiredService<ISessionStamps>()
                .CurrentAsync(userId, context.HttpContext.RequestAborted);
            if (string.Equals(current, stamp, StringComparison.Ordinal))
            {
                return;
            }
        }

        context.RejectPrincipal();
        await SignOutAsync(context.HttpContext);
    }
}

/// <summary>Declares an error response of an action for the OpenAPI contract.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class ProblemAttribute(int statusCode)
    : ProducesResponseTypeAttribute(statusCode == StatusCodes.Status400BadRequest ? typeof(ValidationProblemDetails) : typeof(ProblemDetails), statusCode, "application/problem+json");
