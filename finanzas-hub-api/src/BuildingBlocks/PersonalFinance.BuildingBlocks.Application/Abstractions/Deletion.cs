namespace PersonalFinance.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Locks the row of an aggregate until the command's transaction ends (<c>SELECT … FOR UPDATE</c>). Taken before
/// checking that something is unused and deleting it: whoever adds a child to it meanwhile waits and then fails on
/// its foreign key, instead of the cascade taking the new child away (ADR-024).
/// </summary>
public interface IRowLock
{
    Task LockAsync<TAggregate>(Guid id, CancellationToken cancellationToken) where TAggregate : class;
}

/// <summary>
/// What a module keeps of a workspace, asked before deleting it (ADR-024). Every module with workspace data
/// implements it; Workspaces runs them all without knowing what each one stores.
/// </summary>
public interface IWorkspaceContents
{
    /// <summary>Whether deleting the workspace would lose anything someone registered: then it is only archived.</summary>
    Task<bool> HasDataAsync(Guid workspaceId, CancellationToken cancellationToken);

    /// <summary>Removes what the module created on its own for the workspace, such as the suggested categories.</summary>
    Task RemoveLeftoversAsync(Guid workspaceId, CancellationToken cancellationToken);
}
