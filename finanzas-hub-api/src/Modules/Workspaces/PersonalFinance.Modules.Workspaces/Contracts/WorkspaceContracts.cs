using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Workspaces.Contracts;

/// <summary>A person who belongs to a workspace in use, with what is needed to act as them there.</summary>
public sealed record ActiveMembership(Guid WorkspaceId, Guid UserId, WorkspaceRole Role, string? BaseCurrency, string TimeZone);

/// <summary>
/// For background work that has to act as each person in turn. It says who belongs where and nothing about
/// their money.
/// </summary>
public interface IWorkspaceDirectory
{
    /// <summary>Every active membership of every workspace in use.</summary>
    Task<IReadOnlyList<ActiveMembership>> ListActiveMembershipsAsync(CancellationToken cancellationToken);
}
