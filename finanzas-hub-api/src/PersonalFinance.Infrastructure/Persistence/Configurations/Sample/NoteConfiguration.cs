using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalFinance.Modules.Sample.Domain;

namespace PersonalFinance.Infrastructure.Persistence.Configurations.Sample;

internal sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("notes", Schemas.Sample);
        builder.HasKey(note => note.Id);
        builder.Property(note => note.Id).ValueGeneratedNever();
        builder.Property(note => note.Title).HasMaxLength(Note.MaxTitleLength).IsRequired();
        builder.HasIndex(note => new { note.WorkspaceId, note.OwnerUserId, note.CreatedAt });
    }
}
