using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Categories.Contracts;

/// <summary>Mirrors the kind of a category for modules that must not see the Categories domain.</summary>
public enum CategoryUse
{
    Income,
    Expense
}

/// <summary>
/// What another module may know about a category of the current person.
/// </summary>
/// <param name="ParentName">Set for a subcategory.</param>
/// <param name="Icon">Its own, or its parent's for a subcategory.</param>
/// <param name="Color">Its own, or its parent's for a subcategory; in lower camel case as the API writes it.</param>
public sealed record CategoryInfo(Guid Id, CategoryUse Use, string Name, string? ParentName, string Icon, string Color, bool IsArchived);

/// <summary>
/// Read access to the categories of the current person in the current workspace. A category of someone else
/// is not found, exactly as if it did not exist.
/// </summary>
public interface ICategoryLookup
{
    Task<CategoryInfo?> FindAsync(Guid categoryId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, CategoryInfo>> GetAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken);

    /// <summary>The category and its subcategories, to filter by a category "and everything under it".</summary>
    Task<IReadOnlyList<Guid>> WithSubcategoriesAsync(Guid categoryId, CancellationToken cancellationToken);
}

public sealed record TagInfo(Guid Id, string Name);

/// <summary>The tags of the current person in the current workspace.</summary>
public interface ITagLookup
{
    /// <summary>Turns typed names into tags, creating the ones that do not exist. Fails on a name that cannot be a tag.</summary>
    Task<Result<IReadOnlyList<TagInfo>>> ResolveAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken);

    Task<TagInfo?> FindByNameAsync(string name, CancellationToken cancellationToken);
}
