using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Ledger.Contracts.Events;

namespace PersonalFinance.Modules.Ledger.Domain;

/// <summary>The accounting mechanics of an operation. Closed: who originated it is a separate dimension.</summary>
public enum OperationKind
{
    Income,
    Expense,
    Refund,
    Transfer,
    Adjustment,
    OpeningBalance
}

public enum OperationStatus
{
    Pending,
    Confirmed,
    Voided
}

public enum EntryDirection
{
    In,
    Out
}

/// <summary>
/// The business fact a person sees as "a transaction". Its entries are what moves the balance of an account.
/// It is never deleted: voiding it takes it out of every sum and keeps it for the record.
/// </summary>
public sealed class Operation : AggregateRoot, IOwned
{
    public const int MaxDescriptionLength = 120;
    public const int MaxMerchantLength = 80;
    public const int MaxNoteLength = 500;
    public const int MaxVoidReasonLength = 200;
    public const int MaxTags = 10;

    private readonly List<Entry> _entries = [];
    private readonly List<OperationTag> _tags = [];

    private Operation()
    {
    }

    private Operation(Guid id) : base(id)
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public OperationKind Kind { get; private set; }

    public OperationStatus Status { get; private set; }

    /// <summary>The business date, in the person's calendar. Not an instant.</summary>
    public DateOnly OccurredOn { get; private set; }

    public string Description { get; private set; } = null!;

    public string? Merchant { get; private set; }

    public string? Note { get; private set; }

    /// <summary>A category of the person, by id: it belongs to another module.</summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>Set when another module created the operation; that module is then the only one that changes it.</summary>
    public string? SourceModule { get; private set; }

    public string? SourceType { get; private set; }

    public Guid? SourceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }

    public string? VoidReason { get; private set; }

    public IReadOnlyCollection<Entry> Entries => _entries.AsReadOnly();

    public IReadOnlyCollection<OperationTag> Tags => _tags.AsReadOnly();

    /// <summary>Pending and confirmed operations count in balances; a voided one counts nowhere.</summary>
    public bool IsEffective => Status != OperationStatus.Voided;

    public bool IsTransaction => Kind is OperationKind.Income or OperationKind.Expense;

    /// <summary>
    /// Tells the other modules that this income or expense was registered, edited or voided. Called once its
    /// entries are valued, so whoever reacts reads it as it stands. Any other kind of operation says nothing.
    /// </summary>
    public void Announce(LedgerOperationChange change)
    {
        if (IsTransaction && _entries is [var entry, ..])
        {
            Raise(new LedgerOperationChanged(Id, WorkspaceId, OwnerUserId, change, Kind == OperationKind.Expense, CategoryId, OccurredOn, entry.Amount, entry.Currency, Description));
        }
    }

    /// <summary>An income or an expense on one account, as a person registers it.</summary>
    public static Result<Operation> PostTransaction(
        OperationKind kind, Account account, decimal amount, Guid categoryId, DateOnly occurredOn, string? description, string? merchant, string? note,
        bool pending, DateTimeOffset now)
    {
        if (kind is not (OperationKind.Income or OperationKind.Expense))
        {
            return Error.Validation("ledger.transaction.kind_invalid", "A transaction is either an income or an expense.");
        }

        var open = account.EnsureOpen();
        if (open.IsFailure)
        {
            return open.Error;
        }

        var text = Texts.Validate(description, merchant, note);
        if (text.IsFailure)
        {
            return text.Error;
        }

        var operation = new Operation(NewId())
        {
            WorkspaceId = account.WorkspaceId,
            OwnerUserId = account.OwnerUserId,
            Kind = kind,
            Status = pending ? OperationStatus.Pending : OperationStatus.Confirmed,
            OccurredOn = occurredOn,
            Description = text.Value.Description,
            Merchant = text.Value.Merchant,
            Note = text.Value.Note,
            CategoryId = categoryId,
            CreatedAt = now
        };
        var entry = Entry.Create(operation, account, kind == OperationKind.Income ? EntryDirection.In : EntryDirection.Out, amount);
        if (entry.IsFailure)
        {
            return entry.Error;
        }

        operation._entries.Add(entry.Value);
        return operation;
    }

    /// <summary>
    /// Money moving between two accounts of the same person: one operation, one entry out and one in. It is
    /// neither an income nor an expense. Between two currencies the person states both amounts; nothing is converted.
    /// </summary>
    /// <param name="amountIn">What arrives. Required when the currencies differ; otherwise it is the amount that left.</param>
    public static Result<Operation> PostTransfer(
        Account from, Account to, decimal amountOut, decimal? amountIn, DateOnly occurredOn, string? description, string? note, DateTimeOffset now)
    {
        if (from.Id == to.Id)
        {
            return Error.Validation("ledger.transfer.same_account", "A transfer needs two different accounts.");
        }

        if (from.OwnerUserId != to.OwnerUserId || from.WorkspaceId != to.WorkspaceId)
        {
            return Error.BusinessRule("ledger.transfer.different_owners", "Both accounts of a transfer belong to the same person.");
        }

        foreach (var account in new[] { from, to })
        {
            var open = account.EnsureOpen();
            if (open.IsFailure)
            {
                return open.Error;
            }
        }

        if (from.Currency == to.Currency)
        {
            if (amountIn is { } stated && stated != amountOut)
            {
                return Error.Validation("ledger.transfer.amount_mismatch", "Between two accounts of the same currency, what arrives is what left.");
            }
        }
        else if (amountIn is null)
        {
            return Error.Validation("ledger.transfer.destination_amount_required", "Between two currencies, state how much arrives in the destination account.");
        }

        var text = Texts.Validate(string.IsNullOrWhiteSpace(description) ? "Transferencia" : description, null, note);
        if (text.IsFailure)
        {
            return text.Error;
        }

        var operation = new Operation(NewId())
        {
            WorkspaceId = from.WorkspaceId,
            OwnerUserId = from.OwnerUserId,
            Kind = OperationKind.Transfer,
            Status = OperationStatus.Confirmed,
            OccurredOn = occurredOn,
            Description = text.Value.Description,
            Note = text.Value.Note,
            CreatedAt = now
        };
        var leaving = Entry.Create(operation, from, EntryDirection.Out, amountOut);
        if (leaving.IsFailure)
        {
            return leaving.Error;
        }

        var arriving = Entry.Create(operation, to, EntryDirection.In, amountIn ?? amountOut);
        if (arriving.IsFailure)
        {
            return arriving.Error;
        }

        operation._entries.Add(leaving.Value);
        operation._entries.Add(arriving.Value);
        return operation;
    }

    /// <summary>The balance an account starts with. It is an entry like any other, so the balance is still only a sum.</summary>
    public static Result<Operation> OpenBalance(Account account, decimal balance, DateOnly occurredOn, DateTimeOffset now) =>
        Single(OperationKind.OpeningBalance, account, balance, occurredOn, "Saldo inicial", now);

    /// <summary>Corrects a balance by the difference to the real one. It never overwrites anything.</summary>
    public static Result<Operation> Adjust(Account account, decimal difference, DateOnly occurredOn, DateTimeOffset now) =>
        Single(OperationKind.Adjustment, account, difference, occurredOn, "Ajuste de saldo", now);

    /// <summary>
    /// Changes a transaction in place. The caller records the before and after in the audit trail. An operation
    /// another module created is not edited here: its amounts belong to that module.
    /// </summary>
    public Result Edit(
        OperationKind kind, Account account, decimal amount, Guid categoryId, DateOnly occurredOn, string? description, string? merchant, string? note)
    {
        var editable = EnsureOwnTransaction("edited");
        if (editable.IsFailure)
        {
            return editable;
        }

        if (kind is not (OperationKind.Income or OperationKind.Expense))
        {
            return Error.Validation("ledger.transaction.kind_invalid", "A transaction is either an income or an expense.");
        }

        var text = Texts.Validate(description, merchant, note);
        if (text.IsFailure)
        {
            return text.Error;
        }

        var current = _entries.Single();
        var direction = kind == OperationKind.Income ? EntryDirection.In : EntryDirection.Out;
        if (current.AccountId != account.Id)
        {
            // Moving to another account is a new entry there; an archived account takes none.
            var open = account.EnsureOpen();
            if (open.IsFailure)
            {
                return open;
            }
        }

        var valid = Entry.Validate(account, amount);
        if (valid.IsFailure)
        {
            return valid;
        }

        Kind = kind;
        OccurredOn = occurredOn;
        Description = text.Value.Description;
        Merchant = text.Value.Merchant;
        Note = text.Value.Note;
        CategoryId = categoryId;
        current.Rewrite(account, direction, amount, occurredOn);
        return Result.Success();
    }

    public Result Confirm()
    {
        if (Status != OperationStatus.Pending)
        {
            return Error.BusinessRule("ledger.transaction.not_pending", "Only a pending transaction can be confirmed.");
        }

        Status = OperationStatus.Confirmed;
        return Result.Success();
    }

    public Result Void(string? reason, DateTimeOffset now)
    {
        if (SourceModule is not null)
        {
            return Error.BusinessRule("ledger.transaction.owned_by_module", "This movement was created by another part of the application and is voided from there.");
        }

        var normalized = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (normalized is { Length: > MaxVoidReasonLength })
        {
            return Error.Validation("ledger.transaction.void_reason_invalid", $"The reason can have at most {MaxVoidReasonLength} characters.");
        }

        if (Status == OperationStatus.Voided)
        {
            return Result.Success();
        }

        Status = OperationStatus.Voided;
        VoidedAt = now;
        VoidReason = normalized;
        _entries.ForEach(entry => entry.Exclude());
        return Result.Success();
    }

    public Result SetTags(IReadOnlyCollection<Guid> tagIds)
    {
        var editable = EnsureOwnTransaction("tagged");
        if (editable.IsFailure)
        {
            return editable;
        }

        var distinct = tagIds.Distinct().ToList();
        if (distinct.Count > MaxTags)
        {
            return Error.Validation("ledger.transaction.too_many_tags", $"A transaction can have at most {MaxTags} tags.");
        }

        _tags.RemoveAll(tag => !distinct.Contains(tag.TagId));
        _tags.AddRange(distinct.Where(tagId => _tags.All(tag => tag.TagId != tagId)).Select(tagId => new OperationTag(tagId)));
        return Result.Success();
    }

    private static Result<Operation> Single(OperationKind kind, Account account, decimal signedAmount, DateOnly occurredOn, string description, DateTimeOffset now)
    {
        var open = account.EnsureOpen();
        if (open.IsFailure)
        {
            return open.Error;
        }

        var operation = new Operation(NewId())
        {
            WorkspaceId = account.WorkspaceId,
            OwnerUserId = account.OwnerUserId,
            Kind = kind,
            Status = OperationStatus.Confirmed,
            OccurredOn = occurredOn,
            Description = description,
            CreatedAt = now
        };
        var entry = Entry.Create(operation, account, signedAmount >= 0 ? EntryDirection.In : EntryDirection.Out, Math.Abs(signedAmount));
        if (entry.IsFailure)
        {
            return entry.Error;
        }

        operation._entries.Add(entry.Value);
        return operation;
    }

    private Result EnsureOwnTransaction(string verb)
    {
        if (!IsTransaction)
        {
            return Error.BusinessRule("ledger.transaction.not_editable", $"Only an income or an expense can be {verb}. Void this movement and register it again.");
        }

        if (SourceModule is not null)
        {
            return Error.BusinessRule("ledger.transaction.owned_by_module", $"This movement was created by another part of the application and cannot be {verb} here.");
        }

        return Status == OperationStatus.Voided
            ? Error.BusinessRule("ledger.transaction.voided", $"A voided transaction cannot be {verb}.")
            : Result.Success();
    }

    private static class Texts
    {
        public static Result<(string Description, string? Merchant, string? Note)> Validate(string? description, string? merchant, string? note)
        {
            var validDescription = (description ?? string.Empty).Trim();
            if (validDescription.Length is 0 or > MaxDescriptionLength)
            {
                return Error.Validation("ledger.transaction.description_invalid", $"The description must have between 1 and {MaxDescriptionLength} characters.");
            }

            var validMerchant = string.IsNullOrWhiteSpace(merchant) ? null : merchant.Trim();
            if (validMerchant is { Length: > MaxMerchantLength })
            {
                return Error.Validation("ledger.transaction.merchant_invalid", $"The merchant can have at most {MaxMerchantLength} characters.");
            }

            var validNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            if (validNote is { Length: > MaxNoteLength })
            {
                return Error.Validation("ledger.transaction.note_invalid", $"The note can have at most {MaxNoteLength} characters.");
            }

            return (validDescription, validMerchant, validNote);
        }
    }
}

/// <summary>
/// One movement on one account, in that account's currency. The amount is positive and the direction explicit.
/// It repeats the owner, the date and whether it counts from its operation, so a balance is one indexed sum
/// over this table alone, filtered by owner like everything private.
/// </summary>
public sealed class Entry : Entity, IOwned
{
    private Entry()
    {
    }

    private Entry(Guid id) : base(id)
    {
    }

    public Guid OperationId { get; private set; }

    public Guid AccountId { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public EntryDirection Direction { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = null!;

    /// <summary>Computed by the database: the amount, negative when the direction is out.</summary>
    public decimal SignedAmount { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public bool IsEffective { get; private set; }

    // What the entry was worth in the workspace's base currency when it was registered, so a report of a past
    // month does not change when the rate does. Empty for an entry already in the base currency, and for one
    // registered when the person had no rate for its date: reports value that one at the latest rate.
    public decimal? BaseAmount { get; private set; }

    public decimal? FxRate { get; private set; }

    public DateOnly? FxRateDate { get; private set; }

    internal static Result<Entry> Create(Operation operation, Account account, EntryDirection direction, decimal amount)
    {
        var valid = Validate(account, amount);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        return new Entry(NewId())
        {
            OperationId = operation.Id,
            AccountId = account.Id,
            WorkspaceId = operation.WorkspaceId,
            OwnerUserId = operation.OwnerUserId,
            Direction = direction,
            Amount = amount,
            Currency = account.Currency,
            OccurredOn = operation.OccurredOn,
            IsEffective = operation.IsEffective
        };
    }

    internal void Rewrite(Account account, EntryDirection direction, decimal amount, DateOnly occurredOn)
    {
        AccountId = account.Id;
        Currency = account.Currency;
        Direction = direction;
        Amount = amount;
        OccurredOn = occurredOn;
    }

    internal void Exclude() => IsEffective = false;

    /// <summary>Records what the entry is worth in the base currency, at the rate that applied on its date.</summary>
    /// <param name="rate">Null when there is nothing to record: the entry is in the base currency, or no rate applies.</param>
    internal void ValueWith(ExchangeRate? rate)
    {
        BaseAmount = rate?.Convert(Amount);
        FxRate = rate?.Rate;
        FxRateDate = rate?.AsOf;
    }

    internal static Result Validate(Account account, decimal amount)
    {
        if (amount <= 0)
        {
            return Error.Validation("ledger.amount_invalid", "The amount must be greater than zero.");
        }

        var currency = BuildingBlocks.Domain.Currency.From(account.Currency).Value;
        var money = Money.Create(amount, currency);
        if (money.IsFailure)
        {
            return money.Error;
        }

        return amount > 999_999_999_999.99m ? Error.Validation("ledger.amount_invalid", "The amount is too large.") : Result.Success();
    }
}

/// <summary>One tag of an operation, by id: tags belong to the Categories module.</summary>
public sealed class OperationTag
{
    private OperationTag()
    {
    }

    internal OperationTag(Guid tagId)
    {
        TagId = tagId;
    }

    public Guid TagId { get; private set; }
}
