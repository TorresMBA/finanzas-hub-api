using Microsoft.AspNetCore.Identity;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Identity.Application.Register;
using PersonalFinance.Modules.Identity.Domain;

namespace PersonalFinance.Modules.Identity.Application.Passwords;

public sealed record ConfirmEmailCommand(Guid UserId, string Token) : ICommand<Unit>;

internal sealed class ConfirmEmailHandler(UserManager<User> users, IAuditLog audit) : ICommandHandler<ConfirmEmailCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(command.UserId.ToString());
        if (user is null || UrlToken.Decode(command.Token) is not { } token)
        {
            return IdentityErrors.InvalidToken;
        }

        if (user.EmailConfirmed)
        {
            return Unit.Value;
        }

        var confirmed = await users.ConfirmEmailAsync(user, token);
        if (!confirmed.Succeeded)
        {
            return IdentityErrors.From(confirmed);
        }

        audit.Record(new AuditEntry("identity", "identity.user.email_confirmed", "user", user.Id) { OwnerUserId = user.Id });
        return Unit.Value;
    }
}

public sealed record RequestPasswordResetCommand(string Email) : ICommand<Unit>;

internal sealed class RequestPasswordResetHandler(UserManager<User> users, IEmailSender emails, IAppLinks links) : ICommandHandler<RequestPasswordResetCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        // Always succeeds: the answer must not reveal whether the email has an account.
        var user = await users.FindByEmailAsync(command.Email.Trim());
        if (user is not null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            emails.Queue(IdentityEmails.ResetPassword(user, links.To($"/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={UrlToken.Encode(token)}")));
        }

        return Unit.Value;
    }
}

public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword) : ICommand<Unit>;

internal sealed class ResetPasswordHandler(UserManager<User> users, IAuditLog audit) : ICommandHandler<ResetPasswordCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(command.Email.Trim());
        if (user is null || UrlToken.Decode(command.Token) is not { } token)
        {
            return IdentityErrors.InvalidToken;
        }

        var reset = await users.ResetPasswordAsync(user, token, command.NewPassword);
        if (!reset.Succeeded)
        {
            return IdentityErrors.From(reset, "newPassword");
        }

        // Whoever could read the mailbox to get here has also proven the email, and may come back in.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
        }

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        audit.Record(new AuditEntry("identity", "identity.user.password_reset", "user", user.Id) { OwnerUserId = user.Id });
        return Unit.Value;
    }
}

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand<Unit>;

internal sealed class ChangePasswordHandler(UserManager<User> users, ICurrentUser currentUser, IAuditLog audit) : ICommandHandler<ChangePasswordCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || await users.FindByIdAsync(userId.ToString()) is not { } user)
        {
            return IdentityErrors.Unauthenticated;
        }

        var changed = await users.ChangePasswordAsync(user, command.CurrentPassword, command.NewPassword);
        if (!changed.Succeeded)
        {
            return IdentityErrors.From(changed, "newPassword");
        }

        audit.Record(new AuditEntry("identity", "identity.user.password_changed", "user", user.Id) { OwnerUserId = user.Id });
        return Unit.Value;
    }
}
