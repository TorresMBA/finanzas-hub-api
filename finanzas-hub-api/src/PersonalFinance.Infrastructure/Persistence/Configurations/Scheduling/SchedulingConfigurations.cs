using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalFinance.Modules.Scheduling.Domain;

namespace PersonalFinance.Infrastructure.Persistence.Configurations.Scheduling;

internal sealed class RecurringRuleConfiguration : IEntityTypeConfiguration<RecurringRule>
{
    public void Configure(EntityTypeBuilder<RecurringRule> builder)
    {
        builder.ToTable("recurring_rules", Schemas.Scheduling, table =>
        {
            table.HasCheckConstraint("ck_recurring_rules_amount", "amount > 0");
            table.HasCheckConstraint("ck_recurring_rules_type", "type IN ('Income', 'Expense')");
            table.HasCheckConstraint("ck_recurring_rules_frequency", "frequency IN ('Once', 'Monthly', 'Yearly')");
            table.HasCheckConstraint("ck_recurring_rules_posting_mode", "posting_mode IN ('Automatic', 'Confirm')");
            table.HasCheckConstraint("ck_recurring_rules_status", "status IN ('Active', 'Paused', 'Ended')");
            table.HasCheckConstraint("ck_recurring_rules_period", "ends_on IS NULL OR ends_on >= starts_on");
        });
        builder.HasKey(rule => rule.Id);
        builder.Property(rule => rule.Id).ValueGeneratedNever();
        builder.Property(rule => rule.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(rule => rule.Frequency).HasConversion<string>().HasMaxLength(16);
        builder.Property(rule => rule.PostingMode).HasConversion<string>().HasMaxLength(16);
        builder.Property(rule => rule.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(rule => rule.Amount).HasPrecision(18, 2);
        builder.Property(rule => rule.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(rule => rule.Description).HasMaxLength(RecurringRule.MaxDescriptionLength).IsRequired();
        builder.Property(rule => rule.PauseReason).HasMaxLength(200);
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasIndex(rule => new { rule.WorkspaceId, rule.OwnerUserId, rule.Status });

        // The key is the rule and the date: whatever happens, a date is settled once.
        builder.OwnsMany(rule => rule.Occurrences, occurrences =>
        {
            occurrences.ToTable("occurrences", Schemas.Scheduling);
            occurrences.WithOwner().HasForeignKey("RuleId");
            occurrences.HasKey("RuleId", nameof(Occurrence.ScheduledOn));
            occurrences.Property(occurrence => occurrence.Status).HasConversion<string>().HasMaxLength(16);
            occurrences.Property(occurrence => occurrence.Amount).HasPrecision(18, 2);
        });
        builder.Navigation(rule => rule.Occurrences).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
