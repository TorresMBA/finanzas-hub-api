using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.IntegrationTests.Support;
using PersonalFinance.Modules.Scheduling.Application;

namespace PersonalFinance.IntegrationTests;

/// <summary>Investments: money moved into them is a transfer, their gain counts once, and only their owner sees them.</summary>
[Collection(ApiCollection.Name)]
public class InvestmentsTests(ApiFixture api)
{
    // The workspaces of the tests are in Lima.
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));
    private static readonly string Month = Today.ToString("yyyy-MM");

    private static string Url(TestAccount person, string path) => $"/api/v1/workspaces/{person.PersonalWorkspaceId}/{path}";

    private static async Task<JsonElement> PostAsync(TestAccount person, string path, object body)
    {
        var response = await person.Client.PostWithKeyAsync(Url(person, path), body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task A_deposit_of_15000_lowers_the_account_but_not_the_net_worth_and_only_a_redemption_gain_is_income()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var bcp = (await brian.OpenAccountAsync("BCP", 20_000m)).Id();
        Assert.Equal(20_000m, (await brian.DashboardAsync(Month)).GetProperty("netWorth").Money("amount"));

        // A term deposit of 15 000 at 6 % for a year.
        var deposit = await PostAsync(brian, "holdings", new
        {
            type = "termDeposit", name = "Plazo fijo BCP", institution = "BCP", amount = 15_000m, openedOn = Today.ToString("yyyy-MM-dd"), fromAccountId = bcp,
            annualRate = 6m, termDays = 360
        });
        Assert.Equal((15_000m, 15_900.00m), (deposit.GetProperty("holding").Money("value"), deposit.GetProperty("holding").Money("projectedValue")));
        Assert.Equal(5_000m, await brian.BalanceAsync(bcp));
        var dashboard = await brian.DashboardAsync(Month);
        Assert.Equal((20_000m, 0m), (dashboard.GetProperty("netWorth").Money("amount"), dashboard.Money("expense")));

        // Its account is moved by the investment only.
        var account = deposit.GetProperty("accountId").GetGuid();
        Assert.Equal("ledger.account.managed_by_investment", await Accounts.CodeOfAsync(await brian.PostTransferAsync(bcp, account, 100m, occurredOn: Today.ToString("yyyy-MM-dd"))));

        // An ETF bought for 1 000 is worth 1 200: the gain shows in the net worth once, on top of the capital.
        var etf = await PostAsync(brian, "holdings", new
        {
            type = "etf", name = "Vanguard S&P 500", ticker = "voo", amount = 1_000m, quantity = 2m, openedOn = Today.ToString("yyyy-MM-dd"), fromAccountId = bcp
        });
        var etfId = etf.GetProperty("holding").Id();
        var valued = await brian.Client.PostAsJsonAsync(Url(brian, $"holdings/{etfId}/valuations"), new { onDate = Today.ToString("yyyy-MM-dd"), unitPrice = 600m });
        Assert.Equal(HttpStatusCode.OK, valued.StatusCode);
        var summary = await brian.Client.GetFromJsonAsync<JsonElement>(Url(brian, "investments/summary"));
        Assert.Equal((16_200m, 16_000m, 200m), (summary.Money("value"), summary.Money("invested"), summary.Money("gain")));
        Assert.Equal(["termDeposit", "etf"], summary.GetProperty("distribution").EnumerateArray().Select(share => share.GetProperty("type").GetString()));
        Assert.Equal(20_200m, (await brian.DashboardAsync(Month)).GetProperty("netWorth").Money("amount"));

        // Cuentas shows the same figure as the home page, not the capital alone.
        var netWorth = await brian.Client.GetFromJsonAsync<JsonElement>(Url(brian, "net-worth"));
        Assert.Equal((20_200m, 200m), (netWorth.Money("amount"), netWorth.Money("unrealizedGain")));

        // Redeeming 600 of the 1 200 takes back 500 of capital and 100 of gain, which is an income of the month.
        var redeemed = await PostAsync(brian, $"holdings/{etfId}/withdrawals", new { amount = 600m, quantity = 1m, occurredOn = Today.ToString("yyyy-MM-dd"), accountId = bcp });
        var withdrawal = redeemed.GetProperty("movements")[0];
        Assert.Equal((500m, 100m), (withdrawal.Money("capital"), withdrawal.Money("realized")));
        Assert.Equal(4_600m, await brian.BalanceAsync(bcp));
        dashboard = await brian.DashboardAsync(Month);
        Assert.Equal((100m, 0m, 20_200m), (dashboard.Money("income"), dashboard.Money("expense"), dashboard.GetProperty("netWorth").Money("amount")));
        Assert.Equal("investments.withdrawal_too_large",
            await Accounts.CodeOfAsync(await brian.Client.PostWithKeyAsync(Url(brian, $"holdings/{etfId}/withdrawals"), new { amount = 601m, occurredOn = Today.ToString("yyyy-MM-dd"), accountId = bcp })));

        // Nobody else sees them, not even a member of his workspace.
        var juanEmail = Accounts.NewEmail("juan");
        var juan = await api.RegisterAsync("Juan", invitationToken: await api.InviteAsync(brian, brian.PersonalWorkspaceId, juanEmail), email: juanEmail);
        Assert.Equal(HttpStatusCode.NotFound, (await juan.Client.GetAsync(Url(brian, $"holdings/{etfId}"))).StatusCode);
        Assert.Equal(0m, (await juan.Client.GetFromJsonAsync<JsonElement>(Url(brian, "investments/summary"))).Money("value"));

        // A deposit that matures within a week is told once by the daily cycle.
        await PostAsync(brian, "holdings", new
        {
            type = "termDeposit", name = "CTS", amount = 1_000m, openedOn = Today.AddDays(-85).ToString("yyyy-MM-dd"), fromAccountId = bcp, annualRate = 5m, termDays = 90
        });
        for (var run = 0; run < 2; run++)
        {
            await api.ExecuteAsync(
                services => services.GetRequiredService<ICommandHandler<RunScheduleCommand, RunScheduleResponse>>().HandleAsync(new RunScheduleCommand(), CancellationToken.None),
                brian.UserId, brian.PersonalWorkspaceId);
        }

        var notices = (await brian.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications")).GetProperty("items").EnumerateArray()
            .Select(notice => notice.GetProperty("title").GetString()).ToList();
        Assert.Single(notices, title => title!.StartsWith("Tu depósito «CTS» vence", StringComparison.Ordinal));
    }
}
