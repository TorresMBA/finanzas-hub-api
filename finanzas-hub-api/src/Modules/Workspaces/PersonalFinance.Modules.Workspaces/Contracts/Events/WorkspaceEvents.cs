using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Workspaces.Contracts.Events;

public sealed record WorkspaceCreated(Guid WorkspaceId, Guid OwnerUserId) : IDomainEvent;

/// <summary>The owner chose the base currency and the time zone; the workspace is ready to hold data.</summary>
/// <param name="HasOwnBooks">
/// Whether private finances are kept in this workspace: the personal one of its owner, or a business or another
/// set of books. A shared Personal workspace has none.
/// </param>
public sealed record OnboardingCompleted(
    Guid WorkspaceId, Guid OwnerUserId, string BaseCurrency, string TimeZone, WorkspaceKind Kind, bool HasOwnBooks) : IDomainEvent;

public sealed record MemberJoined(Guid WorkspaceId, Guid UserId, WorkspaceRole Role) : IDomainEvent;

/// <summary>Raised both when a member is removed and when a member leaves.</summary>
public sealed record MemberRemoved(Guid WorkspaceId, Guid UserId) : IDomainEvent;
