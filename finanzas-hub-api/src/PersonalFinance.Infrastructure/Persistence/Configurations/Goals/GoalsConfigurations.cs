using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalFinance.Modules.Goals.Domain;

namespace PersonalFinance.Infrastructure.Persistence.Configurations.Goals;

internal sealed class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> builder)
    {
        builder.ToTable("goals", Schemas.Goals, table =>
        {
            table.HasCheckConstraint("ck_goals_target_amount", "target_amount > 0");
            table.HasCheckConstraint("ck_goals_status", "status IN ('Active', 'Archived', 'Cancelled')");
        });
        builder.HasKey(goal => goal.Id);
        builder.Property(goal => goal.Id).ValueGeneratedNever();
        builder.Property(goal => goal.Name).HasMaxLength(Goal.MaxNameLength).IsRequired();
        builder.Property(goal => goal.TargetAmount).HasPrecision(18, 2);
        builder.Property(goal => goal.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(goal => goal.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(goal => goal.Saved);
        builder.Ignore(goal => goal.IsShared);
        builder.HasIndex(goal => new { goal.WorkspaceId, goal.Status });

        builder.OwnsMany(goal => goal.Participants, participants =>
        {
            participants.ToTable("goal_participants", Schemas.Goals, table =>
            {
                table.HasCheckConstraint("ck_goal_participants_role", "role IN ('Owner', 'Participant')");
                table.HasCheckConstraint("ck_goal_participants_status", "status IN ('Active', 'Left')");
            });
            participants.WithOwner().HasForeignKey("GoalId");
            participants.HasKey("GoalId", nameof(GoalParticipant.UserId));

            // The person is given, never generated: without saying so EF takes a row added to an existing owner for one already saved.
            participants.Property(participant => participant.UserId).ValueGeneratedNever();
            participants.Property(participant => participant.Role).HasConversion<string>().HasMaxLength(16);
            participants.Property(participant => participant.Status).HasConversion<string>().HasMaxLength(16);
            participants.Ignore(participant => participant.IsActive);

            // What the participation filter looks up: the goals of a person.
            participants.HasIndex(nameof(GoalParticipant.UserId), nameof(GoalParticipant.Status));
            participants.HasIndex("GoalId").IsUnique().HasFilter("role = 'Owner' AND status = 'Active'").HasDatabaseName("ix_goal_participants_one_owner");
        });
        builder.Navigation(goal => goal.Participants).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(goal => goal.Contributions, contributions =>
        {
            contributions.ToTable("goal_contributions", Schemas.Goals, table =>
            {
                table.HasCheckConstraint("ck_goal_contributions_amount", "amount > 0");
                table.HasCheckConstraint("ck_goal_contributions_type", "type IN ('Contribution', 'Withdrawal')");
            });
            contributions.WithOwner().HasForeignKey("GoalId");
            contributions.HasKey(contribution => contribution.Id);
            contributions.Property(contribution => contribution.Id).ValueGeneratedNever();
            contributions.Property(contribution => contribution.Type).HasConversion<string>().HasMaxLength(16);
            contributions.Property(contribution => contribution.Amount).HasPrecision(18, 2);
            contributions.Ignore(contribution => contribution.Signed);
            contributions.Ignore(contribution => contribution.IsVoided);

            // One contribution per movement of the ledger, so the two can always be reconciled.
            contributions.HasIndex(contribution => contribution.LedgerOperationId).IsUnique();
            contributions.HasIndex("GoalId", nameof(GoalContribution.OccurredOn));
        });
        builder.Navigation(goal => goal.Contributions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
