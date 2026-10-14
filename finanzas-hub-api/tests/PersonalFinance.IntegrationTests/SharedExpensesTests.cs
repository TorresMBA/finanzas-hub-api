using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PersonalFinance.IntegrationTests.Support;

namespace PersonalFinance.IntegrationTests;

/// <summary>Splitting expenses: what each one owes, what a payment does to each ledger and who sees the group.</summary>
[Collection(ApiCollection.Name)]
public class SharedExpensesTests(ApiFixture api)
{
    private static string Url(Guid workspaceId, string path = "") => $"/api/v1/workspaces/{workspaceId}/shared-groups{path}";

    private static JsonElement Expense(JsonElement detail, string description) =>
        detail.GetProperty("expenses").EnumerateArray().Single(expense => expense.GetProperty("description").GetString() == description);

    private static decimal[] Shares(JsonElement expense, params TestAccount[] people) =>
        people.Select(person => expense.GetProperty("shares").EnumerateArray().Single(share => share.GetProperty("userId").GetGuid() == person.UserId).Money("amount")).ToArray();

    private static async Task<JsonElement> PostAsync(TestAccount person, string url, object body)
    {
        var response = await person.Client.PostWithKeyAsync(url, body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task A_dinner_of_300_is_split_three_ways_and_each_payment_shows_in_the_group_and_in_the_ledger_of_who_registers_it()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var hub = brian.PersonalWorkspaceId;
        async Task<TestAccount> JoinAsync(string name)
        {
            var email = Accounts.NewEmail(name.ToLowerInvariant());
            return await api.RegisterAsync(name, invitationToken: await api.InviteAsync(brian, hub, email), email: email);
        }

        var (juan, pedro, ana) = (await JoinAsync("Juan"), await JoinAsync("Pedro"), await JoinAsync("Ana"));
        var bcp = await brian.OpenAccountAsync("BCP", 1000m);
        var juansBank = await juan.OpenAccountAsync("Interbank", 500m);
        var food = await brian.CategoryIdAsync("Supermercado");

        var created = await brian.Client.PostAsJsonAsync(Url(hub), new { name = "Depa", memberUserIds = new[] { juan.UserId, pedro.UserId } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var groupId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("group").Id();
        var expenses = Url(hub, $"/{groupId}/expenses");
        var settlements = Url(hub, $"/{groupId}/settlements");
        object[] everyone(params decimal?[] values) => new[] { brian, juan, pedro }.Select((person, index) => (object)new { userId = person.UserId, value = values.ElementAtOrDefault(index) }).ToArray();

        // Brian paid the dinner from BCP: the whole 300 is an expense of his, and each of the three owes 100 of it.
        var dinner = Expense(await PostAsync(brian, expenses, new
        {
            description = "Cena", totalAmount = 300m, paidByUserId = brian.UserId, splitType = "equal", participants = everyone(), accountId = bcp.Id(), categoryId = food
        }), "Cena");
        Assert.Equal([100m, 100m, 100m], Shares(dinner, brian, juan, pedro));
        Assert.Equal(("pending", 700m), (dinner.GetProperty("status").GetString(), await brian.BalanceAsync(bcp.Id())));

        // Juan pays his 100 from his own account: his balance goes down and the dinner is partially paid.
        var juanFood = await juan.CategoryIdAsync("Supermercado");
        Assert.Equal("shared.settlement.exceeds_debt", await Accounts.CodeOfAsync(await juan.Client.PostWithKeyAsync(settlements, new { fromUserId = juan.UserId, toUserId = brian.UserId, amount = 150m })));
        var afterJuan = await PostAsync(juan, settlements, new { fromUserId = juan.UserId, toUserId = brian.UserId, amount = 100m, accountId = juansBank.Id(), categoryId = juanFood });
        Assert.Equal(("partiallyPaid", 400m), (Expense(afterJuan, "Cena").GetProperty("status").GetString(), await juan.BalanceAsync(juansBank.Id())));
        Assert.DoesNotContain(bcp.Id().ToString(), afterJuan.GetRawText());

        // Pedro pays in cash, with no account: the dinner is settled. It says who paid whom, how much and when.
        var afterPedro = await PostAsync(pedro, settlements, new { fromUserId = pedro.UserId, toUserId = brian.UserId, amount = 100m });
        Assert.Equal("settled", Expense(afterPedro, "Cena").GetProperty("status").GetString());
        // Brian, who was paid by both, sees both payments; Pedro only his own.
        Assert.Equal("Pedro", Assert.Single(afterPedro.GetProperty("settlements").EnumerateArray()).GetProperty("fromName").GetString());
        var juansPayment = (await brian.Client.GetFromJsonAsync<JsonElement>(Url(hub, $"/{groupId}"))).GetProperty("settlements").EnumerateArray()
            .Single(payment => payment.GetProperty("fromName").GetString() == "Juan");
        Assert.Equal(("Brian", 100m, false), (juansPayment.GetProperty("toName").GetString(), juansPayment.Money("amount"), juansPayment.GetProperty("isVoided").GetBoolean()));
        Assert.False(string.IsNullOrEmpty(juansPayment.GetProperty("occurredOn").GetString()));

        // Brian puts Juan's payment in BCP: it comes back as a refund of the category, so his food expense is 200 and not an income of 100.
        var linkUrl = $"{settlements}/{juansPayment.Id()}/link-account";
        Assert.Equal(HttpStatusCode.OK, (await brian.Client.PostWithKeyAsync(linkUrl, new { accountId = bcp.Id() })).StatusCode);
        Assert.Equal("shared.settlement.already_linked", await Accounts.CodeOfAsync(await brian.Client.PostWithKeyAsync(linkUrl, new { accountId = bcp.Id() })));
        var totals = (await brian.TransactionsAsync()).GetProperty("baseTotals");
        Assert.Equal((800m, 0m, 200m), (await brian.BalanceAsync(bcp.Id()), totals.Money("income"), totals.Money("expense")));

        // The other two ways of splitting the same 300.
        var byPercentage = Expense(await PostAsync(brian, expenses, new
        {
            description = "Mercado", totalAmount = 300m, paidByUserId = brian.UserId, splitType = "percentage", participants = everyone(50, 30, 20)
        }), "Mercado");
        var byAmount = Expense(await PostAsync(juan, expenses, new
        {
            description = "Luz", totalAmount = 300m, paidByUserId = brian.UserId, splitType = "fixedAmount", participants = everyone(150, 100, 50)
        }), "Luz");
        Assert.Equal([150m, 90m, 60m], Shares(byPercentage, brian, juan, pedro));
        Assert.Equal([150m, 100m, 50m], Shares(byAmount, brian, juan, pedro));
        // The list of groups says it without opening the group: what Juan owes, in how many expenses, and with whom he shares.
        var juansCard = (await juan.Client.GetFromJsonAsync<JsonElement>(Url(hub))).GetProperty("groups").EnumerateArray().Single();
        Assert.Equal((-190m, 0m, 190m, 2), (juansCard.Money("myBalance"), juansCard.Money("owedToMe"), juansCard.Money("iOwe"), juansCard.GetProperty("pendingExpenses").GetInt32()));
        Assert.Equal(["Brian", "Pedro", "Juan"], juansCard.GetProperty("memberNames").EnumerateArray().Select(name => name.GetString()));

        // A taxi only Brian and Juan shared: Pedro is in the group and not in that expense, so for him it is not there,
        // and neither is what Juan owes Brian.
        var taxi = await PostAsync(brian, expenses, new
        {
            description = "Taxi", totalAmount = 40m, paidByUserId = brian.UserId, splitType = "equal",
            participants = new[] { new { userId = brian.UserId }, new { userId = juan.UserId } }
        });
        Assert.Equal([20m, 20m], Shares(Expense(taxi, "Taxi"), brian, juan));
        var forPedro = await pedro.Client.GetFromJsonAsync<JsonElement>(Url(hub, $"/{groupId}"));
        Assert.DoesNotContain(forPedro.GetProperty("expenses").EnumerateArray(), expense => expense.GetProperty("description").GetString() == "Taxi");
        Assert.All(forPedro.GetProperty("debts").EnumerateArray(), debt => Assert.Equal(pedro.UserId, debt.GetProperty("fromUserId").GetGuid()));
        Assert.All(forPedro.GetProperty("settlements").EnumerateArray(), payment => Assert.Equal("Pedro", payment.GetProperty("fromName").GetString()));
        Assert.Contains((await juan.Client.GetFromJsonAsync<JsonElement>(Url(hub, $"/{groupId}"))).GetProperty("expenses").EnumerateArray(), expense => expense.GetProperty("description").GetString() == "Taxi");

        // Juan pays 50 from his account and voids it: the movement leaves his account with it, so paying again does not leave two.
        async Task<JsonElement> PayFiftyAsync() => (await PostAsync(juan, settlements, new { fromUserId = juan.UserId, toUserId = brian.UserId, amount = 50m, accountId = juansBank.Id(), categoryId = juanFood }))
            .GetProperty("settlements").EnumerateArray().First(payment => payment.Money("amount") == 50m && !payment.GetProperty("isVoided").GetBoolean());
        var fifty = await PayFiftyAsync();
        Assert.Equal((350m, true), (await juan.BalanceAsync(juansBank.Id()), fifty.GetProperty("canVoid").GetBoolean()));
        var undone = await juan.Client.PostAsync($"{settlements}/{fifty.Id()}/void", null);
        Assert.Equal(HttpStatusCode.OK, undone.StatusCode);
        Assert.Equal(2, (await undone.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("voidsLeftToday").GetInt32());
        Assert.Equal(400m, await juan.BalanceAsync(juansBank.Id()));
        Assert.Equal("shared.settlement.already_voided", await Accounts.CodeOfAsync(await juan.Client.PostAsync($"{settlements}/{fifty.Id()}/void", null)));
        await PayFiftyAsync();
        Assert.Equal((350m, 150m), (await juan.BalanceAsync(juansBank.Id()), (await juan.TransactionsAsync()).GetProperty("baseTotals").Money("expense")));

        // Juan cannot put Brian's expense in an account, nor void the payment Pedro registered.
        Assert.Equal("shared.expense.account_of_payer_only", await Accounts.CodeOfAsync(await juan.Client.PostWithKeyAsync(expenses, new
        {
            description = "Gas", totalAmount = 30m, paidByUserId = brian.UserId, participants = everyone(), accountId = juansBank.Id(), categoryId = juanFood
        })));
        var pedrosPayment = afterPedro.GetProperty("settlements").EnumerateArray().Single(payment => payment.GetProperty("fromName").GetString() == "Pedro");
        Assert.Equal(HttpStatusCode.Forbidden, (await juan.Client.PostAsync($"{settlements}/{pedrosPayment.Id()}/void", null)).StatusCode);

        // Brian reminds Juan once a day, however many times he asks.
        var remind = Url(hub, $"/{groupId}/members/{juan.UserId}/remind");
        Assert.Equal(HttpStatusCode.NoContent, (await brian.Client.PostAsync(remind, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await brian.Client.PostAsync(remind, null)).StatusCode);
        var notices = (await juan.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications")).GetProperty("items").EnumerateArray()
            .Select(notice => notice.GetProperty("title").GetString()).ToList();
        Assert.Single(notices, title => title == "Brian te recuerda un pago");

        // Ana belongs to the workspace and not to the group: for her it does not exist.
        Assert.Empty((await ana.Client.GetFromJsonAsync<JsonElement>(Url(hub))).GetProperty("groups").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await ana.Client.GetAsync(Url(hub, $"/{groupId}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.Client.PostWithKeyAsync(settlements, new { fromUserId = ana.UserId, toUserId = brian.UserId, amount = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.Client.PostAsync(remind, null)).StatusCode);
    }
}
