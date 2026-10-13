using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalFinance.Modules.Categories.Domain;

namespace PersonalFinance.Infrastructure.Persistence.Configurations.Categories;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories", Schemas.Categories, table =>
        {
            table.HasCheckConstraint("ck_categories_kind", "kind IN ('Income', 'Expense')");
            table.HasCheckConstraint("ck_categories_color", "color IN ('Green', 'Blue', 'Violet', 'Amber', 'Red')");

            // Only a root category has a look of its own; a subcategory takes its parent's.
            table.HasCheckConstraint("ck_categories_look", "(parent_id IS NULL) = (icon IS NOT NULL) AND (parent_id IS NULL) = (color IS NOT NULL)");
        });
        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).ValueGeneratedNever();
        builder.Property(category => category.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(category => category.Name).HasMaxLength(Category.MaxNameLength).IsRequired();
        builder.Property(category => category.NameKey).HasMaxLength(Category.MaxNameLength).IsRequired();
        builder.Property(category => category.Icon).HasMaxLength(Category.MaxIconLength);
        builder.Property(category => category.Color).HasConversion<string>().HasMaxLength(16);
        builder.Property(category => category.SystemKey).HasMaxLength(80);
        builder.Ignore(category => category.IsRoot);
        builder.Ignore(category => category.IsArchived);

        builder.HasOne<Category>().WithMany().HasForeignKey(category => category.ParentId).OnDelete(DeleteBehavior.Restrict);

        // The handlers check both rules first to answer with a clear error; these stop two requests racing each other.
        builder.HasIndex(category => new { category.WorkspaceId, category.OwnerUserId, category.ParentId, category.NameKey })
            .IsUnique().AreNullsDistinct(false).HasFilter("archived_at IS NULL");
        builder.HasIndex(category => new { category.WorkspaceId, category.OwnerUserId, category.SystemKey })
            .IsUnique().HasFilter("system_key IS NOT NULL");

        builder.OwnsMany(category => category.Tags, tags =>
        {
            tags.ToTable("category_tags", Schemas.Categories);
            tags.WithOwner().HasForeignKey("CategoryId");
            tags.HasKey("CategoryId", nameof(CategoryTag.TagId));
            tags.Property(tag => tag.TagId).ValueGeneratedNever();
            tags.HasOne<Tag>().WithMany().HasForeignKey(tag => tag.TagId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Navigation(category => category.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags", Schemas.Categories);
        builder.HasKey(tag => tag.Id);
        builder.Property(tag => tag.Id).ValueGeneratedNever();
        builder.Property(tag => tag.Name).HasMaxLength(Tag.MaxNameLength).IsRequired();
        builder.Ignore(tag => tag.IsArchived);
        builder.HasIndex(tag => new { tag.WorkspaceId, tag.OwnerUserId, tag.Name }).IsUnique();
    }
}
