namespace PersonalFinance.Modules.Categories.Domain;

public sealed record SubcategoryTemplate(string Key, string Name);

/// <param name="Key">Stable identifier, stored as the category's system key. Never reuse one for something else.</param>
public sealed record CategoryTemplate(
    string Key, CategoryKind Kind, string Name, string Icon, CategoryColor Color, IReadOnlyList<SubcategoryTemplate> Subcategories, IReadOnlyList<string> Tags);

/// <summary>
/// The categories a person starts with. They are copied to each person, who can then rename or archive them;
/// changing this list never changes what someone already has.
/// </summary>
public static class DefaultCatalog
{
    public static IReadOnlyList<CategoryTemplate> Categories { get; } =
    [
        Expense("housing", "Vivienda", "home", CategoryColor.Blue, ["hogar", "fijo"], ("rent", "Alquiler"), ("maintenance", "Mantenimiento")),
        Expense("groceries", "Supermercado", "shopping_cart", CategoryColor.Green, ["mercado"], ("pantry", "Despensa"), ("cleaning", "Limpieza")),
        Expense("dining", "Restaurantes y delivery", "restaurant", CategoryColor.Amber, ["salidas"], ("delivery", "Delivery"), ("coffee", "Cafés"), ("going_out", "Salidas")),
        Expense("transport", "Transporte", "directions_car", CategoryColor.Violet, ["movilidad"], ("taxi", "Taxi y apps"), ("fuel", "Combustible"), ("public", "Transporte público")),
        Expense("utilities", "Servicios del hogar", "bolt", CategoryColor.Amber, ["fijo", "servicios"], ("electricity", "Luz"), ("water", "Agua"), ("internet", "Internet"), ("phone", "Telefonía")),
        Expense("health", "Salud", "favorite", CategoryColor.Red, ["salud", "deducible"], ("pharmacy", "Farmacia"), ("insurance", "Seguro EPS")),
        Expense("leisure", "Suscripciones y ocio", "movie", CategoryColor.Violet, ["suscripción"], ("streaming", "Streaming"), ("cinema", "Cine")),
        Expense("education", "Educación", "school", CategoryColor.Blue, ["deducible"], ("courses", "Cursos")),
        Expense("pets", "Mascotas", "pets", CategoryColor.Green, ["mascota"]),
        Income("salary", "Sueldo", "payments", CategoryColor.Green, ["fijo"]),
        Income("freelance", "Freelance", "work", CategoryColor.Blue, ["recibo-honorarios"], ("design", "Diseño"), ("consulting", "Consultoría")),
        Income("refunds", "Reembolsos", "group", CategoryColor.Violet, ["compartido"])
    ];

    private static CategoryTemplate Expense(string key, string name, string icon, CategoryColor color, string[] tags, params (string Key, string Name)[] subcategories) =>
        Template(CategoryKind.Expense, $"expense.{key}", name, icon, color, tags, subcategories);

    private static CategoryTemplate Income(string key, string name, string icon, CategoryColor color, string[] tags, params (string Key, string Name)[] subcategories) =>
        Template(CategoryKind.Income, $"income.{key}", name, icon, color, tags, subcategories);

    private static CategoryTemplate Template(CategoryKind kind, string key, string name, string icon, CategoryColor color, string[] tags, (string Key, string Name)[] subcategories) =>
        new(key, kind, name, icon, color, subcategories.Select(subcategory => new SubcategoryTemplate($"{key}.{subcategory.Key}", subcategory.Name)).ToList(), tags);
}
