using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalFinance.Infrastructure.Platform;

namespace PersonalFinance.Infrastructure.Persistence.Configurations.Platform;

internal sealed class OutgoingEmailConfiguration : IEntityTypeConfiguration<OutgoingEmail>
{
    public void Configure(EntityTypeBuilder<OutgoingEmail> builder)
    {
        builder.ToTable("outgoing_emails", Schemas.Platform);
        builder.HasKey(email => email.Id);
        builder.Property(email => email.Id).ValueGeneratedNever();
        builder.Property(email => email.To).HasMaxLength(254).IsRequired();
        builder.Property(email => email.Subject).HasMaxLength(200).IsRequired();
        builder.Property(email => email.LastError).HasMaxLength(1000);
        builder.HasIndex(email => email.NextAttemptAt).HasFilter("sent_at IS NULL");
    }
}
