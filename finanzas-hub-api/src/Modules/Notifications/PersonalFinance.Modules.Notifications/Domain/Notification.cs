using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Notifications.Contracts;

namespace PersonalFinance.Modules.Notifications.Domain;

/// <summary>A notice in the inbox of a person. It belongs to the person, not to a workspace, and only they read it.</summary>
public sealed class Notification : AggregateRoot
{
    public const int MaxTitleLength = 120;
    public const int MaxBodyLength = 500;
    public const int MaxLinkLength = 200;
    public const int MaxDedupeKeyLength = 200;

    private Notification()
    {
    }

    private Notification(Guid id) : base(id)
    {
    }

    public Guid UserId { get; private set; }

    public Guid? WorkspaceId { get; private set; }

    public NotificationCategory Category { get; private set; }

    public NotificationSeverity Severity { get; private set; }

    public string Title { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    public string? Link { get; private set; }

    public string? DedupeKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public static Notification Create(NotificationRequest request, DateTimeOffset now) => new(NewId())
    {
        UserId = request.UserId,
        WorkspaceId = request.WorkspaceId,
        Category = request.Category,
        Severity = request.Severity,
        Title = Cut(request.Title, MaxTitleLength),
        Body = Cut(request.Body, MaxBodyLength),
        Link = request.Link,
        DedupeKey = request.DedupeKey,
        CreatedAt = now
    };

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length];
}
