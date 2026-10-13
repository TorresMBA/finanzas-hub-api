using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalFinance.Infrastructure.Platform;

namespace PersonalFinance.Infrastructure.Persistence.Configurations.Platform;

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log", Schemas.Platform);
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.ActorType).HasMaxLength(16).IsRequired();
        builder.Property(entry => entry.Source).HasMaxLength(32).IsRequired();
        builder.Property(entry => entry.Module).HasMaxLength(50).IsRequired();
        builder.Property(entry => entry.Action).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.ResourceType).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.ScopeType).HasMaxLength(100);
        builder.Property(entry => entry.Before).HasColumnType("jsonb");
        builder.Property(entry => entry.After).HasColumnType("jsonb");
        builder.Property(entry => entry.CorrelationId).HasMaxLength(64);
        builder.Property(entry => entry.TraceId).HasMaxLength(32);
        builder.HasIndex(entry => new { entry.ResourceType, entry.ResourceId, entry.OccurredAt });
        builder.HasIndex(entry => new { entry.ScopeType, entry.ScopeId, entry.OccurredAt });
    }
}
