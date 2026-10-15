using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Infrastructure.Persistence.Configurations.Ledger;

internal static class LedgerColumns
{
    /// <summary>Wide enough for any supported currency and for currencies with more decimals later.</summary>
    public const string Amount = "numeric(19,4)";
}

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        // There is no balance column, and there must never be one: the balance is the sum of the entries.
        builder.ToTable("accounts", Schemas.Ledger, table => table.HasCheckConstraint("ck_accounts_type", "type IN ('Bank', 'Cash', 'Wallet', 'CreditCard', 'GoalFund', 'Investment')"));
        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).ValueGeneratedNever();
        builder.Property(account => account.Name).HasMaxLength(Account.MaxNameLength).IsRequired();
        builder.Property(account => account.NameKey).HasMaxLength(Account.MaxNameLength).IsRequired();
        builder.Property(account => account.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(account => account.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(account => account.Institution).HasMaxLength(Account.MaxInstitutionLength);
        builder.Ignore(account => account.IsArchived);
        builder.Ignore(account => account.IsCreditCard);

        // One row per credit card, none for any other account. What is owed is not here: it is the balance.
        builder.OwnsOne(account => account.CreditCard, card =>
        {
            card.ToTable("credit_card_profiles", Schemas.Ledger, table =>
            {
                table.HasCheckConstraint("ck_credit_card_profiles_limit", "credit_limit > 0");
                table.HasCheckConstraint("ck_credit_card_profiles_days", "payment_day BETWEEN 1 AND 31 AND (closing_day IS NULL OR closing_day BETWEEN 1 AND 31)");
            });
            card.WithOwner().HasForeignKey("AccountId");
            card.HasKey("AccountId");
            card.Property<Guid>("AccountId").HasColumnName("account_id");
            card.Property(profile => profile.CreditLimit).HasColumnType(LedgerColumns.Amount);
        });
        builder.Property<uint>("Version").IsRowVersion();

        // The handlers check the name first to answer with a clear error; this stops two requests racing each other.
        builder.HasIndex(account => new { account.WorkspaceId, account.OwnerUserId, account.NameKey }).IsUnique().HasFilter("archived_at IS NULL");
    }
}

internal sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("exchange_rates", Schemas.Ledger, table => table.HasCheckConstraint("ck_exchange_rates_rate", "rate > 0 AND currency <> base_currency"));
        builder.HasKey(rate => rate.Id);
        builder.Property(rate => rate.Id).ValueGeneratedNever();
        builder.Property(rate => rate.BaseCurrency).HasColumnType("char(3)").IsRequired();
        builder.Property(rate => rate.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(rate => rate.Rate).HasColumnType("numeric(18,8)");

        // One rate per currency and day for each person; giving it again for the same day replaces it.
        builder.HasIndex(rate => new { rate.WorkspaceId, rate.OwnerUserId, rate.BaseCurrency, rate.Currency, rate.AsOf })
            .IsUnique()
            .HasDatabaseName("ix_exchange_rates_owner_currency_as_of");
    }
}

internal sealed class OperationConfiguration : IEntityTypeConfiguration<Operation>
{
    public void Configure(EntityTypeBuilder<Operation> builder)
    {
        builder.ToTable("operations", Schemas.Ledger, table =>
        {
            table.HasCheckConstraint("ck_operations_kind", "kind IN ('Income', 'Expense', 'Refund', 'Transfer', 'Adjustment', 'OpeningBalance')");
            table.HasCheckConstraint("ck_operations_status", "status IN ('Pending', 'Confirmed', 'Voided')");
            table.HasCheckConstraint("ck_operations_category", "kind NOT IN ('Income', 'Expense', 'Refund') OR category_id IS NOT NULL");
        });
        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.Id).ValueGeneratedNever();
        builder.Property(operation => operation.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(operation => operation.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(operation => operation.Description).HasMaxLength(Operation.MaxDescriptionLength).IsRequired();
        builder.Property(operation => operation.Merchant).HasMaxLength(Operation.MaxMerchantLength);
        builder.Property(operation => operation.Note).HasMaxLength(Operation.MaxNoteLength);
        builder.Property(operation => operation.VoidReason).HasMaxLength(Operation.MaxVoidReasonLength);
        builder.Property(operation => operation.SourceModule).HasMaxLength(40);
        builder.Property(operation => operation.SourceType).HasMaxLength(40);
        builder.Ignore(operation => operation.IsEffective);
        builder.Ignore(operation => operation.IsTransaction);
        builder.Property<uint>("Version").IsRowVersion();

        // The order of the transaction list, newest first.
        builder.HasIndex(operation => new { operation.WorkspaceId, operation.OwnerUserId, operation.OccurredOn, operation.Id }).IsDescending(false, false, true, true);
        builder.HasIndex(operation => new { operation.OwnerUserId, operation.CategoryId, operation.OccurredOn });

        // What makes a module's posting idempotent: one operation per thing that originated it.
        builder.HasIndex(operation => new { operation.SourceModule, operation.SourceType, operation.SourceId }).IsUnique().HasFilter("source_id IS NOT NULL");

        builder.HasMany(operation => operation.Entries).WithOne().HasForeignKey(entry => entry.OperationId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(operation => operation.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(operation => operation.Tags, tags =>
        {
            tags.ToTable("operation_tags", Schemas.Ledger);
            tags.WithOwner().HasForeignKey("OperationId");
            tags.HasKey("OperationId", nameof(OperationTag.TagId));
            tags.Property(tag => tag.TagId).ValueGeneratedNever();
            tags.HasIndex(tag => tag.TagId);
        });
        builder.Navigation(operation => operation.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class EntryConfiguration : IEntityTypeConfiguration<Entry>
{
    public void Configure(EntityTypeBuilder<Entry> builder)
    {
        builder.ToTable("entries", Schemas.Ledger, table =>
        {
            table.HasCheckConstraint("ck_entries_amount", "amount > 0");
            table.HasCheckConstraint("ck_entries_direction", "direction IN ('In', 'Out')");
        });
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Direction).HasConversion<string>().HasMaxLength(4);
        builder.Property(entry => entry.Amount).HasColumnType(LedgerColumns.Amount);
        builder.Property(entry => entry.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(entry => entry.SignedAmount).HasColumnType(LedgerColumns.Amount)
            .HasComputedColumnSql("CASE WHEN direction = 'In' THEN amount ELSE -amount END", stored: true);
        builder.Property(entry => entry.BaseAmount).HasColumnType(LedgerColumns.Amount);
        builder.Property(entry => entry.FxRate).HasColumnType("numeric(18,8)");

        builder.HasOne<Account>().WithMany().HasForeignKey(entry => entry.AccountId).OnDelete(DeleteBehavior.Restrict);

        // The balance of an account is answered from this index alone.
        builder.HasIndex(entry => new { entry.AccountId, entry.OccurredOn }).IncludeProperties(entry => entry.SignedAmount).HasFilter("is_effective");
    }
}
