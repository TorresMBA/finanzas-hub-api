using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalFinance.Modules.Budgets.Domain;

namespace PersonalFinance.Infrastructure.Persistence.Configurations.Budgets;

internal sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.ToTable("budgets", Schemas.Budgets, table =>
        {
            table.HasCheckConstraint("ck_budgets_amount", "amount > 0");
            table.HasCheckConstraint("ck_budgets_alert_threshold", "alert_threshold_percent IN (75, 90, 100)");
            table.HasCheckConstraint("ck_budgets_validity", "ends_on IS NULL OR ends_on >= effective_from");
        });
        builder.HasKey(budget => budget.Id);
        builder.Property(budget => budget.Id).ValueGeneratedNever();
        builder.Property(budget => budget.Amount).HasPrecision(18, 2);
        builder.Property(budget => budget.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(budget => budget.IsInForce);

        // One budget per category and starting month; the handler checks first that none is in force.
        builder.HasIndex(budget => new { budget.WorkspaceId, budget.OwnerUserId, budget.CategoryId, budget.EffectiveFrom })
            .IsUnique().HasDatabaseName("ix_budgets_owner_category_effective_from");
    }
}
