using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PersonalFinance.IntegrationTests.Support;

namespace PersonalFinance.IntegrationTests;

/// <summary>Money moving between two books of the same person: one leg in each, neither an income nor an expense (ADR-023).</summary>
[Collection(ApiCollection.Name)]
public class BookTransferTests(ApiFixture api)
{
    private static string Url(Guid workspaceId) => $"/api/v1/workspaces/{workspaceId}/book-transfers";

    private static async Task<Guid> CreateBusinessAsync(TestAccount person, string name)
    {
        var created = await person.Client.PostAsJsonAsync("/api/v1/workspaces", new { name, kind = "business" });
        var business = (await created.Content.ReadFromJsonAsync<JsonElement>()).Id();
        Assert.Equal(HttpStatusCode.OK, (await person.Client.PostAsJsonAsync($"/api/v1/workspaces/{business}/onboarding/complete", new { baseCurrency = "PEN", timeZone = "America/Lima" })).StatusCode);
        return business;
    }

    [Fact]
    public async Task Money_sent_to_the_business_leaves_one_book_and_enters_the_other_and_voiding_either_leg_undoes_both()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var juan = await api.RegisterWithActiveWorkspaceAsync("Juan");
        var personal = brian.PersonalWorkspaceId;
        var bodega = await CreateBusinessAsync(brian, "Bodega");
        var bcp = (await brian.OpenAccountAsync("BCP", 500m)).Id();
        var caja = (await brian.OpenAccountAsync("Caja", 0m, workspaceId: bodega)).Id();

        var sent = await brian.Client.PostWithKeyAsync(Url(personal), new { fromAccountId = bcp, toWorkspaceId = bodega, toAccountId = caja, amount = 100m, occurredOn = "2026-10-12" });
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        var leaving = await sent.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((bodega, "Caja"), (leaving.GetProperty("transfer").GetProperty("otherBookId").GetGuid(), leaving.GetProperty("transfer").GetProperty("toAccountName").GetString()));

        // Each book moved by 100, and in neither is it an income or an expense.
        Assert.Equal((400m, 100m), (await brian.BalanceAsync(bcp), await brian.BalanceAsync(caja, bodega)));
        foreach (var book in new[] { personal, bodega })
        {
            var listed = await brian.TransactionsAsync(workspaceId: book);
            Assert.Equal((0m, 0m), (listed.GetProperty("baseTotals").Money("income"), listed.GetProperty("baseTotals").Money("expense")));
        }

        // Voided from the business, it is voided in the personal book too.
        var arriving = (await brian.TransactionsAsync(workspaceId: bodega)).GetProperty("items").EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "transfer");
        Assert.Equal("BCP", arriving.GetProperty("transfer").GetProperty("fromAccountName").GetString());
        Assert.Equal(HttpStatusCode.OK, (await brian.Client.PostAsJsonAsync($"{Books.TransactionsUrl(bodega)}/{arriving.Id()}/void", new { reason = "Error" })).StatusCode);
        Assert.Equal((500m, 0m), (await brian.BalanceAsync(bcp), await brian.BalanceAsync(caja, bodega)));

        // Into a book of someone else, or into the same book, it is refused.
        var juanBcp = (await juan.OpenAccountAsync("BCP", 100m)).Id();
        var foreign = await juan.Client.PostWithKeyAsync(Url(juan.PersonalWorkspaceId), new { fromAccountId = juanBcp, toWorkspaceId = bodega, toAccountId = caja, amount = 10m, occurredOn = "2026-10-12" });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var ahorros = (await brian.OpenAccountAsync("Ahorros", 0m)).Id();
        var same = await brian.Client.PostWithKeyAsync(Url(personal), new { fromAccountId = bcp, toWorkspaceId = personal, toAccountId = ahorros, amount = 10m, occurredOn = "2026-10-12" });
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "ledger.transfer.same_book"), (same.StatusCode, await Accounts.CodeOfAsync(same)));
    }
}
