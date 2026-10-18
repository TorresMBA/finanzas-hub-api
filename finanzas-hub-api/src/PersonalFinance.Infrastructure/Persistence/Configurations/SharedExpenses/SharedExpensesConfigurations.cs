using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalFinance.Modules.SharedExpenses.Domain;

namespace PersonalFinance.Infrastructure.Persistence.Configurations.SharedExpenses;

internal sealed class SharedGroupConfiguration : IEntityTypeConfiguration<SharedGroup>
{
    public void Configure(EntityTypeBuilder<SharedGroup> builder)
    {
        builder.ToTable("shared_groups", Schemas.SharedExpenses, table => table.HasCheckConstraint("ck_shared_groups_status", "status IN ('Active', 'Archived')"));
        builder.HasKey(group => group.Id);
        builder.Property(group => group.Id).ValueGeneratedNever();
        builder.Property(group => group.Name).HasMaxLength(SharedGroup.MaxNameLength).IsRequired();
        builder.Property(group => group.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(group => group.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(group => group.Icon).HasMaxLength(SharedGroup.MaxIconLength).HasDefaultValue(SharedGroup.DefaultIcon).IsRequired();
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(group => group.HasPendingDebts);
        builder.HasIndex(group => new { group.WorkspaceId, group.Status });

        builder.OwnsMany(group => group.Members, members =>
        {
            members.ToTable("group_members", Schemas.SharedExpenses, table =>
            {
                table.HasCheckConstraint("ck_group_members_role", "role IN ('Admin', 'Member')");
                table.HasCheckConstraint("ck_group_members_status", "status IN ('Active', 'Left')");
            });
            members.WithOwner().HasForeignKey("GroupId");
            members.HasKey("GroupId", nameof(GroupMember.UserId));

            // The person is given, never generated: without saying so EF takes a row added to an existing owner for one already saved.
            members.Property(member => member.UserId).ValueGeneratedNever();
            members.Property(member => member.Role).HasConversion<string>().HasMaxLength(16);
            members.Property(member => member.Status).HasConversion<string>().HasMaxLength(16);
            members.Ignore(member => member.IsActive);

            // What the membership filter looks up: the groups of a person.
            members.HasIndex(nameof(GroupMember.UserId), nameof(GroupMember.Status));
        });
        builder.Navigation(group => group.Members).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(group => group.Expenses, expenses =>
        {
            expenses.ToTable("shared_expenses", Schemas.SharedExpenses, table =>
            {
                table.HasCheckConstraint("ck_shared_expenses_total", "total_amount > 0");
                table.HasCheckConstraint("ck_shared_expenses_split_type", "split_type IN ('Equal', 'Percentage', 'FixedAmount', 'Shares')");
            });
            expenses.WithOwner().HasForeignKey("GroupId");
            expenses.HasKey(expense => expense.Id);
            expenses.Property(expense => expense.Id).ValueGeneratedNever();
            expenses.Property(expense => expense.Description).HasMaxLength(SharedGroup.MaxDescriptionLength).IsRequired();
            expenses.Property(expense => expense.TotalAmount).HasPrecision(18, 2);
            expenses.Property(expense => expense.SplitType).HasConversion<string>().HasMaxLength(16);
            expenses.Ignore(expense => expense.Status);
            expenses.Ignore(expense => expense.IsCancelled);
            expenses.HasIndex("GroupId", nameof(SharedExpense.OccurredOn));

            expenses.OwnsMany(expense => expense.Shares, shares =>
            {
                shares.ToTable("expense_shares", Schemas.SharedExpenses, table =>
                    table.HasCheckConstraint("ck_expense_shares_amounts", "amount >= 0 AND paid_amount >= 0 AND paid_amount <= amount"));
                shares.WithOwner().HasForeignKey("ExpenseId");
                shares.HasKey("ExpenseId", nameof(ExpenseShare.UserId));
                shares.Property(share => share.UserId).ValueGeneratedNever();
                shares.Property(share => share.Amount).HasPrecision(18, 2);
                shares.Property(share => share.PaidAmount).HasPrecision(18, 2);
                shares.Ignore(share => share.Pending);
            });
            expenses.Navigation(expense => expense.Shares).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        builder.Navigation(group => group.Expenses).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(group => group.Settlements, settlements =>
        {
            settlements.ToTable("settlements", Schemas.SharedExpenses, table => table.HasCheckConstraint("ck_settlements_amount", "amount > 0"));
            settlements.WithOwner().HasForeignKey("GroupId");
            settlements.HasKey(settlement => settlement.Id);
            settlements.Property(settlement => settlement.Id).ValueGeneratedNever();
            settlements.Property(settlement => settlement.Amount).HasPrecision(18, 2);
            settlements.Ignore(settlement => settlement.IsVoided);
            settlements.HasIndex("GroupId", nameof(Settlement.OccurredOn));

            settlements.OwnsMany(settlement => settlement.Allocations, allocations =>
            {
                allocations.ToTable("settlement_allocations", Schemas.SharedExpenses);
                allocations.WithOwner().HasForeignKey("SettlementId");
                allocations.HasKey("SettlementId", nameof(SettlementAllocation.ExpenseId));
                allocations.Property(allocation => allocation.ExpenseId).ValueGeneratedNever();
                allocations.Property(allocation => allocation.Amount).HasPrecision(18, 2);
            });
            settlements.Navigation(settlement => settlement.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        builder.Navigation(group => group.Settlements).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
