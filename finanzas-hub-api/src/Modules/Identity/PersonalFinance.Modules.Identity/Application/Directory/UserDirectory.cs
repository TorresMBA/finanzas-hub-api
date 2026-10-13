using Microsoft.EntityFrameworkCore;
using PersonalFinance.Modules.Identity.Application.Abstractions;
using PersonalFinance.Modules.Identity.Contracts;

namespace PersonalFinance.Modules.Identity.Application.Directory;

internal sealed class UserDirectory(IIdentityDb db) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, UserSummary>> GetAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, UserSummary>();
        }

        return await db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new UserSummary(user.Id, user.Email!, user.DisplayName))
            .ToDictionaryAsync(user => user.Id, cancellationToken);
    }

    public async Task<UserSummary?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        (await GetAsync([userId], cancellationToken)).GetValueOrDefault(userId);

    public async Task<UserSummary?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        // Identity keeps an upper-case copy of the email precisely for this lookup.
        var normalized = email.Trim().ToUpperInvariant();
        return await db.Users.AsNoTracking()
            .Where(user => user.NormalizedEmail == normalized)
            .Select(user => new UserSummary(user.Id, user.Email!, user.DisplayName))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
