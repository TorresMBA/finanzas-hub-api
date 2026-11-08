namespace PersonalFinance.Modules.Ledger.Contracts;

/// <summary>
/// Whether another module points at accounts of the current person, asked before deleting one (ADR-024). There are
/// no foreign keys between modules, so without asking, a schedule or an import would be left pointing at nothing.
/// </summary>
public interface IAccountUsage
{
    /// <returns>Those of <paramref name="accountIds"/> the module uses.</returns>
    Task<IReadOnlySet<Guid>> UsedAmongAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);
}
