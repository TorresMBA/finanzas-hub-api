using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PersonalFinance.IntegrationTests.Support;

namespace PersonalFinance.IntegrationTests;

/// <summary>Goals shared by participation: who sees them, what a contribution does to the ledger and what stays private.</summary>
[Collection(ApiCollection.Name)]
public class GoalsTests(ApiFixture api)
{
    private static string Url(Guid workspaceId, string path = "") => $"/api/v1/workspaces/{workspaceId}/goals{path}";

    private static Task<HttpResponseMessage> MoveAsync(TestAccount person, Guid workspaceId, Guid goalId, string what, decimal amount, Guid accountId, string? key = null) =>
        person.Client.PostWithKeyAsync(Url(workspaceId, $"/{goalId}/{what}"), new { amount, accountId }, key);

    private static async Task<JsonElement> GoalAsync(TestAccount person, Guid workspaceId, Guid goalId) =>
        (await person.Client.GetFromJsonAsync<JsonElement>(Url(workspaceId, $"/{goalId}"))).GetProperty("goal");

    private static async Task<List<string?>> NoticeTitlesAsync(TestAccount person) =>
        (await person.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications")).GetProperty("items").EnumerateArray()
            .Select(notice => notice.GetProperty("title").GetString()).ToList();

    [Fact]
    public async Task Brian_contributes_500_to_a_goal_shared_with_Juan_and_Pedro_who_is_not_in_it_never_learns_it_exists()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;
        var (juanEmail, pedroEmail) = (Accounts.NewEmail("juan"), Accounts.NewEmail("pedro"));
        var juan = await api.RegisterAsync("Juan", invitationToken: await api.InviteAsync(brian, workspaceId, juanEmail), email: juanEmail);
        var pedro = await api.RegisterAsync("Pedro", invitationToken: await api.InviteAsync(brian, workspaceId, pedroEmail), email: pedroEmail);
        var bcp = await brian.OpenAccountAsync("BCP", 2000m);
        var juansBank = await juan.OpenAccountAsync("Interbank", 1000m, workspaceId: workspaceId);

        var created = await brian.Client.PostAsJsonAsync(Url(workspaceId), new { name = "Viaje Japón", targetAmount = 1000m, participantUserIds = new[] { juan.UserId } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var goalId = (await created.Content.ReadFromJsonAsync<JsonElement>()).Id();

        // Brian puts in 500 from BCP. Sending it again with the same key does not put in another 500.
        var key = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.Created, (await MoveAsync(brian, workspaceId, goalId, "contributions", 500m, bcp.Id(), key)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await MoveAsync(brian, workspaceId, goalId, "contributions", 500m, bcp.Id(), key)).StatusCode);
        Assert.Equal(1500m, await brian.BalanceAsync(bcp.Id()));

        // In his ledger it is a transfer to his own fund, not an expense; the fund counts in his net worth, so it does not go down.
        var transfer = Assert.Single((await brian.TransactionsAsync("?type=transfer")).GetProperty("items").EnumerateArray());
        Assert.Equal(("Aporte a Viaje Japón", "BCP", "Meta: Viaje Japón", 500m), (
            transfer.GetProperty("description").GetString(), transfer.GetProperty("transfer").GetProperty("fromAccountName").GetString(),
            transfer.GetProperty("transfer").GetProperty("toAccountName").GetString(), transfer.Money("amount")));
        Assert.Empty((await brian.TransactionsAsync("?type=expense")).GetProperty("items").EnumerateArray());
        Assert.Equal(2000m, (await brian.SummaryAsync()).Money("netWorth"));
        var fundId = (await GoalAsync(brian, workspaceId, goalId)).GetProperty("myFundAccountId").GetGuid();
        Assert.Equal(500m, await brian.BalanceAsync(fundId));

        // The money of a goal only moves through the goal: not by voiding the transfer, not by spending from the fund.
        var voided = await brian.Client.PostAsJsonAsync($"{Books.TransactionsUrl(workspaceId)}/{transfer.Id()}/void", new { reason = "Prueba" });
        Assert.Equal("ledger.transaction.owned_by_module", await Accounts.CodeOfAsync(voided));
        Assert.Equal("ledger.account.managed_by_goal", await Accounts.CodeOfAsync(await brian.PostTransactionAsync(fundId, "expense", 10m, "Supermercado")));
        Assert.Equal("ledger.account.managed_by_goal", await Accounts.CodeOfAsync(await brian.PostTransferAsync(fundId, bcp.Id(), 10m)));

        // The progress went up for Brian and for Juan. Juan sees how much Brian put in, and nothing about where from.
        var forJuan = await GoalAsync(juan, workspaceId, goalId);
        Assert.Equal((500m, 0m, true, false), (forJuan.Money("saved"), forJuan.Money("mySaved"), forJuan.GetProperty("isShared").GetBoolean(), forJuan.GetProperty("isOwner").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, forJuan.GetProperty("myFundAccountId").ValueKind);
        Assert.Equal(500m, forJuan.GetProperty("participants").EnumerateArray().Single(participant => participant.GetProperty("displayName").GetString() == "Brian").Money("saved"));
        var detail = (await juan.Client.GetStringAsync(Url(workspaceId, $"/{goalId}")));
        Assert.DoesNotContain(bcp.Id().ToString(), detail);
        Assert.DoesNotContain(fundId.ToString(), detail);
        Assert.Equal(HttpStatusCode.NotFound, (await juan.Client.GetAsync($"{Books.AccountsUrl(workspaceId)}/{fundId}")).StatusCode);
        Assert.Contains("Brian aportó a «Viaje Japón»", await NoticeTitlesAsync(juan));

        // Pedro belongs to the workspace and not to the goal: for him it does not exist, whatever he tries.
        Assert.Empty((await pedro.Client.GetFromJsonAsync<JsonElement>(Url(workspaceId))).GetProperty("goals").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await pedro.Client.GetAsync(Url(workspaceId, $"/{goalId}"))).StatusCode);
        var pedrosBank = await pedro.OpenAccountAsync("BBVA", 100m, workspaceId: workspaceId);
        Assert.Equal(HttpStatusCode.NotFound, (await MoveAsync(pedro, workspaceId, goalId, "contributions", 50m, pedrosBank.Id())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await pedro.Client.PostAsync(Url(workspaceId, $"/{goalId}/cancel"), null)).StatusCode);
        Assert.Empty(await NoticeTitlesAsync(pedro));

        // Juan is a participant, not the owner, and his contribution comes from an account of his own.
        Assert.Equal(HttpStatusCode.Forbidden, (await juan.Client.PatchAsJsonAsync(Url(workspaceId, $"/{goalId}"), new { name = "Mío", targetAmount = 5m })).StatusCode);
        Assert.Equal("ledger.account.not_found", await Accounts.CodeOfAsync(await MoveAsync(juan, workspaceId, goalId, "contributions", 500m, bcp.Id())));
        Assert.Equal(HttpStatusCode.Created, (await MoveAsync(juan, workspaceId, goalId, "contributions", 500m, juansBank.Id())).StatusCode);
        Assert.Equal(("completed", 1000m), ((await GoalAsync(brian, workspaceId, goalId)).GetProperty("health").GetString(), (await GoalAsync(brian, workspaceId, goalId)).Money("saved")));
        Assert.Single(await NoticeTitlesAsync(brian), title => title == "Meta alcanzada: «Viaje Japón»");

        // Each one takes out only what is theirs, and nobody is removed with money still in.
        Assert.Equal("goals.withdrawal.exceeds_own", await Accounts.CodeOfAsync(await MoveAsync(juan, workspaceId, goalId, "withdrawals", 600m, juansBank.Id())));
        Assert.Equal(HttpStatusCode.Created, (await MoveAsync(juan, workspaceId, goalId, "withdrawals", 200m, juansBank.Id())).StatusCode);
        Assert.Equal((700m, 800m), (await juan.BalanceAsync(juansBank.Id(), workspaceId), (await GoalAsync(brian, workspaceId, goalId)).Money("saved")));
        Assert.Equal("goals.participant.has_balance", await Accounts.CodeOfAsync(await brian.Client.DeleteAsync(Url(workspaceId, $"/{goalId}/participants/{juan.UserId}"))));
        // Pedro is brought in later: from then on the goal exists for him too.
        Assert.Equal(HttpStatusCode.OK, (await brian.Client.PostAsJsonAsync(Url(workspaceId, $"/{goalId}/participants"), new { userId = pedro.UserId })).StatusCode);
        Assert.Equal(800m, (await GoalAsync(pedro, workspaceId, goalId)).Money("saved"));
        Assert.Contains((await brian.DashboardAsync(DateTime.UtcNow.ToString("yyyy-MM"))).GetProperty("goals").EnumerateArray(), goal => goal.GetProperty("name").GetString() == "Viaje Japón");
    }

    [Fact]
    public async Task Saving_1000_for_a_trip_keeps_the_net_worth_and_spending_it_is_an_expense_of_the_month_marked_as_paid_with_the_goal()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;
        var juanEmail = Accounts.NewEmail("juan");
        var juan = await api.RegisterAsync("Juan", invitationToken: await api.InviteAsync(brian, workspaceId, juanEmail), email: juanEmail);
        var bcp = (await brian.OpenAccountAsync("BCP", 2000m)).Id();
        var created = await brian.Client.PostAsJsonAsync(Url(workspaceId), new { name = "Viaje Cusco", targetAmount = 1000m, participantUserIds = new[] { juan.UserId } });
        var goalId = (await created.Content.ReadFromJsonAsync<JsonElement>()).Id();

        Assert.Equal(HttpStatusCode.Created, (await MoveAsync(brian, workspaceId, goalId, "contributions", 1000m, bcp)).StatusCode);
        Assert.Equal((1000m, 2000m), (await brian.BalanceAsync(bcp), (await brian.SummaryAsync()).Money("netWorth")));

        // Juan put nothing in: there is nothing of his to spend.
        var leisure = await brian.CategoryIdAsync("Suscripciones y ocio");
        var juansBank = (await juan.OpenAccountAsync("Interbank", 500m, workspaceId: workspaceId)).Id();
        var byJuan = await juan.Client.PostWithKeyAsync(
            Url(workspaceId, $"/{goalId}/spend"), new { amount = 100m, accountId = juansBank, categoryId = leisure });
        Assert.False(byJuan.IsSuccessStatusCode);

        // Brian spends all of it on the trip: the fund empties, BCP is where it was and the month has an expense of 1 000.
        var spent = await brian.Client.PostWithKeyAsync(Url(workspaceId, $"/{goalId}/spend"), new { amount = 1000m, accountId = bcp, categoryId = leisure, description = "Pasajes a Cusco" });
        Assert.Equal(HttpStatusCode.Created, spent.StatusCode);
        Assert.Equal(0m, (await GoalAsync(brian, workspaceId, goalId)).Money("mySaved"));
        Assert.Equal((1000m, 1000m), (await brian.BalanceAsync(bcp), (await brian.SummaryAsync()).Money("netWorth")));
        var expense = Assert.Single((await brian.TransactionsAsync("?type=expense")).GetProperty("items").EnumerateArray());
        Assert.Equal(("Pasajes a Cusco", 1000m, true, true), (
            expense.GetProperty("description").GetString(), expense.Money("amount"), expense.GetProperty("fundedByGoal").GetBoolean(), expense.GetProperty("isEditable").GetBoolean()));

        var month = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5)).ToString("yyyy-MM");
        var report = await brian.Client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{workspaceId}/reports/summary?period=month&anchor={month}");
        Assert.Equal((1000m, 1000m), (report.GetProperty("current").Money("expense"), report.GetProperty("current").Money("goalFundedExpense")));
    }
}
