using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Ledger.Domain;

/// <summary>Where the money is. The accounts other modules open for themselves (a goal's fund, an investment) come with those modules.</summary>
public enum AccountType
{
    Bank,
    Cash,
    Wallet,
    CreditCard,

    /// <summary>What a person has put aside for a goal. Opened and moved by the goals, never by hand.</summary>
    GoalFund,

    /// <summary>The capital a person has put into one investment. Opened and moved by the investments, never by hand.</summary>
    Investment
}

/// <summary>
/// A place that holds money of one person, in one currency. It has no balance of its own: the balance is the
/// sum of its entries, so there is nothing here that could disagree with the ledger.
/// </summary>
public sealed class Account : AggregateRoot, IOwned
{
    public const int MaxNameLength = 60;
    public const int MaxInstitutionLength = 60;

    private Account()
    {
    }

    private Account(Guid id) : base(id)
    {
    }

    public Guid WorkspaceId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>The name as it is compared: two active accounts cannot differ only in upper and lower case.</summary>
    public string NameKey { get; private set; } = null!;

    public AccountType Type { get; private set; }

    /// <summary>ISO 4217 code. Fixed for the life of the account: every entry on it is in this currency.</summary>
    public string Currency { get; private set; } = null!;

    /// <summary>The bank or the wallet provider, as the person calls it.</summary>
    public string? Institution { get; private set; }

    /// <summary>
    /// Whether what the account holds counts in the person's net worth. An archived account never counts,
    /// whatever this says.
    /// </summary>
    public bool IncludeInNetWorth { get; private set; }

    /// <summary>Shown first among the main accounts of the home screen. A preference: it changes nothing in the books.</summary>
    public bool IsFavorite { get; private set; }

    /// <summary>Present exactly when the account is a credit card.</summary>
    public CreditCardProfile? CreditCard { get; private set; }

    public bool IsCreditCard => Type == AccountType.CreditCard;

    public bool IsGoalFund => Type == AccountType.GoalFund;

    public bool IsInvestment => Type == AccountType.Investment;

    /// <summary>An account another module opens and moves: the fund of a goal or the capital of an investment.</summary>
    public bool IsManaged => IsGoalFund || IsInvestment;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public static Result<Account> Open(
        Guid workspaceId, Guid ownerUserId, string? name, AccountType type, string? currency, string? institution, DateTimeOffset now,
        CreditCardTerms? card = null, bool includeInNetWorth = true)
    {
        var validName = ValidateName(name);
        if (validName.IsFailure)
        {
            return validName.Error;
        }

        var validCurrency = BuildingBlocks.Domain.Currency.From(currency);
        if (validCurrency.IsFailure)
        {
            return validCurrency.Error;
        }

        var validInstitution = ValidateInstitution(institution);
        if (validInstitution.IsFailure)
        {
            return validInstitution.Error;
        }

        var profile = ProfileFor(type, card, validCurrency.Value.Code);
        if (profile.IsFailure)
        {
            return profile.Error;
        }

        return new Account(NewId())
        {
            WorkspaceId = workspaceId,
            OwnerUserId = ownerUserId,
            Name = validName.Value,
            NameKey = validName.Value.ToLowerInvariant(),
            Type = type,
            Currency = validCurrency.Value.Code,
            Institution = validInstitution.Value,
            IncludeInNetWorth = includeInNetWorth,
            CreditCard = profile.Value,
            CreatedAt = now
        };
    }

    /// <summary>Type and currency are not here on purpose: changing them would change what its entries mean.</summary>
    /// <param name="card">The line and the days of a credit card. Left out, a card keeps the ones it has.</param>
    public Result Update(string? name, string? institution, bool includeInNetWorth, CreditCardTerms? card = null)
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

        var validInstitution = ValidateInstitution(institution);
        if (validInstitution.IsFailure)
        {
            return validInstitution.Error;
        }

        if (card is not null)
        {
            if (CreditCard is null)
            {
                return NotACard;
            }

            var changed = CreditCard.Change(card, Currency);
            if (changed.IsFailure)
            {
                return changed;
            }
        }

        Name = validName.Value;
        NameKey = validName.Value.ToLowerInvariant();
        Institution = validInstitution.Value;
        IncludeInNetWorth = includeInNetWorth;
        return Result.Success();
    }

    public Result MarkFavorite(bool isFavorite)
    {
        if (IsArchived)
        {
            return Archived;
        }

        IsFavorite = isFavorite;
        return Result.Success();
    }

    /// <summary>An archived account keeps its entries and its balance; it only stops being offered.</summary>
    public void Archive(DateTimeOffset now) => ArchivedAt ??= now;

    public void Restore() => ArchivedAt = null;

    /// <summary>Fails when the account cannot take new entries.</summary>
    public Result EnsureOpen() => IsArchived ? Archived : Result.Success();

    /// <summary>
    /// Fails for the fund of a goal: its balance is what the person contributed to the goal, so money enters and
    /// leaves it only as a contribution or a withdrawal.
    /// </summary>
    public Result EnsureMovedByHand()
    {
        var managed = EnsureNotManaged();
        return managed.IsFailure ? managed : EnsureOpen();
    }

    /// <summary>Why an account cannot be moved by hand, or success when it can.</summary>
    public Result EnsureNotManaged()
    {
        if (IsGoalFund)
        {
            return ManagedByGoal;
        }

        return IsInvestment ? ManagedByInvestment : Result.Success();
    }

    public static Error ManagedByGoal =>
        Error.BusinessRule("ledger.account.managed_by_goal", "This account holds the money of a goal. Contribute to the goal or withdraw from it instead.");

    public static Error ManagedByInvestment =>
        Error.BusinessRule("ledger.account.managed_by_investment", "This account holds the capital of an investment. Contribute to it or redeem it from Investments.");

    private static Error Archived => Error.BusinessRule("ledger.account.archived", "The account is archived. Restore it first.");

    private static Error NotACard => Error.Validation("ledger.card.not_a_card", "Only a credit card has a credit line and a payment day.");

    private static Result<CreditCardProfile?> ProfileFor(AccountType type, CreditCardTerms? card, string currency)
    {
        if (type != AccountType.CreditCard)
        {
            return card is null ? Result.Success<CreditCardProfile?>(null) : NotACard;
        }

        if (card is null)
        {
            return Error.Validation("ledger.card.terms_required", "A credit card needs its credit line and its payment day.");
        }

        var profile = CreditCardProfile.Create(card, currency);
        return profile.IsFailure ? profile.Error : profile.Value;
    }

    private static Result<string> ValidateName(string? name)
    {
        var normalized = string.Join(' ', (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length is 0 or > MaxNameLength
            ? Error.Validation("ledger.account.name_invalid", $"An account name must have between 1 and {MaxNameLength} characters.")
            : normalized;
    }

    private static Result<string?> ValidateInstitution(string? institution)
    {
        var normalized = string.IsNullOrWhiteSpace(institution) ? null : institution.Trim();
        return normalized is { Length: > MaxInstitutionLength }
            ? Error.Validation("ledger.account.institution_invalid", $"The institution can have at most {MaxInstitutionLength} characters.")
            : normalized;
    }
}
