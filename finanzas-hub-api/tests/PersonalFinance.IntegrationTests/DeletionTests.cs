using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.IntegrationTests.Support;
using PersonalFinance.Modules.Categories.Application.Abstractions;

namespace PersonalFinance.IntegrationTests;

/// <summary>What was never used can be deleted; what was used is archived (ADR-024).</summary>
[Collection(ApiCollection.Name)]
public class DeletionTests(ApiFixture api)
{
    private static string Url(Guid workspaceId, string path) => $"/api/v1/workspaces/{workspaceId}/{path}";

    private static async Task<Guid> CreateBusinessAsync(TestAccount person, string name)
    {
        var created = await person.Client.PostAsJsonAsync("/api/v1/workspaces", new { name, kind = "business" });
        var business = (await created.Content.ReadFromJsonAsync<JsonElement>()).Id();
        Assert.Equal(HttpStatusCode.OK, (await person.Client.PostAsJsonAsync($"/api/v1/workspaces/{business}/onboarding/complete", new { baseCurrency = "PEN", timeZone = "America/Lima" })).StatusCode);
        return business;
    }

    [Fact]
    public async Task An_empty_business_is_deleted_with_its_suggested_categories_and_one_with_an_account_is_not()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var juan = await api.RegisterWithActiveWorkspaceAsync("Juan");
        var empty = await CreateBusinessAsync(brian, "Prueba");
        var bodega = await CreateBusinessAsync(brian, "Bodega");
        await brian.OpenAccountAsync("Caja", 0m, workspaceId: bodega);

        var deletion = await brian.Client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{empty}/deletion");
        Assert.True(deletion.GetProperty("canDelete").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await juan.Client.DeleteAsync($"/api/v1/workspaces/{empty}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await brian.Client.DeleteAsync($"/api/v1/workspaces/{empty}")).StatusCode);

        // Its suggested categories went with it, and it is no longer among Brian's workspaces.
        Assert.Equal(0, await api.ExecuteAsync(services =>
            services.GetRequiredService<ICategoriesDb>().Categories.IgnoreQueryFilters().CountAsync(category => category.WorkspaceId == empty)));
        var mine = (await brian.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/workspaces")).EnumerateArray().Select(workspace => workspace.Id());
        Assert.DoesNotContain(empty, mine);

        // An account, even an empty one, is something registered: the business is archived, not deleted. The personal one never goes.
        var refused = await brian.Client.DeleteAsync($"/api/v1/workspaces/{bodega}");
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "workspaces.workspace.not_empty"), (refused.StatusCode, await Accounts.CodeOfAsync(refused)));
        Assert.Equal("workspaces.workspace.not_empty", (await brian.Client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{bodega}/deletion")).GetProperty("reason").GetString());
        var personal = await brian.Client.DeleteAsync($"/api/v1/workspaces/{brian.PersonalWorkspaceId}");
        Assert.Equal("workspaces.workspace.personal", await Accounts.CodeOfAsync(personal));
    }

    [Fact]
    public async Task What_another_module_points_at_is_not_deleted_until_that_goes_first()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var juan = await api.RegisterWithActiveWorkspaceAsync("Juan");
        var hub = brian.PersonalWorkspaceId;
        var bcp = await brian.OpenAccountAsync("BCP", 0m);
        Assert.True((await brian.AccountAsync(bcp.Id())).GetProperty("canDelete").GetBoolean());

        // A schedule points at the account: no foreign key says so, the schedule does.
        var rule = await brian.Client.PostAsJsonAsync(Url(hub, "recurring-rules"), new
        {
            type = "expense", accountId = bcp.Id(), categoryId = await brian.CategoryIdAsync("Vivienda"), amount = 300m, description = "Alquiler",
            frequency = "monthly", startsOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)).ToString("yyyy-MM-dd"), postingMode = "automatic"
        });
        var ruleId = (await rule.Content.ReadFromJsonAsync<JsonElement>()).Id();
        var refused = await brian.Client.DeleteAsync(Url(hub, $"accounts/{bcp.Id()}"));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "ledger.account.in_use"), (refused.StatusCode, await Accounts.CodeOfAsync(refused)));

        // The schedule never registered anything, so it goes; then the account can go too.
        Assert.Equal(HttpStatusCode.NoContent, (await brian.Client.DeleteAsync(Url(hub, $"recurring-rules/{ruleId}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await brian.Client.DeleteAsync(Url(hub, $"accounts/{bcp.Id()}"))).StatusCode);

        // A budget keeps its category.
        var budget = await brian.Client.PostAsJsonAsync(Url(hub, "budgets"), new { categoryId = await brian.CategoryIdAsync("Supermercado"), amount = 450m, alertThresholdPercent = 90, isFixed = false });
        Assert.Equal(HttpStatusCode.Created, budget.StatusCode);
        var category = await brian.Client.DeleteAsync(Url(hub, $"categories/{await brian.CategoryIdAsync("Supermercado")}"));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "categories.in_use"), (category.StatusCode, await Accounts.CodeOfAsync(category)));

        // An empty group goes; to someone outside it, it does not exist.
        var group = await brian.Client.PostAsJsonAsync(Url(hub, "shared-groups"), new { name = "Viaje" });
        var groupId = (await group.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("group").Id();
        Assert.Equal(HttpStatusCode.NotFound, (await juan.Client.DeleteAsync(Url(hub, $"shared-groups/{groupId}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await brian.Client.DeleteAsync(Url(hub, $"shared-groups/{groupId}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await brian.Client.GetAsync(Url(hub, $"shared-groups/{groupId}"))).StatusCode);
    }
}
