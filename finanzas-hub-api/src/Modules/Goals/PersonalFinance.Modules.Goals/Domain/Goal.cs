using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Goals.Domain;

public enum GoalStatus
{
    Active,

    /// <summary>Finished and put away.</summary>
    Archived,

    /// <summary>Given up. No money moves: each participant withdraws what is theirs.</summary>
    Cancelled
}

public enum ParticipantRole
{
    Owner,
    Participant
}

public enum ParticipantStatus
{
    Active,
    Left
}

public enum ContributionType
{
    Contribution,
    Withdrawal
}

/// <summary>Someone who saves for the goal. Only an active participant sees it.</summary>
public sealed class GoalParticipant
{
    private GoalParticipant()
    {
    }

    internal GoalParticipant(Guid userId, ParticipantRole role, DateTimeOffset now)
    {
        UserId = userId;
        Role = role;
        Status = ParticipantStatus.Active;
        JoinedAt = now;
    }

    public Guid UserId { get; private set; }

    public ParticipantRole Role { get; private set; }

    public ParticipantStatus Status { get; internal set; }

    /// <summary>
    /// The account of the ledger where this person keeps what they put aside for the goal, by id. It is theirs
    /// alone: nobody else in the goal sees it. Null until their first contribution.
    /// </summary>
    public Guid? FundAccountId { get; internal set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public bool IsActive => Status == ParticipantStatus.Active;
}

/// <summary>
/// What one participant put into the goal or took out of it. Each is tied to one transfer of the ledger between
/// an account of that person and their fund, so the two can always be reconciled.
/// </summary>
public sealed class GoalContribution
{
    private GoalContribution()
    {
    }

    internal GoalContribution(Guid id, Guid userId, ContributionType type, decimal amount, DateOnly occurredOn, Guid ledgerOperationId, DateTimeOffset now)
    {
        Id = id;
        UserId = userId;
        Type = type;
        Amount = amount;
        OccurredOn = occurredOn;
        LedgerOperationId = ledgerOperationId;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public ContributionType Type { get; private set; }

    /// <summary>Positive, in the currency of the goal.</summary>
    public decimal Amount { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public Guid LedgerOperationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set when it was voided, together with its transfer in the ledger. A voided one stays, and adds nothing.</summary>
    public DateTimeOffset? VoidedAt { get; internal set; }

    public bool IsVoided => VoidedAt is not null;

    /// <summary>What it adds to the goal: negative for a withdrawal, nothing once voided.</summary>
    public decimal Signed
    {
        get
        {
            if (IsVoided)
            {
                return 0;
            }

            return Type == ContributionType.Contribution ? Amount : -Amount;
        }
    }
}

/// <summary>
/// An amount to save by a date, by one person or by several members of a workspace. It is shared by
/// participation, not by membership: who is not a participant does not know it exists. Each participant keeps
/// their own money in their own fund; the goal only adds up what each one put in.
/// </summary>
public sealed class Goal : AggregateRoot
{
    public const int MaxNameLength = 60;
    public const decimal MaxAmount = 999_999_999.99m;

    private static readonly Error OwnerRequired = Error.Forbidden("goals.owner_required", "Only the owner of the goal can do this.");
    private static readonly Error ParticipantNotFound = Error.NotFound("goals.participant.not_found", "That person does not take part in the goal.");
    private static readonly Error NotActive = Error.BusinessRule("goals.not_active", "The goal is closed: it takes no more contributions.");

    private readonly List<GoalParticipant> _participants = [];
    private readonly List<GoalContribution> _contributions = [];

    private Goal()
    {
    }

    private Goal(Guid id) : base(id)
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public string Name { get; private set; } = null!;

    public decimal TargetAmount { get; private set; }

    public string Currency { get; private set; } = null!;

    public DateOnly? TargetDate { get; private set; }

    public GoalStatus Status { get; private set; }

    /// <summary>The first time what was saved reached the target. It is told once and never unset.</summary>
    public DateTimeOffset? AchievedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<GoalParticipant> Participants => _participants.AsReadOnly();

    public IReadOnlyCollection<GoalContribution> Contributions => _contributions.AsReadOnly();

    /// <summary>What everyone put in minus what they took out. Never stored: it is the sum of the contributions.</summary>
    public decimal Saved => _contributions.Sum(contribution => contribution.Signed);

    public bool IsShared => _participants.Count(participant => participant.IsActive) > 1;

    public static Result<Goal> Create(
        Guid workspaceId, Guid creatorUserId, string? name, decimal targetAmount, string currency, DateOnly? targetDate, DateTimeOffset now)
    {
        var goal = new Goal(NewId())
        {
            WorkspaceId = workspaceId, CreatedByUserId = creatorUserId, Currency = currency, Status = GoalStatus.Active, CreatedAt = now
        };
        var described = goal.Describe(name, targetAmount, targetDate);
        if (described.IsFailure)
        {
            return described.Error;
        }

        goal._participants.Add(new GoalParticipant(creatorUserId, ParticipantRole.Owner, now));
        return goal;
    }

    public decimal SavedBy(Guid userId) => _contributions.Where(contribution => contribution.UserId == userId).Sum(contribution => contribution.Signed);

    public GoalParticipant? ActiveParticipant(Guid userId) => _participants.FirstOrDefault(participant => participant.UserId == userId && participant.IsActive);

    public bool IsOwner(Guid userId) => ActiveParticipant(userId)?.Role == ParticipantRole.Owner;

    public Result Update(Guid actorUserId, string? name, decimal targetAmount, DateOnly? targetDate) =>
        IsOwner(actorUserId) ? Describe(name, targetAmount, targetDate) : OwnerRequired;

    /// <summary>Adds a member of the workspace to the goal. Someone who had left comes back with what they had put in.</summary>
    public Result AddParticipant(Guid actorUserId, Guid userId, DateTimeOffset now)
    {
        if (!IsOwner(actorUserId))
        {
            return OwnerRequired;
        }

        if (Status != GoalStatus.Active)
        {
            return NotActive;
        }

        var existing = _participants.FirstOrDefault(participant => participant.UserId == userId);
        if (existing is { IsActive: true })
        {
            return Error.Conflict("goals.participant.already_in", "That person already takes part in the goal.");
        }

        if (existing is not null)
        {
            existing.Status = ParticipantStatus.Active;
        }
        else
        {
            _participants.Add(new GoalParticipant(userId, ParticipantRole.Participant, now));
        }

        return Result.Success();
    }

    /// <summary>
    /// Takes someone out of the goal, by the owner or by themselves. Their money is in their own fund, so they
    /// withdraw it first: once out they could no longer reach the goal to do it.
    /// </summary>
    public Result RemoveParticipant(Guid actorUserId, Guid userId)
    {
        if (ActiveParticipant(userId) is not { } participant)
        {
            return ParticipantNotFound;
        }

        if (actorUserId != userId && !IsOwner(actorUserId))
        {
            return OwnerRequired;
        }

        if (participant.Role == ParticipantRole.Owner)
        {
            return Error.BusinessRule("goals.owner_cannot_leave", "The owner does not leave the goal: they cancel or archive it.");
        }

        if (SavedBy(userId) > 0)
        {
            return Error.BusinessRule("goals.participant.has_balance", "That person still has money in the goal. They must withdraw it first.");
        }

        participant.Status = ParticipantStatus.Left;
        return Result.Success();
    }

    /// <summary>Whether this person may put this amount in. Asked before any money moves.</summary>
    public Result<GoalParticipant> CanContribute(Guid userId, decimal amount)
    {
        if (ActiveParticipant(userId) is not { } participant)
        {
            return ParticipantNotFound;
        }

        if (Status != GoalStatus.Active)
        {
            return NotActive;
        }

        return ValidAmount(amount) ? participant : AmountInvalid;
    }

    /// <summary>Whether this person may take this amount out: no more than what they put in themselves. A closed goal still lets them.</summary>
    public Result<GoalParticipant> CanWithdraw(Guid userId, decimal amount)
    {
        if (ActiveParticipant(userId) is not { } participant)
        {
            return ParticipantNotFound;
        }

        if (!ValidAmount(amount))
        {
            return AmountInvalid;
        }

        return amount > SavedBy(userId)
            ? Error.BusinessRule("goals.withdrawal.exceeds_own", "You can withdraw at most what you put into the goal yourself.")
            : participant;
    }

    /// <summary>Remembers where a participant keeps their part. Set once, with their first contribution.</summary>
    public void SetFund(GoalParticipant participant, Guid fundAccountId) => participant.FundAccountId ??= fundAccountId;

    /// <summary>Writes down a contribution or a withdrawal whose money already moved in the ledger.</summary>
    /// <returns>True when this is the contribution with which the goal reached its target for the first time.</returns>
    public bool Record(Guid contributionId, Guid userId, ContributionType type, decimal amount, DateOnly occurredOn, Guid ledgerOperationId, DateTimeOffset now)
    {
        _contributions.Add(new GoalContribution(contributionId, userId, type, amount, occurredOn, ledgerOperationId, now));
        if (AchievedAt is not null || Saved < TargetAmount)
        {
            return false;
        }

        AchievedAt = now;
        return true;
    }

    /// <summary>
    /// Undoes a contribution made by mistake: only its author, and only while what they still have in the goal
    /// covers it (what was already withdrawn or spent cannot be put back). The ledger transfer is voided with it.
    /// </summary>
    public Result<GoalContribution> VoidContribution(Guid userId, Guid contributionId, DateTimeOffset now)
    {
        if (ActiveParticipant(userId) is null)
        {
            return ParticipantNotFound;
        }

        var contribution = _contributions.FirstOrDefault(candidate => candidate.Id == contributionId && candidate.UserId == userId);
        if (contribution is null || contribution.Type != ContributionType.Contribution)
        {
            return Error.NotFound("goals.contribution.not_found", "The contribution was not found.");
        }

        if (!contribution.IsVoided && SavedBy(userId) < contribution.Amount)
        {
            return Error.BusinessRule(
                "goals.contribution.already_used", "Part of that money was already withdrawn or spent, so the contribution cannot be voided.");
        }

        contribution.VoidedAt ??= now;
        return contribution;
    }

    /// <summary>Archives or cancels the goal. Nothing moves: what each one put in stays in their fund until they withdraw it.</summary>
    public Result Close(Guid actorUserId, GoalStatus status)
    {
        if (!IsOwner(actorUserId))
        {
            return OwnerRequired;
        }

        if (Status != GoalStatus.Active || status == GoalStatus.Active)
        {
            return Error.BusinessRule("goals.already_closed", "The goal was already closed.");
        }

        Status = status;
        return Result.Success();
    }

    private static Error AmountInvalid => Error.Validation("goals.amount_invalid", "The amount must be greater than zero, with two decimals at most.");

    private static bool ValidAmount(decimal amount) => amount > 0 && amount <= MaxAmount && decimal.Round(amount, 2) == amount;

    private Result Describe(string? name, decimal targetAmount, DateOnly? targetDate)
    {
        var text = name?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaxNameLength)
        {
            return Error.Validation("goals.name_invalid", $"The name is required and can have at most {MaxNameLength} characters.");
        }

        if (!ValidAmount(targetAmount))
        {
            return Error.Validation("goals.target_invalid", "The target must be greater than zero, with two decimals at most.");
        }

        Name = text;
        TargetAmount = targetAmount;
        TargetDate = targetDate;
        return Result.Success();
    }
}
