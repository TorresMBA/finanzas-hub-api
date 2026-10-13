namespace PersonalFinance.BuildingBlocks.Application.Abstractions;

/// <summary>
/// One business action worth tracing: who and when come from the execution context, the rest from the caller.
/// </summary>
/// <param name="Module">Owning module, for example <c>ledger</c>.</param>
/// <param name="Action">Dotted action name, for example <c>ledger.operation.voided</c>.</param>
public sealed record AuditEntry(string Module, string Action, string ResourceType, Guid ResourceId)
{
    public Guid? WorkspaceId { get; init; }

    /// <summary>Whose data was touched, so a resource's history can be read with the resource's own authorization.</summary>
    public Guid? OwnerUserId { get; init; }

    /// <summary>Groups entries of a collaborative resource, for example a goal or a shared group.</summary>
    public string? ScopeType { get; init; }

    public Guid? ScopeId { get; init; }

    /// <summary>Only the fields that changed.</summary>
    public object? Before { get; init; }

    public object? After { get; init; }
}

/// <summary>
/// Functional audit trail. Entries are written in the same transaction as the change they describe,
/// so there is never an audited change that did not happen or a change without its audit entry.
/// </summary>
public interface IAuditLog
{
    void Record(AuditEntry entry);
}
