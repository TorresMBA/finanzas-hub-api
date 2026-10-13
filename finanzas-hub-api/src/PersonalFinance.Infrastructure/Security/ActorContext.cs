using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Infrastructure.Security;

/// <summary>
/// Establishes who is acting and in which workspace. Only host code may call it: the HTTP pipeline for a
/// request, a Worker job on behalf of the owner of the work it runs.
/// </summary>
public interface IActorContextInitializer
{
    void ActAs(Guid userId, ActorType actorType, string source);

    void EnterWorkspace(WorkspaceMembershipInfo membership);
}

/// <summary>One instance per DI scope. Until someone calls <see cref="ActAs"/> the actor is the system with no user.</summary>
internal sealed class ActorContext : ICurrentUser, IWorkspaceContext, IActorContextInitializer
{
    private WorkspaceMembershipInfo? _membership;

    public Guid? UserId { get; private set; }

    public ActorType ActorType { get; private set; } = ActorType.System;

    /// <summary>Where the action came from (Web, Scheduler, Import...). Recorded in the audit trail.</summary>
    public string Source { get; private set; } = "System";

    public Guid? WorkspaceId => _membership?.WorkspaceId;

    public WorkspaceRole? Role => _membership?.Role;

    public string? BaseCurrency => _membership?.BaseCurrency;

    public string? TimeZone => _membership?.TimeZone;

    public void ActAs(Guid userId, ActorType actorType, string source)
    {
        UserId = userId;
        ActorType = actorType;
        Source = source;
    }

    public void EnterWorkspace(WorkspaceMembershipInfo membership)
    {
        if (UserId != membership.UserId)
        {
            throw new InvalidOperationException("A workspace membership can only be entered by the user it belongs to.");
        }

        _membership = membership;
    }

    public Result Require(WorkspaceRole minimum) =>
        _membership is { } membership && membership.Role >= minimum
            ? Result.Success()
            : Error.Forbidden("workspace.insufficient_role", $"This action requires the {minimum} role in the workspace.");
}

/// <summary>
/// Stands in until the Workspaces module (phase 1) provides the real implementation: nobody is a member of anything.
/// </summary>
internal sealed class NoWorkspacesModuleAccess : IWorkspaceAccess
{
    public Task<WorkspaceMembershipInfo?> GetActiveMembershipAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<WorkspaceMembershipInfo?>(null);
}
