namespace PersonalFinance.Modules.Categories.Contracts;

/// <summary>
/// Whether another module points at categories or tags of the current person, asked before deleting one (ADR-024).
/// There are no foreign keys between modules, so without asking, a movement or a budget would point at nothing.
/// </summary>
public interface ICategoryUsage
{
    /// <returns>Those of <paramref name="categoryIds"/> the module uses.</returns>
    Task<IReadOnlySet<Guid>> CategoriesUsedAmongAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken);

    /// <returns>Those of <paramref name="tagIds"/> the module uses. Most modules use none.</returns>
    Task<IReadOnlySet<Guid>> TagsUsedAmongAsync(IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
}
