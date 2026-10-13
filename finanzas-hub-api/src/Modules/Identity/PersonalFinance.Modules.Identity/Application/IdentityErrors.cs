using System.Buffers.Text;
using System.Text;
using Microsoft.AspNetCore.Identity;
using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Identity.Application;

internal static class IdentityErrors
{
    public static readonly Error Unauthenticated = Error.Unauthorized("auth.unauthenticated", "Authentication is required.");

    /// <summary>The same answer for an unknown email and for a wrong password, so neither can be told apart.</summary>
    public static readonly Error InvalidCredentials = Error.Unauthorized("auth.invalid_credentials", "The email or the password is not correct.");

    public static readonly Error LockedOut = Error.Unauthorized("auth.locked_out", "Too many failed attempts. Try again in a few minutes.");

    public static readonly Error InvalidToken = Error.BusinessRule("identity.token_invalid", "The link is not valid or has expired. Request a new one.");

    /// <summary>Turns what ASP.NET Core Identity rejected into one error with a message per field.</summary>
    public static Error From(IdentityResult result, string passwordField = "password")
    {
        if (result.Errors.Any(error => error.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return Error.Conflict("identity.email_taken", "There is already an account with this email.");
        }

        if (result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.InvalidToken)))
        {
            return InvalidToken;
        }

        if (result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
        {
            return Error.Validation("identity.password_incorrect", "The current password is not correct.",
                new Dictionary<string, string[]> { ["currentPassword"] = ["The current password is not correct."] });
        }

        var fieldErrors = result.Errors
            .GroupBy(error => error.Code.StartsWith("Password", StringComparison.Ordinal) ? passwordField : "email", error => error.Description)
            .ToDictionary(group => group.Key, group => group.ToArray());
        return Error.Validation("identity.invalid", "The account data is not valid.", fieldErrors);
    }
}

/// <summary>Identity tokens contain characters that do not survive a URL; links carry them in base64url.</summary>
internal static class UrlToken
{
    public static string Encode(string token) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token));

    public static string? Decode(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded) || !Base64Url.IsValid(encoded))
        {
            return null;
        }

        return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(encoded));
    }
}
