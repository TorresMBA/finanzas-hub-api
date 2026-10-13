using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Application.Abstractions;
using PersonalFinance.Modules.Categories.Application.Tags;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Categories.Domain;

namespace PersonalFinance.Modules.Categories.Application;

/// <summary>What the ledger and the modules after it read from Categories. Every query goes through the owner filter.</summary>
internal sealed class CategoryLookup(ICategoriesDb db) : ICategoryLookup
{
    public async Task<CategoryInfo?> FindAsync(Guid categoryId, CancellationToken cancellationToken) =>
        (await GetAsync([categoryId], cancellationToken)).GetValueOrDefault(categoryId);

    public async Task<IReadOnlyDictionary<Guid, CategoryInfo>> GetAsync(IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken)
    {
        if (categoryIds.Count == 0)
        {
            return new Dictionary<Guid, CategoryInfo>();
        }

        var found = await db.Categories.AsNoTracking().Where(category => categoryIds.Contains(category.Id)).ToListAsync(cancellationToken);
        var parentIds = found.Where(category => category.ParentId != null).Select(category => category.ParentId!.Value).Distinct().ToList();
        var parents = await db.Categories.AsNoTracking().Where(category => parentIds.Contains(category.Id)).ToDictionaryAsync(category => category.Id, cancellationToken);
        return found.ToDictionary(category => category.Id, category =>
        {
            var root = category.ParentId is { } parentId ? parents[parentId] : category;
            return new CategoryInfo(
                category.Id,
                root.Id,
                category.Kind == CategoryKind.Income ? CategoryUse.Income : CategoryUse.Expense,
                category.Name,
                category.IsRoot ? null : root.Name,
                root.Icon!,
                root.Color!.Value.ToString().ToLowerInvariant(),
                category.IsArchived);
        });
    }

    public async Task<IReadOnlyList<Guid>> WithSubcategoriesAsync(Guid categoryId, CancellationToken cancellationToken) =>
        await db.Categories.AsNoTracking()
            .Where(category => category.Id == categoryId || category.ParentId == categoryId)
            .Select(category => category.Id)
            .ToListAsync(cancellationToken);
}

internal sealed class TagLookup(ICategoriesDb db, TagResolver resolver, CategoryFamilies families) : ITagLookup
{
    public async Task<Result<IReadOnlyList<TagInfo>>> ResolveAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken)
    {
        if (families.Owner is not (var workspaceId, var userId))
        {
            return CategoryErrors.Unauthenticated;
        }

        var tags = await resolver.ResolveAsync(workspaceId, userId, names, cancellationToken);
        return tags.IsFailure ? tags.Error : tags.Value.Select(tag => new TagInfo(tag.Id, tag.Name)).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken)
    {
        if (tagIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var names = await db.Tags.AsNoTracking().Where(tag => tagIds.Contains(tag.Id)).ToDictionaryAsync(tag => tag.Id, tag => tag.Name, cancellationToken);

        // Tags created by the command in progress are not in the database yet.
        foreach (var added in db.Tags.Local.Where(tag => tagIds.Contains(tag.Id)))
        {
            names[added.Id] = added.Name;
        }

        return names;
    }

    public async Task<TagInfo?> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        var normalized = Tag.Normalize(name);
        if (normalized.IsFailure)
        {
            return null;
        }

        return await db.Tags.AsNoTracking()
            .Where(tag => tag.Name == normalized.Value)
            .Select(tag => new TagInfo(tag.Id, tag.Name))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
