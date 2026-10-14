using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
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

        // A category created earlier in this same command is not in the database yet: it is found among the tracked ones.
        var found = (await db.Categories.AsNoTracking().Where(category => categoryIds.Contains(category.Id)).ToListAsync(cancellationToken))
            .Concat(db.Categories.Local.Where(category => categoryIds.Contains(category.Id))).DistinctBy(category => category.Id).ToList();
        var parentIds = found.Where(category => category.ParentId != null).Select(category => category.ParentId!.Value).Distinct().ToList();
        var parents = (await db.Categories.AsNoTracking().Where(category => parentIds.Contains(category.Id)).ToListAsync(cancellationToken))
            .Concat(db.Categories.Local.Where(category => parentIds.Contains(category.Id))).DistinctBy(category => category.Id).ToDictionary(category => category.Id);
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

internal sealed class CategoryDirectory(ICategoriesDb db, CategoryFamilies families, IAuditLog audit, IClock clock) : ICategoryDirectory
{
    // Loaded once per request and kept up to date with what this request creates, which is not in the database yet.
    private List<Category>? _all;

    public async Task<IReadOnlyList<CategoryInfo>> ListAsync(CancellationToken cancellationToken)
    {
        var all = await AllAsync(cancellationToken);
        var roots = all.Where(category => category.IsRoot).ToDictionary(category => category.Id);
        return all
            .Where(category => !category.IsArchived && roots.TryGetValue(category.ParentId ?? category.Id, out var root) && !root.IsArchived)
            .Select(category =>
            {
                var root = roots[category.ParentId ?? category.Id];
                return new CategoryInfo(
                    category.Id, root.Id, root.Kind == CategoryKind.Income ? CategoryUse.Income : CategoryUse.Expense, category.Name,
                    category.IsRoot ? null : root.Name, root.Icon!, root.Color!.Value.ToString().ToLowerInvariant(), false);
            })
            .OrderBy(category => category.ParentName ?? category.Name, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(category => category.ParentName is null ? string.Empty : category.Name, StringComparer.InvariantCultureIgnoreCase)
            .ToList();
    }

    public async Task<Result<Guid>> EnsureAsync(CategoryUse use, string name, string? parentName, CancellationToken cancellationToken)
    {
        if (families.Owner is not (var workspaceId, var userId))
        {
            return CategoryErrors.Unauthenticated;
        }

        var all = await AllAsync(cancellationToken);
        var kind = use == CategoryUse.Income ? CategoryKind.Income : CategoryKind.Expense;
        var rootName = string.IsNullOrWhiteSpace(parentName) ? name : parentName;
        var root = all.FirstOrDefault(category => category is { IsRoot: true, IsArchived: false } && category.Kind == kind && category.NameKey == KeyOf(rootName));
        if (root is null)
        {
            // A name is taken across both kinds: "Otros" of income cannot sit next to "Otros" of expenses.
            var taken = all.Any(category => category is { IsRoot: true, IsArchived: false } && category.NameKey == KeyOf(rootName));
            var created = Category.CreateRoot(
                workspaceId, userId, kind, taken ? $"{rootName.Trim()} ({(kind == CategoryKind.Income ? "ingresos" : "gastos")})" : rootName, null, null, clock.UtcNow);
            if (created.IsFailure)
            {
                return created.Error;
            }

            root = all.FirstOrDefault(category => category is { IsRoot: true, IsArchived: false } && category.NameKey == created.Value.NameKey) ?? Add(created.Value, userId);
            if (root.Kind != kind)
            {
                return CategoryErrors.NameTaken;
            }
        }

        if (string.IsNullOrWhiteSpace(parentName) || KeyOf(parentName) == KeyOf(name))
        {
            return root.Id;
        }

        if (all.FirstOrDefault(category => category.ParentId == root.Id && !category.IsArchived && category.NameKey == KeyOf(name)) is { } existing)
        {
            return existing.Id;
        }

        var subcategory = root.AddSubcategory(name, clock.UtcNow);
        return subcategory.IsFailure ? subcategory.Error : Add(subcategory.Value, userId).Id;
    }

    private static string KeyOf(string name) => name.Trim().ToLowerInvariant();

    private Category Add(Category category, Guid userId)
    {
        db.Categories.Add(category);
        _all!.Add(category);
        audit.Record(new AuditEntry("categories", "categories.category.created", "category", category.Id)
        {
            OwnerUserId = userId,
            After = new { category.Name, Kind = category.Kind.ToString(), category.ParentId, Origin = "import" }
        });
        return category;
    }

    private async Task<List<Category>> AllAsync(CancellationToken cancellationToken) => _all ??= await db.Categories.ToListAsync(cancellationToken);
}
