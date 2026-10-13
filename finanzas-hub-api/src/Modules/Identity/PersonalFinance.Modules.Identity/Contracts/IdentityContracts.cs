using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Identity.Contracts;

public sealed record UserSummary(Guid Id, string Email, string DisplayName);

/// <summary>What other modules may know about a person: who they are, never their credentials.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, UserSummary>> GetAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    Task<UserSummary?> FindAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserSummary?> FindByEmailAsync(string email, CancellationToken cancellationToken);
}

/// <param name="InvitationToken">Present when the person registered from an invitation link.</param>
public sealed record NewUser(Guid Id, string Email, string DisplayName, string? InvitationToken);

/// <summary>
/// What must exist the moment a person has an account. Identity defines it and Workspaces implements it, so
/// registration creates the personal workspace (and accepts an invitation) in its own transaction without
/// Identity depending on Workspaces.
/// </summary>
public interface IPostRegistrationStep
{
    /// <returns>The workspace the new user should open on.</returns>
    Task<Result<Guid>> OnRegisteredAsync(NewUser user, CancellationToken cancellationToken);
}
