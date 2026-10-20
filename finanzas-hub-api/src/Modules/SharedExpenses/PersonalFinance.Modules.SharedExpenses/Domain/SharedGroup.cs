using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.SharedExpenses.Domain;

public enum GroupStatus
{
    Active,
    Archived
}

public enum GroupRole
{
    Admin,
    Member
}

public enum GroupMemberStatus
{
    Active,
    Left
}

public enum SharedExpenseStatus
{
    /// <summary>Nobody has repaid anything yet.</summary>
    Pending,
    PartiallyPaid,

    /// <summary>Everyone repaid their part.</summary>
    Settled,
    Cancelled
}

public sealed class GroupMember
{
    private GroupMember()
    {
    }

    internal GroupMember(Guid userId, GroupRole role, DateTimeOffset now)
    {
        UserId = userId;
        Role = role;
        Status = GroupMemberStatus.Active;
        JoinedAt = now;
    }

    public Guid UserId { get; private set; }

    public GroupRole Role { get; private set; }

    public GroupMemberStatus Status { get; internal set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public bool IsActive => Status == GroupMemberStatus.Active;
}

/// <summary>What one person owes of an expense, and how much of it they already repaid to whoever paid.</summary>
public sealed class ExpenseShare
{
    private ExpenseShare()
    {
    }

    internal ExpenseShare(Guid userId, decimal amount, decimal paidAmount)
    {
        UserId = userId;
        Amount = amount;
        PaidAmount = paidAmount;
    }

    public Guid UserId { get; private set; }

    public decimal Amount { get; private set; }

    public decimal PaidAmount { get; internal set; }

    public decimal Pending => Amount - PaidAmount;
}

/// <summary>
/// Something one member paid for several. The parts are fixed when it is registered; who paid is owed the part
/// of each of the others, and their own part is paid from the start.
/// </summary>
public sealed class SharedExpense
{
    private readonly List<ExpenseShare> _shares = [];

    private SharedExpense()
    {
    }

    internal SharedExpense(
        Guid id, string description, decimal totalAmount, Guid paidByUserId, DateOnly occurredOn, SplitType splitType, Guid createdByUserId, DateTimeOffset now)
    {
        Id = id;
        Description = description;
        TotalAmount = totalAmount;
        PaidByUserId = paidByUserId;
        OccurredOn = occurredOn;
        SplitType = splitType;
        CreatedByUserId = createdByUserId;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public string Description { get; private set; } = null!;

    public decimal TotalAmount { get; private set; }

    public Guid PaidByUserId { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public SplitType SplitType { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; internal set; }

    /// <summary>
    /// The expense in the ledger of who paid, and its category, by id: they belong to other modules and only who
    /// paid ever sees them. The category is what a repayment is registered against.
    /// </summary>
    public Guid? PayerOperationId { get; internal set; }

    public Guid? PayerCategoryId { get; internal set; }

    public IReadOnlyCollection<ExpenseShare> Shares => _shares.AsReadOnly();

    public bool IsCancelled => CancelledAt is not null;

    public SharedExpenseStatus Status =>
        IsCancelled ? SharedExpenseStatus.Cancelled
        : _shares.All(share => share.Pending == 0) ? SharedExpenseStatus.Settled
        : _shares.Any(share => share.UserId != PaidByUserId && share.PaidAmount > 0) ? SharedExpenseStatus.PartiallyPaid
        : SharedExpenseStatus.Pending;

    public ExpenseShare? ShareOf(Guid userId) => _shares.FirstOrDefault(share => share.UserId == userId);

    internal void AddShare(ExpenseShare share) => _shares.Add(share);
}

/// <summary>How much of a payment went to each expense.</summary>
public sealed class SettlementAllocation
{
    private SettlementAllocation()
    {
    }

    internal SettlementAllocation(Guid expenseId, decimal amount)
    {
        ExpenseId = expenseId;
        Amount = amount;
    }

    public Guid ExpenseId { get; private set; }

    public decimal Amount { get; private set; }
}

/// <summary>One member repaying another what they owe them in the group. It says who paid, how much and when.</summary>
public sealed class Settlement
{
    private readonly List<SettlementAllocation> _allocations = [];

    private Settlement()
    {
    }

    internal Settlement(Guid id, Guid fromUserId, Guid toUserId, decimal amount, DateOnly occurredOn, Guid recordedByUserId, DateTimeOffset now)
    {
        Id = id;
        FromUserId = fromUserId;
        ToUserId = toUserId;
        Amount = amount;
        OccurredOn = occurredOn;
        RecordedByUserId = recordedByUserId;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    /// <summary>Who pays: the one who owed.</summary>
    public Guid FromUserId { get; private set; }

    public Guid ToUserId { get; private set; }

    public decimal Amount { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? VoidedAt { get; internal set; }

    /// <summary>The movement in the ledger of each side, by id, once that side registered it. Each is theirs alone.</summary>
    public Guid? FromOperationId { get; internal set; }

    public Guid? ToOperationId { get; internal set; }

    public IReadOnlyCollection<SettlementAllocation> Allocations => _allocations.AsReadOnly();

    public bool IsVoided => VoidedAt is not null;

    internal void Allocate(Guid expenseId, decimal amount) => _allocations.Add(new SettlementAllocation(expenseId, amount));
}

/// <summary>
/// People of a workspace who split expenses among themselves. It is shared by membership of the group: who is
/// not in it does not know it exists. The group holds no money: it only keeps who paid what for whom and who
/// repaid; every balance is worked out from that, never stored.
/// </summary>
public sealed class SharedGroup : AggregateRoot
{
    public const int MaxNameLength = 60;
    public const int MaxDescriptionLength = 120;
    public const int MaxIconLength = 40;
    public const string DefaultIcon = "group";
    public const decimal MaxAmount = 999_999_999.99m;

    /// <summary>How long after registering a payment it can still be voided. Afterwards it stands.</summary>
    public static readonly TimeSpan VoidWindow = TimeSpan.FromDays(1);

    /// <summary>How many payments a person can void in a group within a day: a mistake is corrected, a payment is not played with.</summary>
    public const int MaxVoidsPerDay = 3;

    private static readonly Error AdminRequired = Error.Forbidden("shared.admin_required", "Only who created the group can do this.");
    private static readonly Error NotAMember = Error.NotFound("shared.member.not_found", "That person is not in the group.");
    private static readonly Error Archived = Error.BusinessRule("shared.group.archived", "The group is archived.");

    private readonly List<GroupMember> _members = [];
    private readonly List<SharedExpense> _expenses = [];
    private readonly List<Settlement> _settlements = [];

    private SharedGroup()
    {
    }

    private SharedGroup(Guid id) : base(id)
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>Every expense and every payment of the group is in this currency.</summary>
    public string Currency { get; private set; } = null!;

    public GroupStatus Status { get; private set; }

    /// <summary>The name of a glyph of the icon font, never markup.</summary>
    public string Icon { get; private set; } = DefaultIcon;

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<GroupMember> Members => _members.AsReadOnly();

    public IReadOnlyCollection<SharedExpense> Expenses => _expenses.AsReadOnly();

    public IReadOnlyCollection<Settlement> Settlements => _settlements.AsReadOnly();

    public static Result<SharedGroup> Create(Guid workspaceId, Guid creatorUserId, string? name, string currency, DateTimeOffset now, string? icon = null)
    {
        var text = name?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaxNameLength)
        {
            return Error.Validation("shared.group.name_invalid", $"The name is required and can have at most {MaxNameLength} characters.");
        }

        var validIcon = ValidateIcon(icon ?? DefaultIcon);
        if (validIcon.IsFailure)
        {
            return validIcon.Error;
        }

        var group = new SharedGroup(NewId())
        {
            WorkspaceId = workspaceId, CreatedByUserId = creatorUserId, Name = text, Currency = currency, Status = GroupStatus.Active, Icon = validIcon.Value,
            CreatedAt = now
        };
        group._members.Add(new GroupMember(creatorUserId, GroupRole.Admin, now));
        return group;
    }

    public Result ChangeIcon(Guid actorUserId, string? icon)
    {
        if (!IsAdmin(actorUserId))
        {
            return AdminRequired;
        }

        var validIcon = ValidateIcon(icon ?? DefaultIcon);
        if (validIcon.IsFailure)
        {
            return validIcon.Error;
        }

        Icon = validIcon.Value;
        return Result.Success();
    }

    private static Result<string> ValidateIcon(string icon)
    {
        var normalized = icon.Trim().ToLowerInvariant();
        return normalized.Length is 0 or > MaxIconLength || !normalized.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')
            ? Error.Validation("shared.group.icon_invalid", "The icon is not valid.")
            : normalized;
    }

    public GroupMember? ActiveMember(Guid userId) => _members.FirstOrDefault(member => member.UserId == userId && member.IsActive);

    public bool IsAdmin(Guid userId) => ActiveMember(userId)?.Role == GroupRole.Admin;

    /// <summary>What one member still owes another: the unpaid parts of the first in what the second paid.</summary>
    public decimal Owed(Guid fromUserId, Guid toUserId) =>
        _expenses.Where(expense => !expense.IsCancelled && expense.PaidByUserId == toUserId).Sum(expense => expense.ShareOf(fromUserId)?.Pending ?? 0m);

    /// <summary>What the group owes this person minus what this person owes: positive when they are owed.</summary>
    public decimal NetOf(Guid userId) =>
        _expenses.Where(expense => !expense.IsCancelled)
            .Sum(expense => expense.PaidByUserId == userId
                ? expense.Shares.Where(share => share.UserId != userId).Sum(share => share.Pending)
                : -(expense.ShareOf(userId)?.Pending ?? 0m));

    public bool HasPendingDebts => _expenses.Any(expense => expense.Status is SharedExpenseStatus.Pending or SharedExpenseStatus.PartiallyPaid);

    public Result AddMember(Guid actorUserId, Guid userId, DateTimeOffset now)
    {
        if (!IsAdmin(actorUserId))
        {
            return AdminRequired;
        }

        if (Status != GroupStatus.Active)
        {
            return Archived;
        }

        var existing = _members.FirstOrDefault(member => member.UserId == userId);
        if (existing is { IsActive: true })
        {
            return Error.Conflict("shared.member.already_in", "That person is already in the group.");
        }

        if (existing is not null)
        {
            existing.Status = GroupMemberStatus.Active;
        }
        else
        {
            _members.Add(new GroupMember(userId, GroupRole.Member, now));
        }

        return Result.Success();
    }

    /// <summary>Takes someone out, by who created the group or by themselves. Nobody goes with something still to pay or to be paid.</summary>
    public Result RemoveMember(Guid actorUserId, Guid userId)
    {
        if (ActiveMember(userId) is not { } member)
        {
            return NotAMember;
        }

        if (actorUserId != userId && !IsAdmin(actorUserId))
        {
            return AdminRequired;
        }

        if (member.Role == GroupRole.Admin)
        {
            return Error.BusinessRule("shared.admin_cannot_leave", "Who created the group does not leave it: they archive it.");
        }

        var involved = _expenses.Any(expense => !expense.IsCancelled
            && ((expense.PaidByUserId == userId && expense.Status != SharedExpenseStatus.Settled) || (expense.ShareOf(userId)?.Pending ?? 0m) > 0));
        if (involved)
        {
            return Error.BusinessRule("shared.member.has_balance", "That person still owes or is owed in the group. It has to be settled first.");
        }

        member.Status = GroupMemberStatus.Left;
        return Result.Success();
    }

    public Result Archive(Guid actorUserId)
    {
        if (!IsAdmin(actorUserId))
        {
            return AdminRequired;
        }

        if (HasPendingDebts)
        {
            return Error.BusinessRule("shared.group.has_pending", "There are expenses still to be repaid. Settle them before archiving the group.");
        }

        Status = GroupStatus.Archived;
        return Result.Success();
    }

    /// <summary>Registers what one member paid for several. Who paid may or may not be among those it is split among.</summary>
    public Result<SharedExpense> AddExpense(
        Guid actorUserId, string? description, decimal totalAmount, Guid paidByUserId, DateOnly occurredOn, SplitType splitType,
        IReadOnlyList<SplitInput> participants, DateTimeOffset now)
    {
        if (ActiveMember(actorUserId) is null)
        {
            return NotAMember;
        }

        if (Status != GroupStatus.Active)
        {
            return Archived;
        }

        var text = description?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaxDescriptionLength)
        {
            return Error.Validation("shared.expense.description_invalid", $"The description is required and can have at most {MaxDescriptionLength} characters.");
        }

        if (!ValidAmount(totalAmount))
        {
            return AmountInvalid;
        }

        if (ActiveMember(paidByUserId) is null || participants.Any(participant => ActiveMember(participant.UserId) is null))
        {
            return Error.BusinessRule("shared.expense.not_members", "Who paid and everyone it is split among must be in the group.");
        }

        var split = SplitCalculator.Split(totalAmount, splitType, participants);
        if (split.IsFailure)
        {
            return split.Error;
        }

        var expense = new SharedExpense(NewId(), text, totalAmount, paidByUserId, occurredOn, splitType, actorUserId, now);
        foreach (var (userId, amount) in split.Value)
        {
            // Who paid owes their own part to nobody.
            expense.AddShare(new ExpenseShare(userId, amount, userId == paidByUserId ? amount : 0m));
        }

        _expenses.Add(expense);
        return expense;
    }

    /// <summary>Takes an expense back, by who registered it or who paid. Not once someone repaid part of it: those payments are voided first.</summary>
    public Result<SharedExpense> CancelExpense(Guid actorUserId, Guid expenseId, DateTimeOffset now)
    {
        if (_expenses.FirstOrDefault(candidate => candidate.Id == expenseId) is not { } expense)
        {
            return Error.NotFound("shared.expense.not_found", "The expense was not found.");
        }

        if (ActiveMember(actorUserId) is null || (actorUserId != expense.CreatedByUserId && actorUserId != expense.PaidByUserId))
        {
            return Error.Forbidden("shared.expense.not_yours", "Only who registered the expense or who paid it can cancel it.");
        }

        if (expense.IsCancelled)
        {
            return expense;
        }

        if (expense.Shares.Any(share => share.UserId != expense.PaidByUserId && share.PaidAmount > 0))
        {
            return Error.BusinessRule("shared.expense.has_payments", "Someone already repaid part of this expense. Void those payments first.");
        }

        expense.CancelledAt = now;
        return expense;
    }

    /// <summary>
    /// The expenses one member still owes another a part of, oldest first: what <see cref="Owed"/> adds up, in the
    /// order a payment covers them.
    /// </summary>
    public IEnumerable<SharedExpense> OwedExpenses(Guid fromUserId, Guid toUserId) =>
        _expenses.Where(expense => !expense.IsCancelled && expense.PaidByUserId == toUserId && expense.ShareOf(fromUserId) is { Pending: > 0 })
            .OrderBy(expense => expense.OccurredOn).ThenBy(expense => expense.CreatedAt);

    /// <summary>
    /// One member repays another. It can be registered by either of the two and never exceeds what is owed. The
    /// money goes to the oldest expenses first, so each one ends up saying how much of it was repaid; or, when it
    /// names an expense, to that one alone and never beyond what is still owed of it.
    /// </summary>
    public Result<Settlement> RecordSettlement(
        Guid actorUserId, Guid fromUserId, Guid toUserId, decimal amount, DateOnly occurredOn, DateTimeOffset now, Guid? expenseId = null)
    {
        if (ActiveMember(actorUserId) is null || (actorUserId != fromUserId && actorUserId != toUserId))
        {
            return Error.Forbidden("shared.settlement.not_a_party", "A payment is registered by who pays or by who is paid.");
        }

        if (fromUserId == toUserId || !ValidAmount(amount))
        {
            return AmountInvalid;
        }

        var owed = OwedExpenses(fromUserId, toUserId).ToList();
        if (expenseId is { } id)
        {
            owed = owed.Where(expense => expense.Id == id).ToList();
            if (owed.Count == 0)
            {
                return Error.BusinessRule("shared.settlement.expense_not_owed", "Nothing is owed of that expense between these two people.");
            }
        }

        if (amount > owed.Sum(expense => expense.ShareOf(fromUserId)!.Pending))
        {
            return Error.BusinessRule("shared.settlement.exceeds_debt", "The payment is more than what is owed.");
        }

        var settlement = new Settlement(NewId(), fromUserId, toUserId, amount, occurredOn, actorUserId, now);
        var left = amount;
        foreach (var expense in owed)
        {
            if (left == 0 || expense.ShareOf(fromUserId) is not { Pending: > 0 } share)
            {
                continue;
            }

            var part = Math.Min(left, share.Pending);
            share.PaidAmount += part;
            settlement.Allocate(expense.Id, part);
            left -= part;
        }

        _settlements.Add(settlement);
        return settlement;
    }

    /// <summary>
    /// Undoes a payment, by who registered it and while it is recent: what it had covered is owed again. A voided
    /// payment stays voided; paying again is a new payment.
    /// </summary>
    public Result<Settlement> VoidSettlement(Guid actorUserId, Guid settlementId, DateTimeOffset now)
    {
        if (_settlements.FirstOrDefault(candidate => candidate.Id == settlementId) is not { } settlement)
        {
            return SettlementNotFound;
        }

        if (ActiveMember(actorUserId) is null || actorUserId != settlement.RecordedByUserId)
        {
            return Error.Forbidden("shared.settlement.not_yours", "Only who registered the payment can void it.");
        }

        if (settlement.IsVoided)
        {
            return Error.Conflict("shared.settlement.already_voided", "That payment was already voided.");
        }

        if (!CanStillVoid(settlement, now))
        {
            return Error.BusinessRule("shared.settlement.void_window_passed", "A payment can be voided within a day of being registered. This one stands.");
        }

        if (VoidsLeft(actorUserId, now) == 0)
        {
            return Error.BusinessRule("shared.settlement.too_many_voids", "You already voided several payments today in this group. Try again tomorrow.");
        }

        foreach (var allocation in settlement.Allocations)
        {
            var share = _expenses.First(expense => expense.Id == allocation.ExpenseId).ShareOf(settlement.FromUserId)!;
            share.PaidAmount -= allocation.Amount;
        }

        settlement.VoidedAt = now;
        return settlement;
    }

    public static bool CanStillVoid(Settlement settlement, DateTimeOffset now) => !settlement.IsVoided && now <= settlement.CreatedAt + VoidWindow;

    /// <summary>How many more payments this person can void in the group today.</summary>
    public int VoidsLeft(Guid userId, DateTimeOffset now) =>
        Math.Max(0, MaxVoidsPerDay - _settlements.Count(settlement => settlement.RecordedByUserId == userId && settlement.VoidedAt > now - VoidWindow));

    /// <summary>The payment, for one of its two sides that has not tied it to a movement of their own ledger yet.</summary>
    public Result<Settlement> SettlementToLink(Guid actorUserId, Guid settlementId)
    {
        if (_settlements.FirstOrDefault(candidate => candidate.Id == settlementId) is not { } settlement)
        {
            return SettlementNotFound;
        }

        if (ActiveMember(actorUserId) is null || (actorUserId != settlement.FromUserId && actorUserId != settlement.ToUserId))
        {
            return Error.Forbidden("shared.settlement.not_a_party", "A payment is registered by who pays or by who is paid.");
        }

        if (settlement.IsVoided)
        {
            return Error.BusinessRule("shared.settlement.voided", "That payment was voided.");
        }

        return (actorUserId == settlement.FromUserId ? settlement.FromOperationId : settlement.ToOperationId) is not null
            ? Error.Conflict("shared.settlement.already_linked", "You already registered this payment in one of your accounts.")
            : settlement;
    }

    /// <summary>
    /// The expense, for who paid it to put it in one of their accounts later, when they did not when registering it.
    /// Nobody else writes it in their ledger.
    /// </summary>
    public Result<SharedExpense> ExpenseToLink(Guid actorUserId, Guid expenseId)
    {
        if (_expenses.FirstOrDefault(candidate => candidate.Id == expenseId) is not { } expense)
        {
            return Error.NotFound("shared.expense.not_found", "The expense was not found.");
        }

        if (ActiveMember(actorUserId) is null || actorUserId != expense.PaidByUserId)
        {
            return Error.Forbidden("shared.expense.account_of_payer_only", "Only who paid can register the expense in one of their accounts.");
        }

        if (expense.IsCancelled)
        {
            return Error.BusinessRule("shared.expense.cancelled", "That expense was cancelled.");
        }

        return expense.PayerOperationId is not null
            ? Error.Conflict("shared.expense.already_linked", "You already registered this expense in one of your accounts.")
            : expense;
    }

    /// <summary>The category the first expense this payment covered was registered under by who paid it, if they did.</summary>
    public Guid? CategoryRepaidBy(Settlement settlement) =>
        settlement.Allocations.Select(allocation => _expenses.First(expense => expense.Id == allocation.ExpenseId).PayerCategoryId).FirstOrDefault(id => id is not null);

    public void LinkPayerOperation(SharedExpense expense, Guid operationId, Guid categoryId)
    {
        expense.PayerOperationId = operationId;
        expense.PayerCategoryId = categoryId;
    }

    public void LinkOperation(Settlement settlement, Guid userId, Guid operationId)
    {
        if (userId == settlement.FromUserId)
        {
            settlement.FromOperationId = operationId;
        }
        else
        {
            settlement.ToOperationId = operationId;
        }
    }

    private static Error AmountInvalid => Error.Validation("shared.amount_invalid", "The amount must be greater than zero, with two decimals at most.");

    private static Error SettlementNotFound => Error.NotFound("shared.settlement.not_found", "The payment was not found.");

    private static bool ValidAmount(decimal amount) => amount > 0 && amount <= MaxAmount && decimal.Round(amount, 2) == amount;
}
