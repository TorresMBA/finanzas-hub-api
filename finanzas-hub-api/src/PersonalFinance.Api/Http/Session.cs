using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace PersonalFinance.Api.Http;

/// <summary>
/// The session is a cookie the browser cannot read from script. It carries only the user id: everything else
/// (workspace, role) is resolved on each request, so a removed member loses access at once.
/// </summary>
internal static class Session
{
    public static Task SignInAsync(HttpContext httpContext, Guid userId)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], CookieAuthenticationDefaults.AuthenticationScheme);
        return httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    public static Task SignOutAsync(HttpContext httpContext) => httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
}

/// <summary>Declares an error response of an action for the OpenAPI contract.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class ProblemAttribute(int statusCode)
    : ProducesResponseTypeAttribute(statusCode == StatusCodes.Status400BadRequest ? typeof(ValidationProblemDetails) : typeof(ProblemDetails), statusCode, "application/problem+json");
