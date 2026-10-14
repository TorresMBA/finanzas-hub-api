using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Workspaces.Contracts.Events;

namespace PersonalFinance.Modules.Workspaces.Domain;

public enum WorkspaceStatus
{
    /// <summary>Created, but the owner has not chosen the base currency yet.</summary>
    PendingSetup,
    Active,
    Archived
}

public enum MembershipStatus
{
    Active,
    Removed
}

/// <summary>
/// A directory of people and the container of what they share. Belonging to it gives no access to another
/// member's finances: roles only govern who administers the workspace itself.
/// </summary>
public sealed class Workspace : AggregateRoot
{
    public const int MaxNameLength = 80;
    public const string DefaultTimeZone = "America/Lima";

    private readonly List<Membership> _memberships = [];

    private Workspace()
    {
    }

    private Workspace(Guid id, string name, Guid ownerUserId, DateTimeOffset createdAt) : base(id)
    {
        Name = name;
        OwnerUserId = ownerUserId;
        TimeZone = DefaultTimeZone;
        Status = WorkspaceStatus.PendingSetup;
        CreatedAt = createdAt;
    }

    public string Name { get; private set; } = null!;

    /// <summary>Currency of reports and budgets. Null until onboarding is completed.</summary>
    public string? BaseCurrency { get; private set; }

    public string TimeZone { get; private set; } = null!;

    public WorkspaceStatus Status { get; private set; }

    public Guid OwnerUserId { get; private set; }

    /// <summary>
    /// The workspace a person gets with their account, where their private finances live. There is one per
    /// person and it stays theirs: it cannot be handed over.
    /// </summary>
    public bool IsPersonal { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<Membership> Memberships => _memberships;

    public static Result<Workspace> Create(string? name, Guid ownerUserId, DateTimeOffset now, bool isPersonal = false)
    {
        var normalized = NormalizeName(name);
        if (normalized.IsFailure)
        {
            return normalized.Error;
        }

        var workspace = new Workspace(NewId(), normalized.Value, ownerUserId, now) { IsPersonal = isPersonal };
        workspace._memberships.Add(new Membership(workspace.Id, ownerUserId, WorkspaceRole.Owner, now));
        workspace.Raise(new WorkspaceCreated(workspace.Id, ownerUserId));
        return workspace;
    }

    public Membership? ActiveMembershipOf(Guid userId) =>
        _memberships.SingleOrDefault(membership => membership.UserId == userId && membership.Status == MembershipStatus.Active);

    public Result CompleteOnboarding(string? baseCurrency, string? timeZone)
    {
        if (Status != WorkspaceStatus.PendingSetup)
        {
            return Error.BusinessRule("workspaces.onboarding_already_completed", "The workspace is already set up.");
        }

        var currency = Currency.From(baseCurrency);
        if (currency.IsFailure)
        {
            return currency.Error;
        }

        var zone = NormalizeTimeZone(timeZone);
        if (zone.IsFailure)
        {
            return zone.Error;
        }

        BaseCurrency = currency.Value.Code;
        TimeZone = zone.Value;
        Status = WorkspaceStatus.Active;
        Raise(new OnboardingCompleted(Id, OwnerUserId, BaseCurrency, TimeZone, IsPersonal));
        return Result.Success();
    }

    public Result Update(string? name, string? timeZone)
    {
        var normalized = NormalizeName(name);
        if (normalized.IsFailure)
        {
            return normalized.Error;
        }

        var zone = NormalizeTimeZone(timeZone);
        if (zone.IsFailure)
        {
            return zone.Error;
        }

        Name = normalized.Value;
        TimeZone = zone.Value;
        return Result.Success();
    }

    /// <summary>Adds a person, or brings back one who had left or been removed.</summary>
    public Result<Membership> Join(Guid userId, WorkspaceRole role, DateTimeOffset now)
    {
        if (role == WorkspaceRole.Owner)
        {
            return Error.BusinessRule("workspaces.owner_by_transfer_only", "Ownership is given only by transferring it.");
        }

        var existing = _memberships.SingleOrDefault(membership => membership.UserId == userId);
        if (existing is { Status: MembershipStatus.Active })
        {
            return Error.Conflict("workspaces.already_member", "This person is already a member of the workspace.");
        }

        if (existing is null)
        {
            existing = new Membership(Id, userId, role, now);
            _memberships.Add(existing);
        }
        else
        {
            existing.Rejoin(role, now);
        }

        Raise(new MemberJoined(Id, userId, role));
        return existing;
    }

    public Result ChangeRole(Guid actorUserId, Guid targetUserId, WorkspaceRole role)
    {
        var (actor, target, error) = Resolve(actorUserId, targetUserId);
        if (error is not null)
        {
            return error;
        }

        if (actorUserId == targetUserId)
        {
            return Error.Forbidden("workspaces.cannot_change_own_role", "You cannot change your own role.");
        }

        if (target!.Role == WorkspaceRole.Owner || role == WorkspaceRole.Owner)
        {
            return Error.BusinessRule("workspaces.owner_by_transfer_only", "Ownership changes only by transferring it.");
        }

        // An Admin manages members but cannot create or demote another Admin; only the Owner can.
        if (actor!.Role != WorkspaceRole.Owner && (role == WorkspaceRole.Admin || target.Role == WorkspaceRole.Admin))
        {
            return Error.Forbidden("workspaces.insufficient_role", "Only the owner can grant or remove the Admin role.");
        }

        target.ChangeRole(role);
        return Result.Success();
    }

    /// <summary>Removes a member, or lets a member leave when actor and target are the same person.</summary>
    public Result Remove(Guid actorUserId, Guid targetUserId, DateTimeOffset now)
    {
        var (actor, target, error) = Resolve(actorUserId, targetUserId);
        if (error is not null)
        {
            return error;
        }

        if (target!.Role == WorkspaceRole.Owner)
        {
            return Error.BusinessRule("workspaces.owner_cannot_leave", "The owner cannot leave or be removed. Transfer the ownership first.");
        }

        if (actorUserId != targetUserId)
        {
            if (actor!.Role == WorkspaceRole.Member)
            {
                return Error.Forbidden("workspaces.insufficient_role", "Only an Admin or the owner can remove members.");
            }

            if (actor.Role == WorkspaceRole.Admin && target.Role == WorkspaceRole.Admin)
            {
                return Error.Forbidden("workspaces.insufficient_role", "Only the owner can remove an Admin.");
            }
        }

        target.Remove(now);
        Raise(new MemberRemoved(Id, targetUserId));
        return Result.Success();
    }

    public Result TransferOwnership(Guid actorUserId, Guid targetUserId)
    {
        var (actor, target, error) = Resolve(actorUserId, targetUserId);
        if (error is not null)
        {
            return error;
        }

        if (actor!.Role != WorkspaceRole.Owner)
        {
            return Error.Forbidden("workspaces.insufficient_role", "Only the owner can transfer the ownership.");
        }

        if (actorUserId == targetUserId)
        {
            return Error.BusinessRule("workspaces.already_owner", "You are already the owner.");
        }

        if (IsPersonal)
        {
            // The private finances of the owner live here; they would be left without a place.
            return Error.BusinessRule("workspaces.personal_not_transferable", "A personal workspace cannot be handed over. Create another workspace to share.");
        }

        actor.ChangeRole(WorkspaceRole.Admin);
        target!.ChangeRole(WorkspaceRole.Owner);
        OwnerUserId = targetUserId;
        return Result.Success();
    }

    private (Membership? Actor, Membership? Target, Error? Error) Resolve(Guid actorUserId, Guid targetUserId)
    {
        var actor = ActiveMembershipOf(actorUserId);
        if (actor is null)
        {
            return (null, null, Error.NotFound("workspace.not_found", "The workspace was not found."));
        }

        var target = ActiveMembershipOf(targetUserId);
        return target is null
            ? (actor, null, Error.NotFound("workspaces.member_not_found", "This person is not a member of the workspace."))
            : (actor, target, null);
    }

    private static Result<string> NormalizeName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        return normalized.Length is 0 or > MaxNameLength
            ? Error.Validation("workspaces.name_invalid", $"The workspace name must have between 1 and {MaxNameLength} characters.")
            : normalized;
    }

    private static Result<string> NormalizeTimeZone(string? timeZone)
    {
        var normalized = timeZone?.Trim() ?? string.Empty;
        return normalized.Length == 0 || !TimeZoneInfo.TryFindSystemTimeZoneById(normalized, out _)
            ? Error.Validation("workspaces.time_zone_invalid", "The time zone is not valid. Use an IANA identifier such as America/Lima.")
            : normalized;
    }
}

/// <summary>One person in one workspace. Removing a member keeps the row, so history stays attributable.</summary>
public sealed class Membership : Entity
{
    private Membership()
    {
    }

    internal Membership(Guid workspaceId, Guid userId, WorkspaceRole role, DateTimeOffset joinedAt) : base(NewId())
    {
        WorkspaceId = workspaceId;
        UserId = userId;
        Role = role;
        Status = MembershipStatus.Active;
        JoinedAt = joinedAt;
    }

    public Guid WorkspaceId { get; private set; }

    public Guid UserId { get; private set; }

    public WorkspaceRole Role { get; private set; }

    public MembershipStatus Status { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public DateTimeOffset? RemovedAt { get; private set; }

    internal void ChangeRole(WorkspaceRole role) => Role = role;

    internal void Remove(DateTimeOffset now)
    {
        Status = MembershipStatus.Removed;
        RemovedAt = now;
    }

    internal void Rejoin(WorkspaceRole role, DateTimeOffset now)
    {
        Role = role;
        Status = MembershipStatus.Active;
        JoinedAt = now;
        RemovedAt = null;
    }
}
