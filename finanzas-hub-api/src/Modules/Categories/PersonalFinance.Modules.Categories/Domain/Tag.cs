using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Categories.Domain;

/// <summary>
/// A free label, private to one person. A category suggests some; from the ledger on, a transaction carries
/// its own. The name is its identity for the person, so it is stored normalized.
/// </summary>
public sealed class Tag : AggregateRoot, IOwned
{
    public const int MaxNameLength = 40;

    private Tag()
    {
    }

    private Tag(Guid id) : base(id)
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public string Name { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public static Result<Tag> Create(Guid workspaceId, Guid ownerUserId, string? name, DateTimeOffset now)
    {
        var normalized = Normalize(name);
        if (normalized.IsFailure)
        {
            return normalized.Error;
        }

        return new Tag(NewId())
        {
            WorkspaceId = workspaceId,
            OwnerUserId = ownerUserId,
            Name = normalized.Value,
            CreatedAt = now
        };
    }

    /// <summary>"  #Recibo Honorarios " becomes "recibo-honorarios": lower case, no leading #, hyphens for spaces.</summary>
    public static Result<string> Normalize(string? name)
    {
        var words = (name ?? string.Empty).Trim().TrimStart('#').ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var normalized = string.Join('-', words);
        return normalized.Length is 0 or > MaxNameLength || !normalized.All(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            ? Error.Validation("categories.tag.name_invalid", $"A tag has between 1 and {MaxNameLength} letters, digits or hyphens.")
            : normalized;
    }

    /// <summary>Transactions and categories point to the tag by its id, so they all follow the new name.</summary>
    public Result Rename(string? name)
    {
        var normalized = Normalize(name);
        if (normalized.IsFailure)
        {
            return normalized.Error;
        }

        Name = normalized.Value;
        return Result.Success();
    }

    public void Archive(DateTimeOffset now) => ArchivedAt ??= now;

    public void Restore() => ArchivedAt = null;
}
