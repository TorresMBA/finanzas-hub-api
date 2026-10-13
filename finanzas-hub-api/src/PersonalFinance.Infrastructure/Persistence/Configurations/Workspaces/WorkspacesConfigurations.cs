using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalFinance.Modules.Workspaces.Domain;

namespace PersonalFinance.Infrastructure.Persistence.Configurations.Workspaces;

internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("workspaces", Schemas.Workspaces, table =>
        {
            table.HasCheckConstraint("ck_workspaces_status", "status IN ('PendingSetup', 'Active', 'Archived')");
            table.HasCheckConstraint("ck_workspaces_base_currency", "status <> 'Active' OR base_currency IS NOT NULL");
        });
        builder.HasKey(workspace => workspace.Id);
        builder.Property(workspace => workspace.Id).ValueGeneratedNever();
        builder.Property(workspace => workspace.Name).HasMaxLength(Workspace.MaxNameLength).IsRequired();
        builder.Property(workspace => workspace.BaseCurrency).HasColumnType("char(3)");
        builder.Property(workspace => workspace.TimeZone).HasMaxLength(64).IsRequired();
        builder.Property(workspace => workspace.Status).HasConversion<string>().HasMaxLength(16);

        // Two people editing the workspace itself (name, time zone, owner) at the same moment: the second one gets a
        // conflict, not a silent overwrite. Membership rows are separate and are not covered by it.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(workspace => workspace.Memberships).WithOne().HasForeignKey(membership => membership.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(workspace => workspace.Memberships).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships", Schemas.Workspaces, table =>
        {
            table.HasCheckConstraint("ck_memberships_role", "role IN ('Member', 'Admin', 'Owner')");
            table.HasCheckConstraint("ck_memberships_status", "status IN ('Active', 'Removed')");
        });
        builder.HasKey(membership => membership.Id);
        builder.Property(membership => membership.Id).ValueGeneratedNever();
        builder.Property(membership => membership.Role).HasConversion<string>().HasMaxLength(16);
        builder.Property(membership => membership.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(membership => new { membership.WorkspaceId, membership.UserId }).IsUnique();
        builder.HasIndex(membership => membership.UserId).HasFilter("status = 'Active'");
    }
}

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations", Schemas.Workspaces, table =>
        {
            table.HasCheckConstraint("ck_invitations_role", "role IN ('Member', 'Admin')");
            table.HasCheckConstraint("ck_invitations_status", "status IN ('Pending', 'Accepted', 'Revoked')");
        });
        builder.HasKey(invitation => invitation.Id);
        builder.Property(invitation => invitation.Id).ValueGeneratedNever();
        builder.Property(invitation => invitation.Email).HasMaxLength(254).IsRequired();
        builder.Property(invitation => invitation.Role).HasConversion<string>().HasMaxLength(16);
        builder.Property(invitation => invitation.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(invitation => invitation.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(invitation => invitation.TokenHash).IsUnique();
        builder.HasIndex(invitation => new { invitation.WorkspaceId, invitation.Status });
        builder.HasOne<Workspace>().WithMany().HasForeignKey(invitation => invitation.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
    }
}
