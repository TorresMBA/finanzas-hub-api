using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalFinance.Modules.Sample.Domain;

namespace PersonalFinance.Infrastructure.Persistence.Configurations.Sample;

internal sealed class NoteReceiptConfiguration : IEntityTypeConfiguration<NoteReceipt>
{
    public void Configure(EntityTypeBuilder<NoteReceipt> builder)
    {
        builder.ToTable("note_receipts", Schemas.Sample);
        builder.HasKey(receipt => receipt.NoteId);
        builder.Property(receipt => receipt.NoteId).ValueGeneratedNever();
    }
}
