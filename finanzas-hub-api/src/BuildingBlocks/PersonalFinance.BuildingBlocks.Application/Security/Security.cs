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

public sealed record WorkspaceMembershipInfo(Guid WorkspaceId, Guid UserId, WorkspaceRole Role, string? BaseCurrency, string TimeZone);

/// <summary>The workspace the current use case runs in, resolved once per request from an active membership.</summary>
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
}
