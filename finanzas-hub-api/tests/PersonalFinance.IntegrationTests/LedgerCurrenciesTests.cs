using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using PersonalFinance.IntegrationTests.Support;

namespace PersonalFinance.IntegrationTests;

/// <summary>Movements in several currencies: what each was worth in the base one, a price in another currency, a rate stated by hand and the fee of a transfer.</summary>
[Collection(ApiCollection.Name)]
public class LedgerCurrenciesTests(ApiFixture api)
{
    [Fact]
    public async Task The_catalog_says_how_each_currency_is_written()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");

        var currencies = await brian.Client.GetFromJsonAsync<JsonElement>("/api/v1/currencies");

        var soles = currencies.EnumerateArray().Single(currency => currency.GetProperty("code").GetString() == "PEN");
        Assert.Equal(("S/", "Sol peruano", "soles", 2), (
            soles.GetProperty("symbol").GetString(), soles.GetProperty("name").GetString(), soles.GetProperty("pluralName").GetString(),
            soles.GetProperty("minorUnits").GetInt32()));
        Assert.Equal(3, currencies.GetArrayLength());
    }

    [Fact]
    public async Task Filtering_by_a_currency_finds_both_sides_of_a_transfer_and_its_fee_goes_and_comes_back_with_it()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var soles = await brian.OpenAccountAsync("BCP", 1000m);
        var dollars = await brian.OpenAccountAsync("Cuenta Spot", currency: "USD");
        await brian.SetRateAsync("USD", 3.70m);

        var earned = await brian.EarnAsync(dollars.Id(), 100m, description: "Proyecto");
        await brian.SpendAsync(soles.Id(), 50m, description: "Mercado");

        // What 100 dollars were worth that day, by the person's rate.
        Assert.Equal((370m, 3.7m, "2026-01-01", false), (
            earned.GetProperty("conversion").Money("baseAmount"), earned.GetProperty("conversion").Money("rate"),
            earned.GetProperty("conversion").GetProperty("rateDate").GetString(), earned.GetProperty("conversion").GetProperty("isManual").GetBoolean()));

        // Buying 100 dollars with 375 soles at a market of 3.70, and a fee of 2 soles charged apart.
        var response = await brian.Client.PostWithKeyAsync(Books.TransfersUrl(brian.PersonalWorkspaceId), new
        {
            fromAccountId = soles.Id(), toAccountId = dollars.Id(), amount = 375m, destinationAmount = 100m, occurredOn = "2026-10-12",
            description = "Compra de dólares", fee = 2m
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transfer = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((3.7m, 2m), (transfer.GetProperty("transfer").Money("marketRate"), transfer.GetProperty("transfer").Money("fee")));
        // The fee is the only money that left for good: an expense in "Comisiones". The exchange is not one.
        Assert.Equal((573m, 200m), (await brian.BalanceAsync(soles.Id()), await brian.BalanceAsync(dollars.Id())));
        var all = await brian.TransactionsAsync();
        var fee = all.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("description").GetString() == "Comisión de transferencia");
        Assert.Equal(("Comisiones", false), (fee.GetProperty("category").GetProperty("name").GetString(), fee.GetProperty("isEditable").GetBoolean()));
        Assert.Equal(52m, all.GetProperty("totals").EnumerateArray().Single(total => total.GetProperty("currency").GetString() == "PEN").Money("expense"));

        // In dollars: the income and the transfer that brought them, with the net in soles beside it.
        var inDollars = await brian.TransactionsAsync("?currency=usd");
        Assert.Equal(["Compra de dólares", "Proyecto"], inDollars.Descriptions());
        var totals = Assert.Single(inDollars.GetProperty("totals").EnumerateArray());
        Assert.Equal(("USD", 100m, 370m), (totals.GetProperty("currency").GetString(), totals.Money("net"), totals.Money("baseNet")));
        Assert.Equal(HttpStatusCode.BadRequest, (await brian.Client.GetAsync(Books.TransactionsUrl(brian.PersonalWorkspaceId) + "?currency=XYZ")).StatusCode);

        // A fee cannot be voided on its own; voiding the transfer gives it back.
        var alone = await brian.Client.PostAsJsonAsync($"{Books.TransactionsUrl(brian.PersonalWorkspaceId)}/{fee.Id()}/void", new { });
        Assert.Equal("ledger.transaction.owned_by_module", await Accounts.CodeOfAsync(alone));
        var voided = await brian.Client.PostAsJsonAsync($"{Books.TransactionsUrl(brian.PersonalWorkspaceId)}/{transfer.Id()}/void", new { reason = "Error" });
        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
        Assert.Equal((950m, 100m), (await brian.BalanceAsync(soles.Id()), await brian.BalanceAsync(dollars.Id())));
    }

    [Fact]
    public async Task A_price_in_another_currency_is_a_reference_and_a_rate_stated_by_hand_values_the_transaction()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var card = await brian.OpenCardAsync("Visa", 5000m);
        var dollars = await brian.OpenAccountAsync("Cuenta Spot", 500m, currency: "USD");
        await brian.SetRateAsync("USD", 3.70m);
        var streaming = await brian.CategoryIdAsync("Suscripciones y ocio");
        var url = Books.TransactionsUrl(brian.PersonalWorkspaceId);

        async Task<HttpResponseMessage> PostAsync(Guid accountId, decimal amount, object extra)
        {
            var body = JsonSerializer.SerializeToNode(new { type = "expense", accountId, amount, categoryId = streaming, occurredOn = "2026-10-12", description = "Netflix" })!
                .AsObject();
            foreach (var (name, value) in JsonSerializer.SerializeToNode(extra)!.AsObject().ToList())
            {
                body[name] = value?.DeepClone();
            }

            return await brian.Client.PostWithKeyAsync(url, body);
        }

        // US$ 15.49 charged by the card as S/ 58.71: the expense is what left, the price stays as a reference.
        var netflix = await (await PostAsync(card.Id(), 58.71m, new { originalAmount = 15.49m, originalCurrency = "USD" })).Content.ReadFromJsonAsync<JsonElement>();
        var price = netflix.GetProperty("originalPrice");
        Assert.Equal((15.49m, "USD", 3.7m), (price.Money("amount"), price.GetProperty("currency").GetString(), price.Money("marketRate")));
        Assert.Equal(-58.71m, await brian.BalanceAsync(card.Id()));
        Assert.Equal(JsonValueKind.Null, netflix.GetProperty("conversion").ValueKind);

        // The bank applied 3.80 instead of the 3.70 of the table.
        var manual = await (await PostAsync(dollars.Id(), 10m, new { fxRate = 3.80m })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((38m, 3.8m, true), (
            manual.GetProperty("conversion").Money("baseAmount"), manual.GetProperty("conversion").Money("rate"),
            manual.GetProperty("conversion").GetProperty("isManual").GetBoolean()));

        Assert.Equal("ledger.transaction.fx_rate_not_needed", await Accounts.CodeOfAsync(await PostAsync(card.Id(), 10m, new { fxRate = 3.8m })));
        Assert.Equal("ledger.transaction.original_currency_same", await Accounts.CodeOfAsync(await PostAsync(card.Id(), 10m, new { originalAmount = 10m, originalCurrency = "PEN" })));
        Assert.Equal("ledger.transaction.original_price_incomplete", await Accounts.CodeOfAsync(await PostAsync(card.Id(), 10m, new { originalAmount = 10m })));
        Assert.Equal(-58.71m, await brian.BalanceAsync(card.Id()));

        // Editing without the rate goes back to the table's.
        var edited = await brian.Client.PatchAsJsonAsync($"{url}/{manual.Id()}", new
        {
            type = "expense", accountId = dollars.Id(), amount = 10m, categoryId = streaming, occurredOn = "2026-10-12", description = "Netflix"
        });
        var back = await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((37m, false), (back.GetProperty("conversion").Money("baseAmount"), back.GetProperty("conversion").GetProperty("isManual").GetBoolean()));
    }
}
