using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Controllers;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.IntegrationTests.Support;

namespace PersonalFinance.IntegrationTests;

/// <summary>Importing a file: what is new, what the ledger already has, and what ends up in it once confirmed.</summary>
[Collection(ApiCollection.Name)]
public class ImportsTests(ApiFixture api)
{
    private static string Url(TestAccount person, string path = "") => $"/api/v1/workspaces/{person.PersonalWorkspaceId}/imports{path}";

    private static Task<HttpResponseMessage> UploadAsync(TestAccount person, string file, Guid? accountId = null, string name = "movimientos.csv")
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(file));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return person.Client.PostAsync(Url(person, $"?fileName={name}{(accountId is null ? string.Empty : $"&accountId={accountId}")}"), content);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static (int Ready, int Pending, int Duplicates, int Invalid, int Imported) Counts(JsonElement detail)
    {
        var counts = detail.GetProperty("counts");
        int Of(string name) => counts.GetProperty(name).GetInt32();
        return (Of("ready"), Of("pending"), Of("duplicates"), Of("invalid"), Of("imported"));
    }

    private static Task<HttpResponseMessage> CommitAsync(TestAccount person, Guid importId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url(person, $"/{importId}/commit"));
        request.Headers.Add(ApiControllerBase.IdempotencyKeyHeader, Guid.NewGuid().ToString());
        return person.Client.SendAsync(request);
    }

    [Fact]
    public async Task A_bank_statement_brings_only_what_the_ledger_does_not_have_and_a_rule_classifies_it()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var bank = (await brian.OpenAccountAsync("Interbank")).Id();
        var streaming = await brian.CategoryIdAsync("Supermercado");
        await brian.SpendAsync(bank, 25m, description: "Bodega", occurredOn: "2026-10-05");
        var rules = $"/api/v1/workspaces/{brian.PersonalWorkspaceId}/categorization-rules";
        Assert.Equal(HttpStatusCode.Created, (await brian.Client.PostAsJsonAsync(rules, new { pattern = "Netflix", categoryId = streaming })).StatusCode);
        Assert.Equal("imports.rule.duplicate", await Accounts.CodeOfAsync(await brian.Client.PostAsJsonAsync(rules, new { pattern = "NETFLIX", categoryId = streaming })));

        const string Statement = "Fecha;Descripción;Cargo;Abono\n05/10/2026;TIENDA XYZ;25,00;\n06/10/2026;NETFLIX.COM;44,90;\n07/10/2026;ABONO HABERES;;1.500,00\nmal;SIN FECHA;1,00;\n";
        Assert.Equal("imports.account_required", await Accounts.CodeOfAsync(await UploadAsync(brian, Statement)));

        // The 25 of the 5th is already in the ledger, typed by hand; Netflix is classified by the rule; the income has no category yet.
        var uploaded = await UploadAsync(brian, Statement, bank);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var detail = await ReadAsync(uploaded);
        var importId = detail.GetProperty("import").Id();
        Assert.Equal(("csv", (1, 1, 1, 1, 0)), (detail.GetProperty("import").GetProperty("format").GetString(), Counts(detail)));
        var netflix = detail.GetProperty("records").EnumerateArray().Single(record => record.GetProperty("description").GetString() == "NETFLIX.COM");
        Assert.Equal((streaming, "ready"), (netflix.GetProperty("categoryId").GetGuid(), netflix.GetProperty("state").GetString()));

        // It is not confirmed while a line has nowhere to go. Nothing reached the ledger.
        Assert.Equal("imports.unresolved", await Accounts.CodeOfAsync(await CommitAsync(brian, importId)));
        Assert.Equal(-25m, await brian.BalanceAsync(bank));

        var incomes = detail.GetProperty("categories").EnumerateArray().Single(category => category.GetProperty("isIncome").GetBoolean());
        var mapped = await brian.Client.PutAsJsonAsync(Url(brian, $"/{importId}/categories/{incomes.Id()}"), new { categoryId = await brian.CategoryIdAsync("Sueldo") });
        Assert.Equal(HttpStatusCode.NoContent, mapped.StatusCode);

        var committed = await ReadAsync(await CommitAsync(brian, importId));
        Assert.Equal(("committed", 2, (0, 0, 1, 1, 2)), (
            committed.GetProperty("import").GetProperty("status").GetString(), committed.GetProperty("import").GetProperty("importedCount").GetInt32(), Counts(committed)));
        Assert.Equal(-25m - 44.90m + 1500m, await brian.BalanceAsync(bank));
        Assert.Equal("imports.already_committed", await Accounts.CodeOfAsync(await CommitAsync(brian, importId)));

        // The same statement again adds nothing: the ledger has it all.
        var again = await ReadAsync(await UploadAsync(brian, Statement, bank));
        Assert.Equal((0, 0, 3, 1, 0), Counts(again));
        Assert.Equal("imports.nothing_to_import", await Accounts.CodeOfAsync(await CommitAsync(brian, again.GetProperty("import").Id())));
        Assert.Equal(HttpStatusCode.NoContent, (await brian.Client.DeleteAsync(Url(brian, $"/{again.GetProperty("import").Id()}"))).StatusCode);
        Assert.Equal(1430.10m, await brian.BalanceAsync(bank));

        // The imported movements are his like any other: he finds them among his transactions.
        Assert.Contains("NETFLIX.COM", (await brian.TransactionsAsync()).Descriptions());

        // Juan cannot see, change or confirm Brian's import, nor import into Brian's account.
        var juan = await api.RegisterWithActiveWorkspaceAsync("Juan");
        Assert.Equal(HttpStatusCode.NotFound, (await juan.Client.GetAsync($"/api/v1/workspaces/{juan.PersonalWorkspaceId}/imports/{importId}")).StatusCode);
        Assert.Equal("imports.account_not_found", await Accounts.CodeOfAsync(await UploadAsync(juan, Statement, bank)));
        Assert.Empty((await juan.Client.GetFromJsonAsync<JsonElement>(rules.Replace(brian.PersonalWorkspaceId.ToString(), juan.PersonalWorkspaceId.ToString()))).EnumerateArray());
    }

    [Fact]
    public async Task A_Bluecoins_export_arrives_with_its_accounts_categories_and_transfers()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var bcp = (await brian.OpenAccountAsync("BCP", 1000m)).Id();

        const string Export = """
            Type,Date,Set Time,Title,Amount,Currency,Exchange Rate,Category Group Name,Category,Account,Notes,Labels,Status
            New Account,2026-09-01 08:00,,Efectivo,200.00,PEN,1,,(New Account),Efectivo,,,
            Expense,2026-10-01 13:20,,"Almuerzo, con equipo",-42.50,PEN,1,Salidas,Menú del día,Efectivo,,trabajo,
            Income,2026-10-02 09:00,,Sueldo octubre,3500.00,PEN,1,Ingresos,Sueldo,bcp,,,
            Transfer,2026-10-03 10:00,,Retiro cajero,-300.00,PEN,1,(Transfer),(Transfer),BCP,,,
            Transfer,2026-10-03 10:00,,Retiro cajero,300.00,PEN,1,(Transfer),(Transfer),Efectivo,,,
            """;

        var detail = await ReadAsync(await UploadAsync(brian, Export, name: "transactions_list.csv"));
        var importId = detail.GetProperty("import").Id();
        Assert.Equal("bluecoins", detail.GetProperty("import").GetProperty("format").GetString());

        // BCP is recognised by its name. Efectivo is not one of his accounts: only the income, which touches BCP alone, is ready.
        var accounts = detail.GetProperty("accounts").EnumerateArray().ToDictionary(account => account.GetProperty("sourceName").GetString()!, StringComparer.OrdinalIgnoreCase);
        Assert.Equal((bcp, JsonValueKind.Null), (accounts["BCP"].GetProperty("accountId").GetGuid(), accounts["Efectivo"].GetProperty("accountId").ValueKind));
        Assert.Equal((1, 3, 0, 0, 0), Counts(detail));

        // He has "Sueldo" and not "Menú del día": the first is his, the second will be created under its group.
        var categories = detail.GetProperty("categories").EnumerateArray().ToDictionary(category => category.GetProperty("sourceName").GetString()!);
        Assert.Equal((await brian.CategoryIdAsync("Sueldo"), true), (categories["Sueldo"].GetProperty("categoryId").GetGuid(), categories["Menú del día"].GetProperty("create").GetBoolean()));

        var opened = await brian.Client.PutAsJsonAsync(Url(brian, $"/{importId}/accounts/{accounts["Efectivo"].Id()}"), new { createAs = "cash" });
        Assert.Equal(HttpStatusCode.NoContent, opened.StatusCode);
        var committed = await ReadAsync(await CommitAsync(brian, importId));
        Assert.Equal((0, 0, 0, 0, 4), Counts(committed));

        // The opening balance, the lunch and the withdrawal add up in the new account; the salary and the withdrawal in BCP.
        var cash = (await brian.Client.GetFromJsonAsync<JsonElement>(Books.AccountsUrl(brian.PersonalWorkspaceId))).EnumerateArray()
            .Single(account => account.GetProperty("name").GetString() == "Efectivo");
        Assert.Equal(("cash", 200m - 42.50m + 300m, 1000m + 3500m - 300m), (cash.GetProperty("type").GetString(), cash.Money("balance"), await brian.BalanceAsync(bcp)));

        // A transfer is neither an income nor an expense, and the lunch carries its new category and its label.
        var transactions = await brian.TransactionsAsync();
        Assert.Equal((3500m, 42.50m), (transactions.GetProperty("baseTotals").Money("income"), transactions.GetProperty("baseTotals").Money("expense")));
        var lunch = transactions.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("description").GetString() == "Almuerzo, con equipo");
        Assert.Equal(await brian.CategoryIdAsync("Salidas/Menú del día"), lunch.GetProperty("category").GetProperty("id").GetGuid());
        Assert.Contains("trabajo", lunch.GetRawText());

        // Exporting again from Bluecoins, with one more lunch, brings that lunch and nothing else.
        var again = await ReadAsync(await UploadAsync(brian, Export + "\nExpense,2026-10-06 09:00,,Pan,-3.00,PEN,1,Salidas,Menú del día,Efectivo,,,\n", name: "transactions_list.csv"));
        Assert.Equal((1, 0, 4, 0, 0), Counts(again));
    }

    [Fact]
    public async Task A_statement_saved_as_an_Excel_workbook_is_read_like_its_csv()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var bcp = (await brian.OpenAccountAsync("BCP", 1000m)).Id();
        var workbook = await api.ExecuteAsync(services => Task.FromResult(services.GetRequiredService<ISpreadsheets>().Write(
        [
            new SpreadsheetSheet("Movimientos", [
                ["Fecha", "Descripción", "Monto"],
                [new DateOnly(2025, 8, 3), "Wong", -54.90m],
                [new DateOnly(2025, 8, 5), "Abono sueldo", 2500m]
            ])
        ])));

        var content = new ByteArrayContent(workbook);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var detail = await ReadAsync(await brian.Client.PostAsync(Url(brian, $"?fileName=extracto.xlsx&accountId={bcp}"), content));

        Assert.Equal(("xlsx", 2, 0), (detail.GetProperty("import").GetProperty("format").GetString(), detail.GetProperty("import").GetProperty("recordCount").GetInt32(), Counts(detail).Invalid));
        var lines = detail.GetProperty("records").EnumerateArray()
            .Select(record => (record.GetProperty("occurredOn").GetString(), record.GetProperty("kind").GetString(), record.GetProperty("amount").GetDecimal()))
            .OrderBy(line => line.Item1).ToList();
        Assert.Equal([("2025-08-03", "expense", 54.90m), ("2025-08-05", "income", 2500m)], lines);
    }
}
