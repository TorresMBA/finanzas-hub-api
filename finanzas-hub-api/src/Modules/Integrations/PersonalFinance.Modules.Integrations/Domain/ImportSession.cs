using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Integrations.Domain;

public enum ImportStatus
{
    /// <summary>Read and waiting for the person to look at it. Nothing is in the ledger yet.</summary>
    Review,

    /// <summary>Its movements are in the ledger. It stays as the record of what was imported.</summary>
    Committed
}

/// <summary>The kinds of account an import can open by itself. A credit card needs terms the file does not bring.</summary>
public enum NewAccountType
{
    Bank,
    Cash,
    Wallet
}

/// <summary>Where a line of an import stands. Only the ready ones enter the ledger.</summary>
public enum ImportRecordState
{
    /// <summary>The line could not be read.</summary>
    Invalid,

    /// <summary>The ledger already has a movement like it, and the person has not said it is another one.</summary>
    Duplicate,

    /// <summary>The person left it out.</summary>
    Excluded,

    /// <summary>Its account in the file has no account of the person yet.</summary>
    NeedsAccount,

    /// <summary>It is in a currency other than the one of its account.</summary>
    CurrencyMismatch,

    NeedsCategory,
    Ready,
    Imported
}

/// <summary>
/// A file of movements a person uploaded, from the moment it is read until its movements enter the ledger. It
/// keeps what the person decides while reviewing: which of their accounts each account of the file is, and
/// which of their categories each category of the file is. It belongs to one person, like the ledger it feeds.
/// </summary>
public sealed class ImportSession : AggregateRoot, IOwned
{
    public const int MaxRows = 10_000;
    public const int MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxFileNameLength = 120;

    private readonly List<ImportAccount> _accounts = [];
    private readonly List<ImportCategory> _categories = [];

    private ImportSession()
    {
    }

    private ImportSession(Guid id) : base(id)
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public ImportFormat Format { get; private set; }

    public string FileName { get; private set; } = null!;

    /// <summary>SHA-256 of the file: the same file uploaded twice while in review is the same session.</summary>
    public string FileHash { get; private set; } = null!;

    public ImportStatus Status { get; private set; }

    public int RecordCount { get; private set; }

    /// <summary>How many movements entered the ledger when it was confirmed.</summary>
    public int ImportedCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CommittedAt { get; private set; }

    public IReadOnlyCollection<ImportAccount> Accounts => _accounts.AsReadOnly();

    public IReadOnlyCollection<ImportCategory> Categories => _categories.AsReadOnly();

    public static readonly Error AlreadyCommitted = Error.Conflict("imports.already_committed", "This import was already confirmed. It can no longer be changed.");

    public static ImportSession Start(Guid workspaceId, Guid ownerUserId, ImportFormat format, string? fileName, string fileHash, int recordCount, DateTimeOffset now)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "archivo" : fileName.Trim();
        return new ImportSession(NewId())
        {
            WorkspaceId = workspaceId,
            OwnerUserId = ownerUserId,
            Format = format,
            FileName = name.Length > MaxFileNameLength ? name[^MaxFileNameLength..] : name,
            FileHash = fileHash,
            Status = ImportStatus.Review,
            RecordCount = recordCount,
            CreatedAt = now
        };
    }

    /// <summary>Notes an account of the file. An empty name stands for the single account of a bank statement.</summary>
    public ImportAccount AddAccount(string sourceName, string currency, Guid? accountId)
    {
        var account = new ImportAccount(sourceName, currency, accountId);
        _accounts.Add(account);
        return account;
    }

    public ImportCategory AddCategory(bool isIncome, string? sourceGroup, string sourceName, Guid? categoryId, bool create)
    {
        var category = new ImportCategory(isIncome, sourceGroup ?? string.Empty, sourceName, categoryId, create);
        _categories.Add(category);
        return category;
    }

    public ImportAccount? AccountOf(string? sourceName) =>
        _accounts.FirstOrDefault(account => string.Equals(account.SourceName, sourceName ?? string.Empty, StringComparison.OrdinalIgnoreCase));

    public ImportCategory? CategoryOf(bool isIncome, string? sourceGroup, string sourceName) => _categories.FirstOrDefault(category =>
        category.IsIncome == isIncome
        && string.Equals(category.SourceGroup, sourceGroup ?? string.Empty, StringComparison.OrdinalIgnoreCase)
        && string.Equals(category.SourceName, sourceName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Says which account of the person an account of the file is, or that a new one is to be opened for it.</summary>
    public Result MapAccount(Guid mappingId, Guid? accountId, NewAccountType? createAs)
    {
        if (Status == ImportStatus.Committed)
        {
            return AlreadyCommitted;
        }

        if (_accounts.FirstOrDefault(account => account.Id == mappingId) is not { } mapping)
        {
            return Error.NotFound("imports.mapping_not_found", "That account of the file was not found.");
        }

        if (accountId is not null && createAs is not null)
        {
            return Error.Validation("imports.mapping_invalid", "Choose one of your accounts or a new one, not both.");
        }

        if (createAs is not null && mapping.SourceName.Length == 0)
        {
            return Error.Validation("imports.mapping_invalid", "A statement is imported into one of your accounts.");
        }

        mapping.Set(accountId, createAs);
        return Result.Success();
    }

    public Result MapCategory(Guid mappingId, Guid? categoryId, bool create)
    {
        if (Status == ImportStatus.Committed)
        {
            return AlreadyCommitted;
        }

        if (_categories.FirstOrDefault(category => category.Id == mappingId) is not { } mapping)
        {
            return Error.NotFound("imports.mapping_not_found", "That category of the file was not found.");
        }

        if (create && (categoryId is not null || mapping.SourceName.Length == 0))
        {
            return Error.Validation("imports.mapping_invalid", "Choose one of your categories, or a new one named as in the file.");
        }

        mapping.Set(categoryId, create);
        return Result.Success();
    }

    /// <summary>
    /// Where a line stands with what the person has decided so far.
    /// </summary>
    /// <param name="accountCurrencies">The currency of each account of the person an account of the file may point to.</param>
    public ImportRecordState StateOf(ImportRecord record, IReadOnlyDictionary<Guid, string> accountCurrencies)
    {
        if (record.OperationId is not null)
        {
            return ImportRecordState.Imported;
        }

        if (record.Error is not null)
        {
            return ImportRecordState.Invalid;
        }

        if (record.Excluded ?? record.IsDuplicate)
        {
            return record.IsDuplicate && record.Excluded is null ? ImportRecordState.Duplicate : ImportRecordState.Excluded;
        }

        string? CurrencyOf(ImportAccount? mapping) =>
            mapping is null ? null : mapping.CreateAs is not null ? mapping.Currency : mapping.AccountId is { } id ? accountCurrencies.GetValueOrDefault(id) : null;

        var currency = CurrencyOf(AccountOf(record.SourceAccount));
        var counterCurrency = record.Kind == ImportKind.Transfer ? CurrencyOf(AccountOf(record.SourceCounterAccount)) : currency;
        if (currency is null || counterCurrency is null)
        {
            return ImportRecordState.NeedsAccount;
        }

        // What the file says is in another currency cannot go into the account as it is; nothing is converted here.
        if ((record.Currency is not null && record.Currency != currency) || (currency != counterCurrency && record.CounterAmount is null))
        {
            return ImportRecordState.CurrencyMismatch;
        }

        if (record.Kind is ImportKind.Income or ImportKind.Expense
            && record.CategoryId is null
            && CategoryOf(record.Kind == ImportKind.Income, record.SourceCategoryGroup, record.SourceCategory) is not ({ CategoryId: not null } or { Create: true }))
        {
            return ImportRecordState.NeedsCategory;
        }

        return ImportRecordState.Ready;
    }

    public Result Commit(int importedCount, DateTimeOffset now)
    {
        if (Status == ImportStatus.Committed)
        {
            return AlreadyCommitted;
        }

        Status = ImportStatus.Committed;
        ImportedCount = importedCount;
        CommittedAt = now;
        return Result.Success();
    }
}

/// <summary>An account as the file names it, and which account of the person it is.</summary>
public sealed class ImportAccount
{
    private ImportAccount()
    {
    }

    internal ImportAccount(string sourceName, string currency, Guid? accountId)
    {
        Id = Entity.NewId();
        SourceName = sourceName;
        Currency = currency;
        AccountId = accountId;
    }

    public Guid Id { get; private set; }

    /// <summary>Empty for the single account of a bank statement.</summary>
    public string SourceName { get; private set; } = null!;

    /// <summary>The currency its lines are in: the one a new account is opened with.</summary>
    public string Currency { get; private set; } = null!;

    public Guid? AccountId { get; private set; }

    /// <summary>Set when a new account is to be opened for it on confirming.</summary>
    public NewAccountType? CreateAs { get; private set; }

    internal void Set(Guid? accountId, NewAccountType? createAs)
    {
        AccountId = accountId;
        CreateAs = createAs;
    }

    /// <summary>The account opened for it when the import was confirmed.</summary>
    public void Opened(Guid accountId) => Set(accountId, null);
}

/// <summary>A category as the file names it, and which category of the person it is.</summary>
public sealed class ImportCategory
{
    private ImportCategory()
    {
    }

    internal ImportCategory(bool isIncome, string sourceGroup, string sourceName, Guid? categoryId, bool create)
    {
        Id = Entity.NewId();
        IsIncome = isIncome;
        SourceGroup = sourceGroup;
        SourceName = sourceName;
        CategoryId = categoryId;
        Create = create;
    }

    public Guid Id { get; private set; }

    public bool IsIncome { get; private set; }

    /// <summary>The group the category is under in the file; empty when it has none.</summary>
    public string SourceGroup { get; private set; } = null!;

    /// <summary>Empty for the lines of a file that names no category.</summary>
    public string SourceName { get; private set; } = null!;

    public Guid? CategoryId { get; private set; }

    /// <summary>Set when a category with the name of the file is to be created on confirming.</summary>
    public bool Create { get; private set; }

    internal void Set(Guid? categoryId, bool create)
    {
        CategoryId = categoryId;
        Create = create;
    }

    public void Created(Guid categoryId) => Set(categoryId, false);
}

/// <summary>
/// One line of an imported file. It is staging: it becomes a movement of the ledger only when the import is
/// confirmed, and from then on the ledger is the only truth about it.
/// </summary>
public sealed class ImportRecord : Entity, IOwned
{
    private ImportRecord()
    {
    }

    private ImportRecord(Guid id) : base(id)
    {
    }

    public Guid SessionId { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public int Line { get; private set; }

    public ImportKind Kind { get; private set; }

    public DateOnly? OccurredOn { get; private set; }

    /// <summary>Positive. For an opening balance it carries its sign.</summary>
    public decimal Amount { get; private set; }

    public decimal? CounterAmount { get; private set; }

    public string? Currency { get; private set; }

    public string Description { get; private set; } = null!;

    public string? Note { get; private set; }

    public string SourceAccount { get; private set; } = null!;

    public string? SourceCounterAccount { get; private set; }

    public string? SourceCategoryGroup { get; private set; }

    public string SourceCategory { get; private set; } = null!;

    /// <summary>The labels of the line, separated by commas.</summary>
    public string Labels { get; private set; } = null!;

    /// <summary>Why the line cannot be imported.</summary>
    public string? Error { get; private set; }

    /// <summary>The ledger already has a movement on that account, that day, for that amount.</summary>
    public bool IsDuplicate { get; private set; }

    /// <summary>What the person decided. Null leaves it to the rule: a duplicate stays out, anything else goes in.</summary>
    public bool? Excluded { get; private set; }

    /// <summary>The category the person or a rule chose for this line; it wins over the one of the file.</summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>The movement of the ledger it became.</summary>
    public Guid? OperationId { get; private set; }

    /// <summary>What it adds to its account: negative when the money leaves. For a transfer, what leaves the origin.</summary>
    public decimal SignedAmount => Kind switch
    {
        ImportKind.Income => Amount,
        ImportKind.OpeningBalance => Amount,
        _ => -Amount
    };

    public static ImportRecord From(ImportSession session, ParsedRow row) => new(NewId())
    {
        SessionId = session.Id,
        WorkspaceId = session.WorkspaceId,
        OwnerUserId = session.OwnerUserId,
        Line = row.Line,
        Kind = row.Kind,
        OccurredOn = row.OccurredOn,
        Amount = row.Amount,
        CounterAmount = row.CounterAmount,
        Currency = row.Currency,
        Description = row.Description,
        Note = row.Note,
        SourceAccount = row.Account,
        SourceCounterAccount = row.CounterAccount,
        SourceCategoryGroup = row.CategoryGroup,
        SourceCategory = row.Category,
        Labels = string.Join(',', row.Labels),
        Error = row.Error
    };

    public void MarkDuplicate(bool isDuplicate) => IsDuplicate = isDuplicate;

    public void Classify(Guid? categoryId) => CategoryId = categoryId;

    public void Exclude(bool excluded) => Excluded = excluded;

    public void Imported(Guid operationId) => OperationId = operationId;
}

/// <summary>
/// "When the description contains this, it is of this category". A person writes it once and every later file
/// arrives classified. The longest text that matches wins, being the most specific.
/// </summary>
public sealed class CategorizationRule : AggregateRoot, IOwned
{
    public const int MinPatternLength = 2;
    public const int MaxPatternLength = 60;

    private CategorizationRule()
    {
    }

    private CategorizationRule(Guid id) : base(id)
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public string Pattern { get; private set; } = null!;

    /// <summary>The text as it is compared: without accents or upper case.</summary>
    public string PatternKey { get; private set; } = null!;

    public Guid CategoryId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<CategorizationRule> Create(Guid workspaceId, Guid ownerUserId, string? pattern, Guid categoryId, DateTimeOffset now)
    {
        var rule = new CategorizationRule(NewId()) { WorkspaceId = workspaceId, OwnerUserId = ownerUserId, IsActive = true, CreatedAt = now };
        var updated = rule.Update(pattern, categoryId, true);
        return updated.IsFailure ? updated.Error : rule;
    }

    public Result Update(string? pattern, Guid categoryId, bool isActive)
    {
        var text = string.Join(' ', (pattern ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length is < MinPatternLength or > MaxPatternLength)
        {
            return Error.Validation("imports.rule.pattern_invalid", $"The text of a rule has between {MinPatternLength} and {MaxPatternLength} characters.");
        }

        Pattern = text;
        PatternKey = TextKey.Of(text);
        CategoryId = categoryId;
        IsActive = isActive;
        return Result.Success();
    }

    public bool Matches(string description) => IsActive && TextKey.Of(description).Contains(PatternKey, StringComparison.Ordinal);
}
