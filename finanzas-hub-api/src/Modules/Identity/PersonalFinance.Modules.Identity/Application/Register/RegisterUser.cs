using Microsoft.AspNetCore.Identity;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Identity.Application.Abstractions;
using PersonalFinance.Modules.Identity.Contracts;
using PersonalFinance.Modules.Identity.Domain;

namespace PersonalFinance.Modules.Identity.Application.Register;

/// <summary>Who the session belongs to. The API turns it into the session cookie.</summary>
public sealed record AuthenticatedUser(Guid UserId);

public sealed record RegisterUserCommand(string Email, string Password, string DisplayName, string? InvitationToken) : ICommand<AuthenticatedUser>;

internal sealed class RegisterUserHandler(
    UserManager<User> users, IIdentityDb db, IPostRegistrationStep afterRegistration, IEmailSender emails, IAppLinks links, IAuditLog audit, IClock clock)
    : ICommandHandler<RegisterUserCommand, AuthenticatedUser>
{
    public async Task<Result<AuthenticatedUser>> HandleAsync(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        var created = User.Create(command.Email, command.DisplayName, clock.UtcNow);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var user = created.Value;
        var stored = await users.CreateAsync(user, command.Password);
        if (!stored.Succeeded)
        {
            return IdentityErrors.From(stored);
        }

        var preferences = UserPreferences.CreateDefault(user.Id);
        db.UserPreferences.Add(preferences);

        // Same transaction: an account never exists without its personal workspace.
        var workspace = await afterRegistration.OnRegisteredAsync(new NewUser(user.Id, user.Email!, user.DisplayName, command.InvitationToken), cancellationToken);
        if (workspace.IsFailure)
        {
            return workspace.Error;
        }

        preferences.OpenOn(workspace.Value);

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        emails.Queue(IdentityEmails.ConfirmEmail(user, links.To($"/confirm-email?userId={user.Id}&token={UrlToken.Encode(token)}")));
        audit.Record(new AuditEntry("identity", "identity.user.registered", "user", user.Id) { OwnerUserId = user.Id });

        return new AuthenticatedUser(user.Id);
    }
}

internal static class IdentityEmails
{
    public static EmailMessage ConfirmEmail(User user, string link) => new(
        user.Email!,
        "Confirma tu correo en PersonalHub",
        $"Hola, {user.DisplayName}:\n\nConfirma que este correo es tuyo abriendo este enlace:\n{link}\n\nSi no creaste una cuenta en PersonalHub, ignora este mensaje.");

    public static EmailMessage ResetPassword(User user, string link) => new(
        user.Email!,
        "Restablece tu contraseña de PersonalHub",
        $"Hola, {user.DisplayName}:\n\nPara elegir una contraseña nueva abre este enlace:\n{link}\n\nSi no lo pediste, ignora este mensaje: tu contraseña no cambia.");
}
