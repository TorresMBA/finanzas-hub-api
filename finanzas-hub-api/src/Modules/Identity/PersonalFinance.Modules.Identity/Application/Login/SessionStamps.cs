using Microsoft.EntityFrameworkCore;
using PersonalFinance.Modules.Identity.Application.Abstractions;

namespace PersonalFinance.Modules.Identity.Application.Login;

/// <summary>
/// The security stamp ASP.NET Core Identity changes whenever the password changes or is reset. The session
/// cookie carries the stamp it was issued with, and a cookie whose stamp no longer matches is refused: changing
/// the password ends every other session.
/// </summary>
public interface ISessionStamps
{
    /// <summary>The current stamp, or null when the account no longer exists.</summary>
    Task<string?> CurrentAsync(Guid userId, CancellationToken cancellationToken);
}

internal sealed class SessionStamps(IIdentityDb db) : ISessionStamps
{
    public Task<string?> CurrentAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.Where(user => user.Id == userId).Select(user => user.SecurityStamp).SingleOrDefaultAsync(cancellationToken);
}
