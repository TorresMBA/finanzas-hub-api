using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Infrastructure.Persistence;
using PersonalFinance.IntegrationTests.Support;
using PersonalFinance.Modules.Categories.Domain;

namespace PersonalFinance.IntegrationTests;

/// <summary>Categories and tags: the first private data. Each person has their own, even inside a shared workspace.</summary>
[Collection(ApiCollection.Name)]
public class CategoriesTests(ApiFixture api)
{
    [Fact]
    public async Task After_onboarding_Brian_has_the_default_categories_with_their_subcategories_and_tags()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");

        var categories = await ListAsync(brian);
        var tags = await TagsAsync(brian);

        Assert.Equal(DefaultCatalog.Categories.Count, categories.Count);
        Assert.Equal(["expense", "income"], categories.Select(Kind).Distinct());
        var housing = Named(categories, "Vivienda");
        Assert.Equal(("home", "blue", false), (housing.GetProperty("icon").GetString(), housing.GetProperty("color").GetString(), housing.GetProperty("isArchived").GetBoolean()));
        Assert.Equal(["Alquiler", "Mantenimiento"], Subcategories(housing));
        Assert.Equal(["fijo", "hogar"], TagsOf(housing));
        Assert.Equal(3, tags.Single(tag => tag.GetProperty("name").GetString() == "fijo").GetProperty("categoryCount").GetInt32());

        var seeded = await api.ExecuteAsync(services => services.GetRequiredService<AppDbContext>().AuditLog.AsNoTracking()
            .SingleAsync(entry => entry.Action == "categories.defaults.seeded" && entry.OwnerUserId == brian.UserId));
        Assert.Equal(brian.PersonalWorkspaceId, seeded.WorkspaceId);
    }

    [Fact]
    public async Task Seeding_is_safe_to_repeat_and_never_brings_back_what_the_person_changed()
    {
        var brian = await api.RegisterAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;
        Assert.Empty(await ListAsync(brian));

        // Before the onboarding Brian already made a category with the name of a default one.
        await CreateAsync(brian, new { kind = "expense", name = "mascotas" });
        var onboarding = await brian.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/onboarding/complete", new { baseCurrency = "PEN", timeZone = "America/Lima" });
        Assert.Equal(HttpStatusCode.OK, onboarding.StatusCode);
        var afterOnboarding = await ListAsync(brian);
        Assert.Equal(DefaultCatalog.Categories.Count, afterOnboarding.Count);
        Assert.Empty(TagsOf(Named(afterOnboarding, "mascotas")));

        var health = Named(afterOnboarding, "Salud");
        await brian.Client.PatchAsJsonAsync($"{Url(workspaceId)}/{Id(health)}", new { name = "Bienestar" });
        await brian.Client.PostAsync($"{Url(workspaceId)}/{Id(Named(afterOnboarding, "Sueldo"))}/archive", null);

        var again = await brian.Client.PostAsync($"{Url(workspaceId)}/defaults", null);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(0, (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetInt32());
        var names = (await ListAsync(brian, "?includeArchived=true")).Select(Name).ToList();
        Assert.Equal(DefaultCatalog.Categories.Count, names.Count);
        Assert.DoesNotContain("Salud", names);
        Assert.DoesNotContain("Mascotas", names);
    }

    [Fact]
    public async Task Brian_creates_a_category_with_tags_and_a_subcategory_and_archiving_it_stops_offering_both()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;

        var created = await CreateAsync(brian, new { kind = "expense", name = "Hijos", icon = "child_care", color = "violet", tags = new[] { "Familia", "#Colegio Nuevo", "familia" } });
        var withSubcategory = await CreateAsync(brian, new { name = "Útiles", parentId = Id(created) });

        Assert.Equal(("expense", "child_care", "violet"), (Kind(created), created.GetProperty("icon").GetString(), created.GetProperty("color").GetString()));
        Assert.Equal(["colegio-nuevo", "familia"], TagsOf(created));
        Assert.Equal(Id(created), Id(withSubcategory));
        Assert.Equal(["Útiles"], Subcategories(withSubcategory));

        var archive = await brian.Client.PostAsync($"{Url(workspaceId)}/{Id(created)}/archive", null);

        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        Assert.DoesNotContain(await ListAsync(brian, "?kind=expense"), category => Name(category) == "Hijos");
        var archived = Named(await ListAsync(brian, "?includeArchived=true"), "Hijos");
        Assert.True(archived.GetProperty("isArchived").GetBoolean());
        Assert.True(archived.GetProperty("subcategories")[0].GetProperty("isArchived").GetBoolean());

        // Its tags are still Brian's, but no active category suggests them.
        Assert.Equal(0, (await TagsAsync(brian)).Single(tag => tag.GetProperty("name").GetString() == "familia").GetProperty("categoryCount").GetInt32());
        var rename = await brian.Client.PatchAsJsonAsync($"{Url(workspaceId)}/{Id(created)}", new { name = "Niños" });
        Assert.Equal("categories.archived", await Accounts.CodeOfAsync(rename));

        var actions = await api.ExecuteAsync(services => services.GetRequiredService<AppDbContext>().AuditLog.AsNoTracking()
            .Where(entry => entry.ResourceId == Id(created)).OrderBy(entry => entry.OccurredAt).Select(entry => entry.Action).ToListAsync());
        Assert.Equal(["categories.category.created", "categories.category.archived"], actions);
    }

    [Fact]
    public async Task A_name_is_unique_among_the_active_categories_of_the_same_level()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;
        var categories = await ListAsync(brian);
        var housing = Named(categories, "Vivienda");
        var transport = Named(categories, "Transporte");

        var sameRoot = await PostAsync(brian, new { kind = "income", name = "  vivienda " });
        var sameSubcategory = await PostAsync(brian, new { name = "ALQUILER", parentId = Id(housing) });
        var elsewhere = await PostAsync(brian, new { name = "Alquiler", parentId = Id(transport) });
        var renamed = await brian.Client.PatchAsJsonAsync($"{Url(workspaceId)}/{Id(transport)}", new { name = "Vivienda" });

        Assert.Equal((HttpStatusCode.Conflict, "categories.name_taken"), (sameRoot.StatusCode, await Accounts.CodeOfAsync(sameRoot)));
        Assert.Equal((HttpStatusCode.Conflict, "categories.name_taken"), (sameSubcategory.StatusCode, await Accounts.CodeOfAsync(sameSubcategory)));
        Assert.Equal(HttpStatusCode.Created, elsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);

        // The name of an archived category is free again; restoring the old one then conflicts.
        await brian.Client.PostAsync($"{Url(workspaceId)}/{Id(housing)}/archive", null);
        var reused = await PostAsync(brian, new { kind = "expense", name = "Vivienda" });
        var restore = await brian.Client.PostAsync($"{Url(workspaceId)}/{Id(housing)}/restore", null);

        Assert.Equal(HttpStatusCode.Created, reused.StatusCode);
        Assert.Equal((HttpStatusCode.Conflict, "categories.name_taken"), (restore.StatusCode, await Accounts.CodeOfAsync(restore)));
    }

    [Fact]
    public async Task Restoring_a_category_brings_back_only_the_subcategories_archived_with_it()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;
        var utilities = Named(await ListAsync(brian), "Servicios del hogar");
        var phone = utilities.GetProperty("subcategories").EnumerateArray().Single(subcategory => Name(subcategory) == "Telefonía");

        await brian.Client.PostAsync($"{Url(workspaceId)}/{Id(phone)}/archive", null);
        await brian.Client.PostAsync($"{Url(workspaceId)}/{Id(utilities)}/archive", null);
        var orphan = await brian.Client.PostAsync($"{Url(workspaceId)}/{Id(phone)}/restore", null);
        var restored = await brian.Client.PostAsync($"{Url(workspaceId)}/{Id(utilities)}/restore", null);

        Assert.Equal((HttpStatusCode.UnprocessableEntity, "categories.parent_archived"), (orphan.StatusCode, await Accounts.CodeOfAsync(orphan)));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equal(["Agua", "Internet", "Luz"], Subcategories(Named(await ListAsync(brian), "Servicios del hogar")));

        var phoneAgain = await brian.Client.PostAsync($"{Url(workspaceId)}/{Id(phone)}/restore", null);
        Assert.Equal(HttpStatusCode.OK, phoneAgain.StatusCode);
        Assert.Equal(["Agua", "Internet", "Luz", "Telefonía"], Subcategories(Named(await ListAsync(brian), "Servicios del hogar")));
    }

    [Fact]
    public async Task Tags_are_created_by_naming_them_and_archiving_one_removes_it_from_every_category()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;
        var categories = await ListAsync(brian);
        var pets = Named(categories, "Mascotas");

        var set = await brian.Client.PutAsJsonAsync($"{Url(workspaceId)}/{Id(pets)}/tags", new { tags = new[] { "veterinario", "Fijo" } });

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(["fijo", "veterinario"], TagsOf(await set.Content.ReadFromJsonAsync<JsonElement>()));
        var tags = await TagsAsync(brian);
        Assert.DoesNotContain(tags, tag => tag.GetProperty("name").GetString() == "mascota" && tag.GetProperty("categoryCount").GetInt32() > 0);
        var fijo = tags.Single(tag => tag.GetProperty("name").GetString() == "fijo");
        Assert.Equal(4, fijo.GetProperty("categoryCount").GetInt32());

        var archive = await brian.Client.PostAsync($"/api/v1/workspaces/{workspaceId}/tags/{Id(fijo)}/archive", null);

        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        Assert.DoesNotContain(await TagsAsync(brian), tag => tag.GetProperty("name").GetString() == "fijo");
        Assert.All(await ListAsync(brian), category => Assert.DoesNotContain("fijo", TagsOf(category)));

        // Naming it again brings the same tag back instead of making a second one.
        await brian.Client.PutAsJsonAsync($"{Url(workspaceId)}/{Id(pets)}/tags", new { tags = new[] { "fijo" } });
        Assert.Equal(Id(fijo), Id((await TagsAsync(brian)).Single(tag => tag.GetProperty("name").GetString() == "fijo")));
    }

    [Fact]
    public async Task Requests_that_break_a_rule_are_rejected_with_their_code()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;
        var housing = Named(await ListAsync(brian), "Vivienda");
        var rent = housing.GetProperty("subcategories").EnumerateArray().First();

        var noKind = await PostAsync(brian, new { name = "Regalos" });
        var noName = await PostAsync(brian, new { kind = "expense", name = "" });
        var badColor = await PostAsync(brian, new { kind = "expense", name = "Regalos", color = "pink" });
        var badTag = await PostAsync(brian, new { kind = "expense", name = "Regalos", tags = new[] { "a,b" } });
        var thirdLevel = await PostAsync(brian, new { name = "Depósito", parentId = Id(rent) });
        var taggedSubcategory = await PostAsync(brian, new { name = "Seguro", parentId = Id(housing), tags = new[] { "fijo" } });
        var tagsOnSubcategory = await brian.Client.PutAsJsonAsync($"{Url(workspaceId)}/{Id(rent)}/tags", new { tags = new[] { "fijo" } });
        var unknownParent = await PostAsync(brian, new { name = "Seguro", parentId = Guid.NewGuid() });

        Assert.Equal((HttpStatusCode.BadRequest, "categories.kind_required"), (noKind.StatusCode, await Accounts.CodeOfAsync(noKind)));
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badColor.StatusCode);
        Assert.Equal((HttpStatusCode.BadRequest, "categories.tag.name_invalid"), (badTag.StatusCode, await Accounts.CodeOfAsync(badTag)));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "categories.max_depth"), (thirdLevel.StatusCode, await Accounts.CodeOfAsync(thirdLevel)));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "categories.tags_on_subcategory"), (taggedSubcategory.StatusCode, await Accounts.CodeOfAsync(taggedSubcategory)));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "categories.tags_on_subcategory"), (tagsOnSubcategory.StatusCode, await Accounts.CodeOfAsync(tagsOnSubcategory)));
        Assert.Equal((HttpStatusCode.NotFound, "categories.parent_not_found"), (unknownParent.StatusCode, await Accounts.CodeOfAsync(unknownParent)));

        // Nothing a rejected request wrote may stay, not even the tag it would have created.
        Assert.DoesNotContain(await ListAsync(brian, "?includeArchived=true"), category => Name(category) is "Regalos" or "Seguro");
    }

    [Fact]
    public async Task Two_members_of_a_workspace_each_have_their_own_categories_and_cannot_reach_the_others()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;
        var juanEmail = Accounts.NewEmail("juan");
        var juan = await api.RegisterAsync("Juan", invitationToken: await api.InviteAsync(brian, workspaceId, juanEmail), email: juanEmail);

        // Juan got his own copy when he joined, while he was not even signed in yet.
        var brians = await ListAsync(brian);
        var juans = await ListAsync(juan, workspaceId: workspaceId);
        Assert.Equal(DefaultCatalog.Categories.Count, juans.Count);
        Assert.Empty(brians.Select(Id).Intersect(juans.Select(Id)));

        var secret = await CreateAsync(brian, new { kind = "expense", name = "Terapia", tags = new[] { "privado" } });
        var housing = Named(brians, "Vivienda");
        var url = $"{Url(workspaceId)}/{Id(housing)}";

        var rename = await juan.Client.PatchAsJsonAsync(url, new { name = "De Juan" });
        var archive = await juan.Client.PostAsync($"{url}/archive", null);
        var restore = await juan.Client.PostAsync($"{url}/restore", null);
        var setTags = await juan.Client.PutAsJsonAsync($"{url}/tags", new { tags = new[] { "espía" } });
        var subcategory = await juan.Client.PostAsJsonAsync(Url(workspaceId), new { name = "De Juan", parentId = Id(housing) });
        var briansTag = (await TagsAsync(brian)).Single(tag => tag.GetProperty("name").GetString() == "privado");
        var archiveTag = await juan.Client.PostAsync($"/api/v1/workspaces/{workspaceId}/tags/{Id(briansTag)}/archive", null);

        foreach (var response in new[] { rename, archive, restore, setTags })
        {
            Assert.Equal((HttpStatusCode.NotFound, "categories.not_found"), (response.StatusCode, await Accounts.CodeOfAsync(response)));
        }

        Assert.Equal((HttpStatusCode.NotFound, "categories.parent_not_found"), (subcategory.StatusCode, await Accounts.CodeOfAsync(subcategory)));
        Assert.Equal((HttpStatusCode.NotFound, "categories.tag.not_found"), (archiveTag.StatusCode, await Accounts.CodeOfAsync(archiveTag)));

        // Being the owner of the workspace gives Brian nothing either: Juan's categories answer 404 to him.
        var ownerPeeks = await brian.Client.PatchAsJsonAsync($"{Url(workspaceId)}/{Id(Named(juans, "Vivienda"))}", new { name = "De Brian" });
        Assert.Equal(HttpStatusCode.NotFound, ownerPeeks.StatusCode);

        var juansAfter = await ListAsync(juan, "?includeArchived=true", workspaceId);
        Assert.DoesNotContain(juansAfter, category => Id(category) == Id(secret));
        Assert.DoesNotContain(await TagsAsync(juan, workspaceId), tag => tag.GetProperty("name").GetString() is "privado" or "espía");
        var unchanged = Named(await ListAsync(brian), "Vivienda");
        Assert.Equal((Id(housing), false), (Id(unchanged), unchanged.GetProperty("isArchived").GetBoolean()));
        Assert.Equal(["fijo", "hogar"], TagsOf(unchanged));
        Assert.Equal(["Alquiler", "Mantenimiento"], Subcategories(unchanged));
    }

    [Fact]
    public async Task The_categories_of_a_person_are_the_same_in_every_workspace_of_theirs()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var second = await brian.Client.PostAsJsonAsync("/api/v1/workspaces", new { name = "Negocio" });
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var housing = Named(await ListAsync(brian), "Vivienda");

        var inOther = await ListAsync(brian, workspaceId: secondId);
        var fromOther = await brian.Client.PatchAsJsonAsync($"{Url(secondId)}/{Id(housing)}", new { name = "Local" });

        // Categories are private, so they follow the person: the second workspace shows and changes the very same ones.
        Assert.Equal(Id(housing), Id(Named(inOther, "Vivienda")));
        Assert.Equal(HttpStatusCode.OK, fromOther.StatusCode);
        Assert.Contains(await ListAsync(brian), category => category.GetProperty("name").GetString() == "Local");
    }

    [Fact]
    public async Task Someone_outside_the_workspace_or_not_signed_in_gets_nothing()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var pedro = await api.RegisterAsync("Pedro");
        var url = Url(brian.PersonalWorkspaceId);

        var outsider = await pedro.Client.GetAsync(url);
        var outsiderTags = await pedro.Client.GetAsync($"/api/v1/workspaces/{brian.PersonalWorkspaceId}/tags");
        var outsiderSeeds = await pedro.Client.PostAsync($"{url}/defaults", null);
        var anonymous = await api.AnonymousClient().GetAsync(url);

        Assert.Equal((HttpStatusCode.NotFound, "workspace.not_found"), (outsider.StatusCode, await Accounts.CodeOfAsync(outsider)));
        Assert.Equal(HttpStatusCode.NotFound, outsiderTags.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, outsiderSeeds.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private static string Url(Guid workspaceId) => $"/api/v1/workspaces/{workspaceId}/categories";

    private static async Task<List<JsonElement>> ListAsync(TestAccount account, string query = "", Guid? workspaceId = null) =>
        (await account.Client.GetFromJsonAsync<JsonElement>(Url(workspaceId ?? account.PersonalWorkspaceId) + query)).EnumerateArray().ToList();

    private static async Task<List<JsonElement>> TagsAsync(TestAccount account, Guid? workspaceId = null) =>
        (await account.Client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{workspaceId ?? account.PersonalWorkspaceId}/tags")).EnumerateArray().ToList();

    private static Task<HttpResponseMessage> PostAsync(TestAccount account, object body) =>
        account.Client.PostAsJsonAsync(Url(account.PersonalWorkspaceId), body);

    private static async Task<JsonElement> CreateAsync(TestAccount account, object body)
    {
        var response = await PostAsync(account, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static JsonElement Named(IEnumerable<JsonElement> categories, string name) => categories.Single(category => Name(category) == name);

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static string Name(JsonElement element) => element.GetProperty("name").GetString()!;

    private static string Kind(JsonElement category) => category.GetProperty("kind").GetString()!;

    private static List<string> TagsOf(JsonElement category) => category.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()!).ToList();

    private static List<string> Subcategories(JsonElement category) => category.GetProperty("subcategories").EnumerateArray().Select(Name).ToList();
}
