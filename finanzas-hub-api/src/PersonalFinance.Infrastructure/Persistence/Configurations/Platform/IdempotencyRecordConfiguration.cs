using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalFinance.Infrastructure.Platform;

namespace PersonalFinance.Infrastructure.Persistence.Configurations.Platform;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_keys", Schemas.Platform);
        builder.HasKey(record => new { record.UserId, record.Key });
        builder.Property(record => record.Key).HasMaxLength(100);
        builder.Property(record => record.Operation).HasMaxLength(300).IsRequired();
        builder.Property(record => record.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(record => record.ResponseBody).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(record => record.ExpiresAt);
    }
}
