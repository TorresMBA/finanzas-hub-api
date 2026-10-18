using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Identity;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Identity.Domain;

namespace PersonalFinance.Modules.Identity.Application.Login;

public enum LoginFailure
{
    InvalidCredentials,
    LockedOut
}

/// <summary>
/// A failed attempt is a successful command: it has an effect that must be saved (the failure count that
/// leads to the lockout). Returning an error would roll that count back.
/// </summary>
public sealed record LoginOutcome(Guid? UserId, LoginFailure? Failure)
{
    public Error? Error => Failure switch
    {
        LoginFailure.InvalidCredentials => IdentityErrors.InvalidCredentials,
        LoginFailure.LockedOut => IdentityErrors.LockedOut,
        _ => null
    };
}

public sealed record LoginCommand(string Email, string Password) : ICommand<LoginOutcome>;

internal sealed class LoginHandler(UserManager<User> users, IAuditLog audit) : ICommandHandler<LoginCommand, LoginOutcome>
{
    public async Task<Result<LoginOutcome>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(command.Email.Trim());
        if (user is null)
        {
            return Failed(LoginFailure.InvalidCredentials);
        }

        if (await users.IsLockedOutAsync(user))
        {
            return Failed(LoginFailure.LockedOut);
        }

        if (!await users.CheckPasswordAsync(user, command.Password))
        {
            await users.AccessFailedAsync(user);
            if (!await users.IsLockedOutAsync(user))
            {
                return Failed(LoginFailure.InvalidCredentials);
            }

            audit.Record(new AuditEntry("identity", "identity.user.locked_out", "user", user.Id) { OwnerUserId = user.Id });
            return Failed(LoginFailure.LockedOut);
        }

        if (user.AccessFailedCount > 0)
        {
            await users.ResetAccessFailedCountAsync(user);
        }

        return new LoginOutcome(user.Id, null);
    }

    // Same meter name as the platform's, which is the one the hosts export. Never tagged with who tried.
    private static readonly Counter<long> FailedLogins =
        new Meter("PersonalFinance").CreateCounter<long>("auth.failed_logins", description: "Sign-ins refused, by reason.");

    private static LoginOutcome Failed(LoginFailure failure)
    {
        FailedLogins.Add(1, new KeyValuePair<string, object?>("reason", failure == LoginFailure.LockedOut ? "locked_out" : "invalid_credentials"));
        return new LoginOutcome(null, failure);
    }
}
