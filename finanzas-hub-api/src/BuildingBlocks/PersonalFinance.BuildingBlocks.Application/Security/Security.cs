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

/// <summary>A workspace that keeps books: where the private finances of a person live.</summary>
public sealed record BookInfo(Guid WorkspaceId, string? BaseCurrency, string TimeZone);

/// <param name="Personal">
/// Where the private finances of the member are when they are not in this workspace: their personal one, for a
/// workspace of kind Personal. When left out, this workspace itself holds them (a business, or the personal one).
/// </param>
public sealed record WorkspaceMembershipInfo(
    Guid WorkspaceId, Guid UserId, WorkspaceRole Role, string? BaseCurrency, string TimeZone, BookInfo? Personal = null,
    WorkspaceKind Kind = WorkspaceKind.Personal);

/// <summary>
/// The books the current request works on (ADR-023). With a Personal workspace open, they are those of the
/// personal workspace of the person, whichever Personal workspace it is: someone invited to the workspace of
/// another person finds their own books there, not an empty set. With a Business or Other workspace open, they are
/// that workspace's own. Accounts, movements, categories, budgets and scheduled movements go by this, never by
/// <see cref="IWorkspaceContext"/>.
/// </summary>
public interface IBook
{
    Guid? WorkspaceId { get; }

    /// <summary>The currency the private reports of the person are stated in.</summary>
    string? BaseCurrency { get; }

    string? TimeZone { get; }
}

/// <summary>
/// The workspace the current use case runs in, resolved once per request from an active membership. It is what
/// collaboration happens in (members, goals, shared expenses); private finances go by <see cref="IBook"/>.
/// </summary>
public interface IWorkspaceContext
{
    Guid? WorkspaceId { get; }

    WorkspaceRole? Role { get; }

    string? BaseCurrency { get; }

    string? TimeZone { get; }

    WorkspaceKind? Kind { get; }

    /// <summary>Fails with <see cref="ErrorType.Forbidden"/> when the member's role is below <paramref name="minimum"/>.</summary>
    Result Require(WorkspaceRole minimum);

    /// <summary>Fails when the workspace is not of kind Personal: only those have goals, shared expenses and other members.</summary>
    Result RequireCollaboration();
}

/// <summary>Implemented by the Workspaces module. Returns null when the user is not an active member.</summary>
public interface IWorkspaceAccess
{
    Task<WorkspaceMembershipInfo?> GetActiveMembershipAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken);

    /// <summary>The personal workspace of a user, for acting as them outside a request to a workspace (a WhatsApp message). Null when they have none.</summary>
    Task<WorkspaceMembershipInfo?> GetPersonalMembershipAsync(Guid userId, CancellationToken cancellationToken);
}
