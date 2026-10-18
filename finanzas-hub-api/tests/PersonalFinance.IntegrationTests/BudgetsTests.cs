using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Infrastructure.Persistence;
using PersonalFinance.IntegrationTests.Support;

namespace PersonalFinance.IntegrationTests;

/// <summary>Monthly limits by category: what counts as spent, who sees them and what a change does to the past.</summary>
[Collection(ApiCollection.Name)]
public class BudgetsTests(ApiFixture api)
{
    // The budgets start in the real current month, so the movements of these tests are dated on its first day.
    private static readonly DateOnly Month = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
    private static readonly string Day = Month.ToString("yyyy-MM-dd");

    private static string Url(Guid workspaceId) => $"/api/v1/workspaces/{workspaceId}/budgets";

    private static async Task<JsonElement> CreateAsync(TestAccount person, string category, decimal amount, bool isFixed = false, Guid? workspaceId = null)
    {
        var response = await person.Client.PostAsJsonAsync(Url(workspaceId ?? person.PersonalWorkspaceId), new
        {
            categoryId = await person.CategoryIdAsync(category, workspaceId), amount, alertThresholdPercent = 90, isFixed
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> ListAsync(TestAccount person, string query = "", Guid? workspaceId = null) =>
        await person.Client.GetFromJsonAsync<JsonElement>(Url(workspaceId ?? person.PersonalWorkspaceId) + query);

    [Fact]
    public async Task With_a_limit_of_450_and_498_spent_the_budget_is_exceeded_and_only_expenses_of_the_category_count()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var bcp = await brian.OpenAccountAsync("BCP", 5000m);
        var savings = await brian.OpenAccountAsync("Ahorros", 0m);
        var groceries = await CreateAsync(brian, "Supermercado", 450m);
        await CreateAsync(brian, "Vivienda", 1800m, isFixed: true);
        Assert.Equal(("onTrack", 0m), (groceries.GetProperty("status").GetString(), groceries.Money("spent")));

        await brian.SpendAsync(bcp.Id(), 300m, description: "Wong", occurredOn: Day);
        await brian.SpendAsync(bcp.Id(), 198m, category: "Supermercado/Despensa", description: "Mercado", occurredOn: Day, pending: true);
        var mistake = await brian.SpendAsync(bcp.Id(), 70m, description: "Duplicado", occurredOn: Day);
        await brian.Client.PostAsJsonAsync($"{Books.TransactionsUrl(brian.PersonalWorkspaceId)}/{mistake.Id()}/void", new { reason = "Duplicado" });

        // None of these is an expense of the category: a transfer, an income and an expense of another one.
        await brian.TransferAsync(bcp.Id(), savings.Id(), 1000m, occurredOn: Day);
        await brian.EarnAsync(bcp.Id(), 4000m, occurredOn: Day);
        await brian.SpendAsync(bcp.Id(), 1800m, category: "Vivienda/Alquiler", description: "Alquiler", occurredOn: Day);

        var month = await ListAsync(brian);
        Assert.Equal((2250m, 2298m, -48m), (month.Money("limit"), month.Money("spent"), month.Money("remaining")));
        var rows = month.GetProperty("budgets").EnumerateArray().ToDictionary(row => row.GetProperty("categoryName").GetString()!);
        Assert.Equal(("exceeded", 498m, -48m, 2), (
            rows["Supermercado"].GetProperty("status").GetString(), rows["Supermercado"].Money("spent"), rows["Supermercado"].Money("remaining"),
            rows["Supermercado"].GetProperty("movementCount").GetInt32()));
        Assert.Equal(("paid", 1800m), (rows["Vivienda"].GetProperty("status").GetString(), rows["Vivienda"].Money("spent")));

        var detail = await brian.Client.GetFromJsonAsync<JsonElement>($"{Url(brian.PersonalWorkspaceId)}/{groceries.Id()}");
        // Each movement says the subcategory it went on, none when it went on the category itself.
        Assert.Equal(
            [("Mercado", "Despensa"), ("Wong", null)],
            detail.GetProperty("movements").EnumerateArray()
                .Select(row => (row.GetProperty("description").GetString(), row.GetProperty("subcategoryName").GetString())).Order());
        var daily = detail.GetProperty("daily").EnumerateArray().ToList();
        Assert.Equal((DateTime.DaysInMonth(Month.Year, Month.Month), 498m), (daily.Count, daily[0].Money("amount")));

        var home = (await brian.DashboardAsync(Month.ToString("yyyy-MM"))).GetProperty("budget");
        Assert.Equal((2250m, 2298m), (home.Money("limit"), home.Money("spent")));

        // One budget per category, and only on an expense category of the first level.
        async Task<string?> RejectedAsync(string category) => await Accounts.CodeOfAsync(await brian.Client.PostAsJsonAsync(
            Url(brian.PersonalWorkspaceId), new { categoryId = await brian.CategoryIdAsync(category), amount = 100m, alertThresholdPercent = 90 }));
        Assert.Equal("budgets.already_exists", await RejectedAsync("Supermercado"));
        Assert.Equal("budgets.category_invalid", await RejectedAsync("Supermercado/Despensa"));
        Assert.Equal("budgets.category_invalid", await RejectedAsync("Sueldo"));
    }

    [Fact]
    public async Task A_budget_is_private_and_adjusting_it_keeps_the_limit_of_the_months_gone()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;
        var juanEmail = Accounts.NewEmail("juan");
        var juan = await api.RegisterAsync("Juan", invitationToken: await api.InviteAsync(brian, workspaceId, juanEmail), email: juanEmail);
        var budget = await CreateAsync(brian, "Supermercado", 450m);
        var budgetUrl = $"{Url(workspaceId)}/{budget.Id()}";

        // Another member of the same workspace, whatever the role, neither sees it nor touches it.
        Assert.Empty((await ListAsync(juan, workspaceId: workspaceId)).GetProperty("budgets").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await juan.Client.GetAsync(budgetUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await juan.Client.PatchAsJsonAsync(budgetUrl, new { amount = 1m, alertThresholdPercent = 90 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await juan.Client.DeleteAsync(budgetUrl)).StatusCode);
        var juans = await CreateAsync(juan, "Supermercado", 200m, workspaceId: workspaceId);
        Assert.Equal(450m, (await ListAsync(brian)).Money("limit"));

        // As if Brian had created his two months ago: the change of today opens a new budget and closes the old one.
        var id = budget.Id();
        var past = Month.AddMonths(-2);
        await api.ExecuteAsync(services => services.GetRequiredService<AppDbContext>().Budgets.IgnoreQueryFilters()
            .Where(row => row.Id == id).ExecuteUpdateAsync(set => set.SetProperty(row => row.EffectiveFrom, past)));
        var adjusted = await brian.Client.PatchAsJsonAsync(budgetUrl, new { amount = 600m, alertThresholdPercent = 75, isFixed = false });
        Assert.Equal(HttpStatusCode.OK, adjusted.StatusCode);
        var current = await adjusted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(id, current.Id());
        Assert.Equal(600m, (await ListAsync(brian)).Money("limit"));
        Assert.Equal(450m, (await ListAsync(brian, $"?month={Month.AddMonths(-1):yyyy-MM}")).Money("limit"));
        Assert.Equal("budgets.not_in_force", await Accounts.CodeOfAsync(await brian.Client.PatchAsJsonAsync(budgetUrl, new { amount = 700m, alertThresholdPercent = 75 })));

        // Ending it takes it out of this month and leaves the past and Juan's alone.
        Assert.Equal(HttpStatusCode.NoContent, (await brian.Client.DeleteAsync($"{Url(workspaceId)}/{current.Id()}")).StatusCode);
        Assert.Empty((await ListAsync(brian)).GetProperty("budgets").EnumerateArray());
        Assert.Equal(450m, (await ListAsync(brian, $"?month={Month.AddMonths(-1):yyyy-MM}")).Money("limit"));
        Assert.Equal(juans.Id(), Assert.Single((await ListAsync(juan, workspaceId: workspaceId)).GetProperty("budgets").EnumerateArray()).Id());
    }

    [Fact]
    public async Task The_suggested_limit_is_the_average_of_the_three_closed_months_with_the_subcategories_and_without_other_categories()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var bcp = (await brian.OpenAccountAsync("BCP", 5000m)).Id();
        string Back(int months) => Month.AddMonths(-months).AddDays(9).ToString("yyyy-MM-dd");

        // 300 + 0 + 451 in the three months before this one; what is spent this month or four months ago does not count.
        await brian.SpendAsync(bcp, 200m, occurredOn: Back(3));
        await brian.SpendAsync(bcp, 100m, category: "Supermercado/Despensa", occurredOn: Back(3));
        await brian.SpendAsync(bcp, 451m, occurredOn: Back(1));
        await brian.SpendAsync(bcp, 999m, occurredOn: Back(4));
        await brian.SpendAsync(bcp, 999m, occurredOn: Day);
        await brian.SpendAsync(bcp, 999m, category: "Mascotas", occurredOn: Back(2));

        var groceries = await brian.CategoryIdAsync("Supermercado");
        var suggestion = await brian.Client.GetFromJsonAsync<JsonElement>($"{Url(brian.PersonalWorkspaceId)}/suggestion?categoryId={groceries}");
        Assert.Equal(251m, suggestion.Money("suggestedLimit"));
        Assert.Equal([300m, 0m, 451m], suggestion.GetProperty("months").EnumerateArray().Select(month => month.Money("spent")));

        var juan = await api.RegisterWithActiveWorkspaceAsync("Juan");
        Assert.Equal(HttpStatusCode.NotFound, (await juan.Client.GetAsync($"{Url(juan.PersonalWorkspaceId)}/suggestion?categoryId={groceries}")).StatusCode);
    }
}
