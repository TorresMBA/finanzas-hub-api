using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Categories.Domain;

public enum CategoryKind
{
    Income,
    Expense
}

/// <summary>The colours the design offers for a category. The frontend maps each one to its tokens.</summary>
public enum CategoryColor
{
    Green,
    Blue,
    Violet,
    Amber,
    Red
}

/// <summary>
/// A way to classify income or expenses. It belongs to one person: two members of a workspace each have their
/// own. There are two levels: a category and its subcategories. A subcategory takes its kind, icon, colour and
/// suggested tags from its parent, so it stores none of them.
/// </summary>
public sealed class Category : AggregateRoot, IOwned
{
    public const int MaxNameLength = 60;
    public const int MaxIconLength = 40;
    public const int MaxTags = 20;
    public const string DefaultIcon = "category";

    private readonly List<CategoryTag> _tags = [];

    private Category()
    {
    }

    private Category(Guid id) : base(id)
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public CategoryKind Kind { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>The name as it is compared: two siblings cannot differ only in upper and lower case.</summary>
    public string NameKey { get; private set; } = null!;

    public Guid? ParentId { get; private set; }

    public string? Icon { get; private set; }

    public CategoryColor? Color { get; private set; }

    /// <summary>Which entry of the default catalog this came from. It lets seeding run twice without duplicating.</summary>
    public string? SystemKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    /// <summary>The tags this category suggests for its transactions. Only a root category has them.</summary>
    public IReadOnlyCollection<CategoryTag> Tags => _tags.AsReadOnly();

    public bool IsRoot => ParentId is null;

    public bool IsArchived => ArchivedAt is not null;

    public static Result<Category> CreateRoot(
        Guid workspaceId, Guid ownerUserId, CategoryKind kind, string? name, string? icon, CategoryColor? color, DateTimeOffset now, string? systemKey = null)
    {
        var validName = ValidateName(name);
        if (validName.IsFailure)
        {
            return validName.Error;
        }

        var validIcon = ValidateIcon(icon ?? DefaultIcon);
        if (validIcon.IsFailure)
        {
            return validIcon.Error;
        }

        return new Category(NewId())
        {
            WorkspaceId = workspaceId,
            OwnerUserId = ownerUserId,
            Kind = kind,
            Name = validName.Value,
            NameKey = KeyOf(validName.Value),
            Icon = validIcon.Value,
            Color = color ?? CategoryColor.Green,
            SystemKey = systemKey,
            CreatedAt = now
        };
    }

    public Result<Category> AddSubcategory(string? name, DateTimeOffset now, string? systemKey = null)
    {
        if (!IsRoot)
        {
            return Error.BusinessRule("categories.max_depth", "A subcategory cannot have subcategories of its own.");
        }

        if (IsArchived)
        {
            return Archived;
        }

        var validName = ValidateName(name);
        if (validName.IsFailure)
        {
            return validName.Error;
        }

        return new Category(NewId())
        {
            WorkspaceId = WorkspaceId,
            OwnerUserId = OwnerUserId,
            Kind = Kind,
            Name = validName.Value,
            NameKey = KeyOf(validName.Value),
            ParentId = Id,
            SystemKey = systemKey,
            CreatedAt = now
        };
    }

    /// <summary>Renames it and, for a root category, changes how it looks. The kind never changes.</summary>
    public Result Update(string? name, string? icon, CategoryColor? color)
    {
        if (IsArchived)
        {
            return Archived;
        }

        var validName = ValidateName(name);
        if (validName.IsFailure)
        {
            return validName.Error;
        }

        if (IsRoot)
        {
            var validIcon = ValidateIcon(icon ?? Icon!);
            if (validIcon.IsFailure)
            {
                return validIcon.Error;
            }

            Icon = validIcon.Value;
            Color = color ?? Color;
        }

        Name = validName.Value;
        NameKey = KeyOf(validName.Value);
        return Result.Success();
    }

    public Result SetTags(IReadOnlyCollection<Guid> tagIds)
    {
        if (!IsRoot)
        {
            return Error.BusinessRule("categories.tags_on_subcategory", "A subcategory uses the tags of its category.");
        }

        if (IsArchived)
        {
            return Archived;
        }

        var distinct = tagIds.Distinct().ToList();
        if (distinct.Count > MaxTags)
        {
            return Error.Validation("categories.too_many_tags", $"A category can suggest at most {MaxTags} tags.");
        }

        _tags.RemoveAll(tag => !distinct.Contains(tag.TagId));
        _tags.AddRange(distinct.Where(tagId => _tags.All(tag => tag.TagId != tagId)).Select(tagId => new CategoryTag(tagId)));
        return Result.Success();
    }

    /// <summary>Used when the tag itself is archived, so it also works on an archived category.</summary>
    public void RemoveTag(Guid tagId) => _tags.RemoveAll(tag => tag.TagId == tagId);

    /// <summary>A category is never deleted: what was classified with it keeps pointing to it.</summary>
    public void Archive(DateTimeOffset now) => ArchivedAt ??= now;

    public void Restore() => ArchivedAt = null;

    private static Error Archived => Error.BusinessRule("categories.archived", "An archived category cannot be changed. Restore it first.");

    private static string KeyOf(string name) => name.ToLowerInvariant();

    private static Result<string> ValidateName(string? name)
    {
        var normalized = string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length is 0 or > MaxNameLength
            ? Error.Validation("categories.name_invalid", $"A category name must have between 1 and {MaxNameLength} characters.")
            : normalized;
    }

    /// <summary>An icon is the name of a glyph of the icon font, never markup.</summary>
    private static Result<string> ValidateIcon(string icon)
    {
        var normalized = icon.Trim().ToLowerInvariant();
        return normalized.Length is 0 or > MaxIconLength || !normalized.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')
            ? Error.Validation("categories.icon_invalid", "The icon is not valid.")
            : normalized;
    }
}

/// <summary>One suggested tag of a category.</summary>
public sealed class CategoryTag
{
    private CategoryTag()
    {
    }

    internal CategoryTag(Guid tagId)
    {
        TagId = tagId;
    }

    public Guid TagId { get; private set; }
}
