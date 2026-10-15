using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.BuildingBlocks.Application.Security;

public enum ActorType
{
    User,
    System,
    Integration
}

/// <summary>Who is executing the current use case, whether it arrived over HTTP or from a background process.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    ActorType ActorType { get; }

    bool IsAuthenticated => UserId is not null;
}

/// <summary>The personal workspace of a person: where their private finances live, whichever workspace they are looking at.</summary>
public sealed record PersonalSpaceInfo(Guid WorkspaceId, string? BaseCurrency, string TimeZone);

/// <param name="Personal">The personal workspace of the member. When left out, this workspace itself is taken as theirs.</param>
public sealed record WorkspaceMembershipInfo(
    Guid WorkspaceId, Guid UserId, WorkspaceRole Role, string? BaseCurrency, string TimeZone, PersonalSpaceInfo? Personal = null);

/// <summary>
/// Where the private finances of the current person live: their personal workspace. Accounts, movements,
/// categories, budgets and scheduled movements belong to the person, not to the workspace they have open, so
/// the modules that hold them ask here and never to <see cref="IWorkspaceContext"/>. Someone invited to the
/// workspace of another person finds their own books there, not an empty set.
/// </summary>
public interface IPersonalSpace
{
    Guid? WorkspaceId { get; }

    /// <summary>The currency the private reports of the person are stated in.</summary>
    string? BaseCurrency { get; }

    string? TimeZone { get; }
}

/// <summary>
/// The workspace the current use case runs in, resolved once per request from an active membership. It is what
/// collaboration happens in (members, goals, shared expenses); private finances go by <see cref="IPersonalSpace"/>.
/// </summary>
public interface IWorkspaceContext
{
    Guid? WorkspaceId { get; }

    WorkspaceRole? Role { get; }

    string? BaseCurrency { get; }

    string? TimeZone { get; }

    /// <summary>Fails with <see cref="ErrorType.Forbidden"/> when the member's role is below <paramref name="minimum"/>.</summary>
    Result Require(WorkspaceRole minimum);
}

/// <summary>Implemented by the Workspaces module. Returns null when the user is not an active member.</summary>
public interface IWorkspaceAccess
{
    Task<WorkspaceMembershipInfo?> GetActiveMembershipAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken);

    /// <summary>The personal workspace of a user, for acting as them outside a request to a workspace (a WhatsApp message). Null when they have none.</summary>
    Task<WorkspaceMembershipInfo?> GetPersonalMembershipAsync(Guid userId, CancellationToken cancellationToken);
}
