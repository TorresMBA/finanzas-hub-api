using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.IntegrationTests.Support;

namespace PersonalFinance.IntegrationTests;

/// <summary>Reports, their download and the search: the same figures as the ledger, and only the person's own.</summary>
[Collection(ApiCollection.Name)]
public class ReportsTests(ApiFixture api)
{
    private static string Url(TestAccount person, string path) => $"/api/v1/workspaces/{person.PersonalWorkspaceId}/{path}";

    private static async Task<JsonElement> GetAsync(TestAccount person, string path)
    {
        var response = await person.Client.GetAsync(Url(person, path));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static (decimal Income, decimal Expense, decimal Savings, decimal? Rate, decimal Daily, int Days) Totals(JsonElement totals) => (
        totals.GetProperty("income").GetDecimal(), totals.GetProperty("expense").GetDecimal(), totals.GetProperty("savings").GetDecimal(),
        totals.GetProperty("savingsRate").ValueKind == JsonValueKind.Null ? null : totals.GetProperty("savingsRate").GetDecimal(),
        totals.GetProperty("dailyExpense").GetDecimal(), totals.GetProperty("days").GetInt32());

    [Fact]
    public async Task A_report_adds_up_like_the_ledger_compares_with_the_period_before_and_downloads_only_the_persons_own()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var bcp = (await brian.OpenAccountAsync("BCP", 5000m)).Id();

        // September 2025, the period before.
        await brian.EarnAsync(bcp, 1000m, occurredOn: "2025-09-05");
        await brian.SpendAsync(bcp, 200m, merchant: "Plaza Vea", occurredOn: "2025-09-10");

        // October 2025: the same shop written three ways, and a taxi that names no shop.
        await brian.EarnAsync(bcp, 2000m, occurredOn: "2025-10-01");
        await brian.SpendAsync(bcp, 100m, merchant: "Wong", occurredOn: "2025-10-03");
        await brian.SpendAsync(bcp, 50m, category: "Supermercado/Despensa", merchant: "WONG ", occurredOn: "2025-10-04");
        await brian.SpendAsync(bcp, 10m, merchant: "Wong", occurredOn: "2025-10-20");
        await brian.SpendAsync(bcp, 30m, category: "Transporte/Taxi y apps", description: "Uber 1234", occurredOn: "2025-10-21");

        var month = await GetAsync(brian, "reports/summary?period=month&anchor=2025-10");
        Assert.Equal(("2025-10", "PEN"), (month.GetProperty("label").GetString(), month.GetProperty("baseCurrency").GetString()));
        Assert.Equal((2000m, 190m, 1810m, 90.5m, 6.13m, 31), Totals(month.GetProperty("current")));
        Assert.Equal((1000m, 200m, 800m, 80.0m, 6.67m, 30), Totals(month.GetProperty("previous")));

        // A subcategory counts in its category; a category without expenses before has no change to show.
        var categories = month.GetProperty("categories").EnumerateArray()
            .Select(row => (row.GetProperty("name").GetString(), row.GetProperty("amount").GetDecimal(), row.GetProperty("share").GetDecimal(),
                row.GetProperty("previousAmount").GetDecimal(), row.GetProperty("change").ValueKind == JsonValueKind.Null ? (decimal?)null : row.GetProperty("change").GetDecimal()))
            .ToList();
        Assert.Equal([("Supermercado", 160m, 84.2m, 200m, -20.0m), ("Transporte", 30m, 15.8m, 0m, null)], categories);
        var merchants = month.GetProperty("merchants").EnumerateArray()
            .Select(row => (row.GetProperty("name").GetString(), row.GetProperty("count").GetInt32(), row.GetProperty("amount").GetDecimal())).ToList();
        Assert.Equal([("Wong", 3, 160m), ("Uber 1234", 1, 30m)], merchants);
        var months = month.GetProperty("months").EnumerateArray().ToList();
        Assert.Equal(("2025-05", "2025-10", 90.5m), (months[0].GetProperty("month").GetString(), months[^1].GetProperty("month").GetString(), months[^1].GetProperty("savingsRate").GetDecimal()));

        // The same figure the ledger gives Transactions and Categories for the month.
        var ledger = await GetAsync(brian, "transactions/category-totals?from=2025-10-01&to=2025-10-31");
        var salary = await brian.CategoryIdAsync("Sueldo");
        Assert.Equal(190m, ledger.GetProperty("categories").EnumerateArray()
            .Where(row => row.GetProperty("categoryId").GetGuid() != salary).Sum(row => row.GetProperty("amount").GetDecimal()));

        // A quarter is compared with the quarter before, a year with the year before.
        var quarter = await GetAsync(brian, "reports/summary?period=quarter&anchor=2025-11");
        Assert.Equal(("2025-T4", "2025-07-01", "2025-09-30"), (quarter.GetProperty("label").GetString(), quarter.GetProperty("previousFrom").GetString(), quarter.GetProperty("previousTo").GetString()));
        Assert.Equal((2000m, 190m, 1810m, 90.5m, 2.07m, 92), Totals(quarter.GetProperty("current")));
        Assert.Equal((1000m, 200m), (quarter.GetProperty("previous").GetProperty("income").GetDecimal(), quarter.GetProperty("previous").GetProperty("expense").GetDecimal()));
        var year = await GetAsync(brian, "reports/summary?period=year&anchor=2025-03");
        Assert.Equal((3000m, 390m, 12), (year.GetProperty("current").GetProperty("income").GetDecimal(), year.GetProperty("current").GetProperty("expense").GetDecimal(), year.GetProperty("months").GetArrayLength()));
        Assert.Equal(HttpStatusCode.BadRequest, (await brian.Client.GetAsync(Url(brian, "reports/summary?period=month&anchor=octubre"))).StatusCode);

        // The movements as CSV: readable by Excel with its accents, one line each, the opening balance left out.
        var csv = await brian.Client.GetAsync(Url(brian, "reports/export?period=month&anchor=2025-10&format=csv"));
        Assert.Equal(("text/csv", "finanzas-2025-10.csv"), (csv.Content.Headers.ContentType?.MediaType, csv.Content.Headers.ContentDisposition?.FileNameStar));
        var bytes = await csv.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Fecha,Tipo,Descripción,Comercio,Categoría,Cuenta,Cuenta destino,Monto,Moneda,Nota", lines[0]);
        Assert.Equal(6, lines.Length);
        Assert.Equal("2025-10-21,Gasto,Uber 1234,,Transporte / Taxi y apps,BCP,,-30.00,PEN,", lines[^1]);

        // The workbook has the summary first, then the movements.
        var xlsx = await brian.Client.GetAsync(Url(brian, "reports/export?period=month&anchor=2025-10&format=xlsx"));
        Assert.Equal("finanzas-2025-10.xlsx", xlsx.Content.Headers.ContentDisposition?.FileNameStar);
        var workbook = await xlsx.Content.ReadAsByteArrayAsync();
        var summary = await api.ExecuteAsync(services => Task.FromResult(services.GetRequiredService<ISpreadsheets>().ReadFirstSheet(workbook)));
        Assert.Equal(["Ingresos", "2000", "1000"], summary!.Single(row => row[0] == "Ingresos").Take(3));

        // The search finds his own; Juan, a member of his workspace, finds nothing of his.
        var found = await GetAsync(brian, "search?q=wong");
        Assert.Equal(3, found.GetProperty("movements").GetArrayLength());
        Assert.Equal("BCP", (await GetAsync(brian, "search?q=bc")).GetProperty("accounts")[0].GetProperty("name").GetString());
        Assert.Equal("reporting.search_invalid", await Accounts.CodeOfAsync(await brian.Client.GetAsync(Url(brian, "search?q=w"))));

        var juanEmail = Accounts.NewEmail("juan");
        var juan = await api.RegisterAsync("Juan", invitationToken: await api.InviteAsync(brian, brian.PersonalWorkspaceId, juanEmail), email: juanEmail);
        var theirs = await juan.Client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{brian.PersonalWorkspaceId}/search?q=wong");
        Assert.Equal((0, 0), (theirs.GetProperty("movements").GetArrayLength(), theirs.GetProperty("accounts").GetArrayLength()));
        var report = await juan.Client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{brian.PersonalWorkspaceId}/reports/summary?period=month&anchor=2025-10");
        Assert.Equal(0m, report.GetProperty("current").GetProperty("expense").GetDecimal());
    }
}
