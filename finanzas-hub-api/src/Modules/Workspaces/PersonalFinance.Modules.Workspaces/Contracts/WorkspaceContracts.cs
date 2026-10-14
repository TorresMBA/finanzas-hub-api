using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Workspaces.Contracts;

/// <summary>A person who belongs to a workspace in use, with what is needed to act as them there.</summary>
/// <param name="IsPersonal">Whether the workspace is the personal one of this person.</param>
public sealed record ActiveMembership(Guid WorkspaceId, Guid UserId, WorkspaceRole Role, string? BaseCurrency, string TimeZone, bool IsPersonal);

/// <summary>Someone who belongs to a workspace, as another member may know them: who they are, nothing about their money.</summary>
public sealed record WorkspaceMember(Guid UserId, string DisplayName);

/// <summary>Who belongs where. It says nothing about anybody's money.</summary>
public interface IWorkspaceDirectory
{
    /// <summary>The active members of one workspace, by name.</summary>
    Task<IReadOnlyList<WorkspaceMember>> ListMembersAsync(Guid workspaceId, CancellationToken cancellationToken);

    /// <summary>Every active membership of every workspace in use, for background work that has to act as each person in turn.</summary>
    Task<IReadOnlyList<ActiveMembership>> ListActiveMembershipsAsync(CancellationToken cancellationToken);
}
