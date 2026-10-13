using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PersonalFinance.Api.Controllers;

namespace PersonalFinance.IntegrationTests.Support;

/// <summary>Accounts and transactions created through the API, for tests that need a ledger with something in it.</summary>
public static class Books
{
    public static string AccountsUrl(Guid workspaceId) => $"/api/v1/workspaces/{workspaceId}/accounts";

    public static string TransactionsUrl(Guid workspaceId) => $"/api/v1/workspaces/{workspaceId}/transactions";

    /// <summary>A POST with an Idempotency-Key; a new one unless the test wants to repeat a request.</summary>
    public static async Task<HttpResponseMessage> PostWithKeyAsync(this HttpClient client, string url, object body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add(ApiControllerBase.IdempotencyKeyHeader, key ?? Guid.NewGuid().ToString());
        return await client.SendAsync(request);
    }

    public static async Task<JsonElement> OpenAccountAsync(
        this TestAccount person, string name, decimal openingBalance = 0, string currency = "PEN", string type = "bank", Guid? workspaceId = null)
    {
        var response = await person.Client.PostWithKeyAsync(
            AccountsUrl(workspaceId ?? person.PersonalWorkspaceId), new { name, type, currency, openingBalance });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>The id of one of the person's categories, by name; a subcategory as "Vivienda/Alquiler".</summary>
    public static async Task<Guid> CategoryIdAsync(this TestAccount person, string name, Guid? workspaceId = null)
    {
        var categories = await person.Client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{workspaceId ?? person.PersonalWorkspaceId}/categories?includeArchived=true");
        var path = name.Split('/');
        var root = categories.EnumerateArray().Single(category => category.GetProperty("name").GetString() == path[0]);
        return path.Length == 1
            ? root.GetProperty("id").GetGuid()
            : root.GetProperty("subcategories").EnumerateArray().Single(subcategory => subcategory.GetProperty("name").GetString() == path[1]).GetProperty("id").GetGuid();
    }

    public static async Task<HttpResponseMessage> PostTransactionAsync(
        this TestAccount person, Guid accountId, string type, decimal amount, string category, string description = "Compra", string occurredOn = "2026-10-12",
        string[]? tags = null, bool pending = false, string? key = null, string? merchant = null, string? note = null, Guid? workspaceId = null)
    {
        var categoryId = await person.CategoryIdAsync(category, workspaceId);
        return await person.Client.PostWithKeyAsync(
            TransactionsUrl(workspaceId ?? person.PersonalWorkspaceId),
            new { type, accountId, amount, categoryId, occurredOn, description, tags, pending, merchant, note },
            key);
    }

    public static async Task<JsonElement> SpendAsync(
        this TestAccount person, Guid accountId, decimal amount, string category = "Supermercado", string description = "Compra", string occurredOn = "2026-10-12",
        string[]? tags = null, bool pending = false, string? merchant = null, string? note = null)
    {
        var response = await person.PostTransactionAsync(accountId, "expense", amount, category, description, occurredOn, tags, pending, merchant: merchant, note: note);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<JsonElement> EarnAsync(
        this TestAccount person, Guid accountId, decimal amount, string category = "Sueldo", string description = "Pago", string occurredOn = "2026-10-12")
    {
        var response = await person.PostTransactionAsync(accountId, "income", amount, category, description, occurredOn);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<decimal> BalanceAsync(this TestAccount person, Guid accountId, Guid? workspaceId = null) =>
        (await person.Client.GetFromJsonAsync<JsonElement>($"{AccountsUrl(workspaceId ?? person.PersonalWorkspaceId)}/{accountId}")).GetProperty("balance").GetDecimal();

    public static async Task<JsonElement> TransactionsAsync(this TestAccount person, string query = "", Guid? workspaceId = null) =>
        await person.Client.GetFromJsonAsync<JsonElement>(TransactionsUrl(workspaceId ?? person.PersonalWorkspaceId) + query);

    public static Guid Id(this JsonElement element) => element.GetProperty("id").GetGuid();

    public static List<string> Descriptions(this JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("description").GetString()!).ToList();
}
