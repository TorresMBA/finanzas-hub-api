using Microsoft.AspNetCore.Identity;
using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Identity.Domain;

/// <summary>
/// A person who can sign in. Credentials, lockout and tokens are ASP.NET Core Identity's; this type only adds
/// what the application itself needs to know about the person. The email is also the user name.
/// </summary>
public sealed class User : IdentityUser<Guid>
{
    public const int MaxDisplayNameLength = 80;
    public const int MaxPhoneLength = 20;

    private User()
    {
    }

    public string DisplayName { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<User> Create(string? email, string? displayName, DateTimeOffset now)
    {
        var normalizedEmail = email?.Trim() ?? string.Empty;
        if (normalizedEmail.Length is < 3 or > 254 || normalizedEmail.Count(character => character == '@') != 1)
        {
            return Error.Validation("identity.email_invalid", "The email address is not valid.");
        }

        var name = NormalizeDisplayName(displayName);
        if (name.IsFailure)
        {
            return name.Error;
        }

        return new User
        {
            Id = Guid.CreateVersion7(),
            Email = normalizedEmail,
            UserName = normalizedEmail,
            DisplayName = name.Value,
            CreatedAt = now
        };
    }

    public Result UpdateProfile(string? displayName, string? phone)
    {
        var name = NormalizeDisplayName(displayName);
        if (name.IsFailure)
        {
            return name.Error;
        }

        var normalizedPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (normalizedPhone is not null
            && (normalizedPhone.Length > MaxPhoneLength || !normalizedPhone.All(character => char.IsAsciiDigit(character) || character is '+' or ' ' or '-')))
        {
            return Error.Validation("identity.phone_invalid", "The phone number may contain only digits, spaces, '+' and '-'.");
        }

        if (normalizedPhone != PhoneNumber)
        {
            PhoneNumber = normalizedPhone;
            PhoneNumberConfirmed = false;
        }

        DisplayName = name.Value;
        return Result.Success();
    }

    private static Result<string> NormalizeDisplayName(string? displayName)
    {
        var normalized = displayName?.Trim() ?? string.Empty;
        return normalized.Length is 0 or > MaxDisplayNameLength
            ? Error.Validation("identity.display_name_invalid", $"The name must have between 1 and {MaxDisplayNameLength} characters.")
            : normalized;
    }
}
