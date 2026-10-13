using PersonalFinance.Modules.Categories.Domain;

namespace PersonalFinance.UnitTests.Categories;

public class CategoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Workspace = Guid.NewGuid();
    private static readonly Guid Brian = Guid.NewGuid();

    private static Category Root(string name = "Vivienda", CategoryKind kind = CategoryKind.Expense) =>
        Category.CreateRoot(Workspace, Brian, kind, name, "home", CategoryColor.Blue, Now).Value;

    [Fact]
    public void A_category_belongs_to_its_owner_and_compares_its_name_without_case()
    {
        var category = Category.CreateRoot(Workspace, Brian, CategoryKind.Expense, "  Servicios   del Hogar ", "Bolt", CategoryColor.Amber, Now).Value;

        Assert.Equal((Workspace, Brian), (category.WorkspaceId, category.OwnerUserId));
        Assert.Equal("Servicios del Hogar", category.Name);
        Assert.Equal("servicios del hogar", category.NameKey);
        Assert.Equal("bolt", category.Icon);
        Assert.True(category.IsRoot);
        Assert.False(category.IsArchived);
    }

    [Fact]
    public void A_category_without_a_look_gets_the_default_one()
    {
        var category = Category.CreateRoot(Workspace, Brian, CategoryKind.Income, "Sueldo", null, null, Now).Value;

        Assert.Equal(Category.DefaultIcon, category.Icon);
        Assert.Equal(CategoryColor.Green, category.Color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("Este nombre de categoría es demasiado largo para caber en la pantalla")]
    public void A_category_needs_a_name_that_fits(string? name)
    {
        Assert.Equal("categories.name_invalid", Category.CreateRoot(Workspace, Brian, CategoryKind.Expense, name, null, null, Now).Error.Code);
    }

    [Theory]
    [InlineData("<script>")]
    [InlineData("shopping cart")]
    [InlineData("")]
    public void An_icon_is_only_the_name_of_a_glyph(string icon)
    {
        Assert.Equal("categories.icon_invalid", Category.CreateRoot(Workspace, Brian, CategoryKind.Expense, "Compras", icon, null, Now).Error.Code);
    }

    [Fact]
    public void A_subcategory_takes_kind_and_owner_from_its_parent_and_has_no_look_of_its_own()
    {
        var parent = Root(kind: CategoryKind.Income);

        var subcategory = parent.AddSubcategory("Bonos", Now).Value;

        Assert.Equal(parent.Id, subcategory.ParentId);
        Assert.Equal(CategoryKind.Income, subcategory.Kind);
        Assert.Equal((Workspace, Brian), (subcategory.WorkspaceId, subcategory.OwnerUserId));
        Assert.Null(subcategory.Icon);
        Assert.Null(subcategory.Color);
    }

    [Fact]
    public void There_are_only_two_levels()
    {
        var subcategory = Root().AddSubcategory("Alquiler", Now).Value;

        Assert.Equal("categories.max_depth", subcategory.AddSubcategory("Depósito", Now).Error.Code);
    }

    [Fact]
    public void Updating_a_category_changes_name_and_look_and_keeps_what_was_not_sent()
    {
        var category = Root();

        Assert.True(category.Update("Casa", null, CategoryColor.Red).IsSuccess);

        Assert.Equal(("Casa", "casa", "home", CategoryColor.Red), (category.Name, category.NameKey, category.Icon, category.Color));
    }

    [Fact]
    public void Updating_a_subcategory_only_renames_it()
    {
        var subcategory = Root().AddSubcategory("Alquiler", Now).Value;

        Assert.True(subcategory.Update("Renta", "home", CategoryColor.Red).IsSuccess);

        Assert.Equal("Renta", subcategory.Name);
        Assert.Null(subcategory.Icon);
        Assert.Null(subcategory.Color);
    }

    [Fact]
    public void An_archived_category_cannot_be_changed_until_it_is_restored()
    {
        var category = Root();
        category.Archive(Now);

        Assert.Equal("categories.archived", category.Update("Casa", null, null).Error.Code);
        Assert.Equal("categories.archived", category.AddSubcategory("Alquiler", Now).Error.Code);
        Assert.Equal("categories.archived", category.SetTags([Guid.NewGuid()]).Error.Code);

        category.Restore();
        Assert.True(category.Update("Casa", null, null).IsSuccess);
    }

    [Fact]
    public void Archiving_twice_keeps_the_first_instant()
    {
        var category = Root();

        category.Archive(Now);
        category.Archive(Now.AddDays(1));

        Assert.Equal(Now, category.ArchivedAt);
    }

    [Fact]
    public void Setting_tags_replaces_them_without_repeating_any()
    {
        var category = Root();
        var (fijo, hogar, salud) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        category.SetTags([fijo, hogar]);

        Assert.True(category.SetTags([hogar, salud, salud]).IsSuccess);

        Assert.Equal([hogar, salud], category.Tags.Select(tag => tag.TagId));
    }

    [Fact]
    public void Only_a_category_suggests_tags_and_only_so_many()
    {
        var category = Root();
        var subcategory = category.AddSubcategory("Alquiler", Now).Value;

        Assert.Equal("categories.tags_on_subcategory", subcategory.SetTags([Guid.NewGuid()]).Error.Code);
        Assert.Equal("categories.too_many_tags", category.SetTags(Enumerable.Range(0, Category.MaxTags + 1).Select(_ => Guid.NewGuid()).ToList()).Error.Code);
    }

    [Theory]
    [InlineData("  #Recibo Honorarios ", "recibo-honorarios")]
    [InlineData("Suscripción", "suscripción")]
    [InlineData("fijo", "fijo")]
    [InlineData("año_2026", "año_2026")]
    public void A_tag_name_is_normalized(string typed, string expected)
    {
        Assert.Equal(expected, Tag.Create(Workspace, Brian, typed, Now).Value.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" # ")]
    [InlineData("a,b")]
    [InlineData("una-etiqueta-con-un-nombre-que-no-termina-nunca")]
    public void A_tag_needs_a_simple_name(string? typed)
    {
        Assert.Equal("categories.tag.name_invalid", Tag.Create(Workspace, Brian, typed, Now).Error.Code);
    }

    [Fact]
    public void The_default_catalog_can_be_seeded_as_it_is()
    {
        var templates = DefaultCatalog.Categories;
        var keys = templates.Select(template => template.Key).Concat(templates.SelectMany(template => template.Subcategories).Select(subcategory => subcategory.Key)).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Equal(templates.Count, templates.Select(template => template.Name.ToLowerInvariant()).Distinct().Count());
        Assert.Contains(templates, template => template.Kind == CategoryKind.Income);
        Assert.Contains(templates, template => template.Kind == CategoryKind.Expense);
        foreach (var template in templates)
        {
            var root = Category.CreateRoot(Workspace, Brian, template.Kind, template.Name, template.Icon, template.Color, Now, template.Key);
            Assert.True(root.IsSuccess, template.Key);
            Assert.All(template.Subcategories, subcategory => Assert.True(root.Value.AddSubcategory(subcategory.Name, Now, subcategory.Key).IsSuccess, subcategory.Key));
            Assert.Equal(template.Subcategories.Count, template.Subcategories.Select(subcategory => subcategory.Name.ToLowerInvariant()).Distinct().Count());
            Assert.All(template.Tags, tag => Assert.Equal(tag, Tag.Normalize(tag).Value));
        }
    }
}
