using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalFinance.Modules.Integrations.Domain;

namespace PersonalFinance.Infrastructure.Persistence.Configurations.Integrations;

internal sealed class ImportSessionConfiguration : IEntityTypeConfiguration<ImportSession>
{
    public void Configure(EntityTypeBuilder<ImportSession> builder)
    {
        builder.ToTable("import_sessions", Schemas.Integrations, table =>
        {
            table.HasCheckConstraint("ck_import_sessions_format", "format IN ('Csv', 'Bluecoins', 'Ofx', 'Xlsx')");
            table.HasCheckConstraint("ck_import_sessions_status", "status IN ('Review', 'Committed')");
        });
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.Property(session => session.Format).HasConversion<string>().HasMaxLength(16);
        builder.Property(session => session.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(session => session.FileName).HasMaxLength(ImportSession.MaxFileNameLength).IsRequired();
        builder.Property(session => session.FileHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasIndex(session => new { session.WorkspaceId, session.OwnerUserId, session.CreatedAt });

        builder.OwnsMany(session => session.Accounts, accounts =>
        {
            accounts.ToTable("import_accounts", Schemas.Integrations);
            accounts.WithOwner().HasForeignKey("SessionId");
            accounts.HasKey(account => account.Id);

            // The id is given, never generated: without saying so EF takes a row added to an existing owner for one already saved.
            accounts.Property(account => account.Id).ValueGeneratedNever();
            accounts.Property(account => account.SourceName).HasMaxLength(200).IsRequired();
            accounts.Property(account => account.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
            accounts.Property(account => account.CreateAs).HasConversion<string>().HasMaxLength(16);
        });
        builder.Navigation(session => session.Accounts).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(session => session.Categories, categories =>
        {
            categories.ToTable("import_categories", Schemas.Integrations);
            categories.WithOwner().HasForeignKey("SessionId");
            categories.HasKey(category => category.Id);
            categories.Property(category => category.Id).ValueGeneratedNever();
            categories.Property(category => category.SourceGroup).HasMaxLength(200).IsRequired();
            categories.Property(category => category.SourceName).HasMaxLength(200).IsRequired();
        });
        builder.Navigation(session => session.Categories).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ImportRecordConfiguration : IEntityTypeConfiguration<ImportRecord>
{
    public void Configure(EntityTypeBuilder<ImportRecord> builder)
    {
        builder.ToTable("import_records", Schemas.Integrations, table =>
            table.HasCheckConstraint("ck_import_records_kind", "kind IN ('Income', 'Expense', 'Transfer', 'OpeningBalance')"));
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).ValueGeneratedNever();
        builder.Property(record => record.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(record => record.Amount).HasPrecision(18, 2);
        builder.Property(record => record.CounterAmount).HasPrecision(18, 2);
        builder.Property(record => record.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(record => record.Description).HasMaxLength(120).IsRequired();
        builder.Property(record => record.Note).HasMaxLength(500);
        builder.Property(record => record.SourceAccount).HasMaxLength(200).IsRequired();
        builder.Property(record => record.SourceCounterAccount).HasMaxLength(200);
        builder.Property(record => record.SourceCategoryGroup).HasMaxLength(200);
        builder.Property(record => record.SourceCategory).HasMaxLength(200).IsRequired();
        builder.Property(record => record.Labels).HasMaxLength(500).IsRequired();
        builder.Property(record => record.Error).HasMaxLength(200);
        builder.Ignore(record => record.SignedAmount);

        // The lines are staging of their import: discarding it takes them along.
        builder.HasOne<ImportSession>().WithMany().HasForeignKey(record => record.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(record => new { record.SessionId, record.Line });
    }
}

internal sealed class CategorizationRuleConfiguration : IEntityTypeConfiguration<CategorizationRule>
{
    public void Configure(EntityTypeBuilder<CategorizationRule> builder)
    {
        builder.ToTable("categorization_rules", Schemas.Integrations);
        builder.HasKey(rule => rule.Id);
        builder.Property(rule => rule.Id).ValueGeneratedNever();
        builder.Property(rule => rule.Pattern).HasMaxLength(CategorizationRule.MaxPatternLength).IsRequired();
        builder.Property(rule => rule.PatternKey).HasMaxLength(CategorizationRule.MaxPatternLength * 2).IsRequired();
        builder.Property<uint>("Version").IsRowVersion();

        // One rule per text; the handler checks first to answer with a clear error.
        builder.HasIndex(rule => new { rule.WorkspaceId, rule.OwnerUserId, rule.PatternKey }).IsUnique().HasDatabaseName("ix_categorization_rules_owner_pattern");
    }
}
