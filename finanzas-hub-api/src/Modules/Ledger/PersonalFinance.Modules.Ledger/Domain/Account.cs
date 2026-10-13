using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Ledger.Domain;

/// <summary>Where the money is. Credit cards and the accounts other modules open for themselves come in later phases.</summary>
public enum AccountType
{
    Bank,
    Cash,
    Wallet
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

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public static Result<Account> Open(
        Guid workspaceId, Guid ownerUserId, string? name, AccountType type, string? currency, string? institution, DateTimeOffset now)
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

        return new Account(NewId())
        {
            WorkspaceId = workspaceId,
            OwnerUserId = ownerUserId,
            Name = validName.Value,
            NameKey = validName.Value.ToLowerInvariant(),
            Type = type,
            Currency = validCurrency.Value.Code,
            Institution = validInstitution.Value,
            CreatedAt = now
        };
    }

    /// <summary>Type and currency are not here on purpose: changing them would change what its entries mean.</summary>
    public Result Update(string? name, string? institution)
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

        Name = validName.Value;
        NameKey = validName.Value.ToLowerInvariant();
        Institution = validInstitution.Value;
        return Result.Success();
    }

    /// <summary>An archived account keeps its entries and its balance; it only stops being offered.</summary>
    public void Archive(DateTimeOffset now) => ArchivedAt ??= now;

    public void Restore() => ArchivedAt = null;

    /// <summary>Fails when the account cannot take new entries.</summary>
    public Result EnsureOpen() => IsArchived ? Archived : Result.Success();

    private static Error Archived => Error.BusinessRule("ledger.account.archived", "The account is archived. Restore it first.");

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
