using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Infrastructure.Persistence;
using PersonalFinance.IntegrationTests.Support;

namespace PersonalFinance.IntegrationTests;

/// <summary>Accounts and transactions: the only source of every balance, private to each person.</summary>
[Collection(ApiCollection.Name)]
public class LedgerTests(ApiFixture api)
{
    [Fact]
    public async Task An_account_with_1000_shows_946_after_a_54_expense_and_1000_again_once_it_is_voided()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var account = await brian.OpenAccountAsync("BCP Sueldo", 1000m);
        Assert.Equal(1000m, account.GetProperty("balance").GetDecimal());

        var key = Guid.NewGuid().ToString();
        var first = await brian.PostTransactionAsync(account.Id(), "expense", 54m, "Supermercado", "Wong", key: key);
        var replay = await brian.PostTransactionAsync(account.Id(), "expense", 54m, "Supermercado", "Wong", key: key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var expense = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expense.Id(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).Id());
        Assert.Equal(946m, await brian.BalanceAsync(account.Id()));
        Assert.Equal(["Saldo inicial", "Wong"], (await brian.TransactionsAsync()).Descriptions().Order());

        var voided = await brian.Client.PostAsJsonAsync($"{Books.TransactionsUrl(brian.PersonalWorkspaceId)}/{expense.Id()}/void", new { reason = "Duplicado" });

        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
        Assert.Equal(1000m, await brian.BalanceAsync(account.Id()));
        Assert.Equal(["Saldo inicial"], (await brian.TransactionsAsync()).Descriptions());
        var kept = (await brian.TransactionsAsync("?status=voided")).GetProperty("items")[0];
        Assert.Equal(("voided", "Duplicado", false), (kept.GetProperty("status").GetString(), kept.GetProperty("voidReason").GetString(), kept.GetProperty("isEditable").GetBoolean()));
    }

    [Fact]
    public async Task Every_balance_is_the_sum_of_the_entries_and_nothing_else()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var account = await brian.OpenAccountAsync("BCP", 1000m);
        await brian.EarnAsync(account.Id(), 4500m);
        await brian.SpendAsync(account.Id(), 54.90m);
        await brian.SpendAsync(account.Id(), 120m, pending: true);
        var voided = await brian.SpendAsync(account.Id(), 999m);
        await brian.Client.PostAsJsonAsync($"{Books.TransactionsUrl(brian.PersonalWorkspaceId)}/{voided.Id()}/void", new { });

        var balance = await brian.BalanceAsync(account.Id());

        var accountId = account.Id();
        var fromEntries = await api.ExecuteAsync(services => services.GetRequiredService<AppDbContext>().Entries.IgnoreQueryFilters().AsNoTracking()
            .Where(entry => entry.AccountId == accountId && entry.IsEffective)
            .SumAsync(entry => entry.SignedAmount));
        Assert.Equal(5325.10m, balance);
        Assert.Equal(fromEntries, balance);
        var columns = await api.ExecuteAsync(services => Task.FromResult(services.GetRequiredService<AppDbContext>().Model
            .FindEntityType(typeof(Modules.Ledger.Domain.Account))!.GetProperties().Select(property => property.Name).ToList()));
        Assert.DoesNotContain(columns, column => column.Contains("Balance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_opening_balance_and_an_adjustment_are_movements_of_the_account()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var url = Books.AccountsUrl(brian.PersonalWorkspaceId);
        var account = await brian.OpenAccountAsync("Efectivo", 200m, type: "cash");
        var empty = await brian.OpenAccountAsync("Yape", type: "wallet");

        var up = await brian.Client.PostWithKeyAsync($"{url}/{account.Id()}/adjust-balance", new { actualBalance = 180.50m, occurredOn = "2026-10-10" });
        var same = await brian.Client.PostWithKeyAsync($"{url}/{account.Id()}/adjust-balance", new { actualBalance = 180.50m });

        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        Assert.Equal(180.50m, (await up.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal(180.50m, await brian.BalanceAsync(account.Id()));
        Assert.Equal(0m, await brian.BalanceAsync(empty.Id()));

        var movements = (await brian.TransactionsAsync($"?accountId={account.Id()}")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["openingBalance", "adjustment"], movements.Select(movement => movement.GetProperty("kind").GetString()));
        Assert.Equal([200m, -19.50m], movements.Select(movement => movement.GetProperty("signedAmount").GetDecimal()));
        Assert.All(movements, movement => Assert.False(movement.GetProperty("isEditable").GetBoolean()));
        Assert.Empty((await brian.TransactionsAsync($"?accountId={empty.Id()}")).GetProperty("items").EnumerateArray());

        // Neither is income nor expense.
        Assert.Empty((await brian.TransactionsAsync()).GetProperty("totals").EnumerateArray());
        var edit = await brian.Client.PatchAsJsonAsync($"{Books.TransactionsUrl(brian.PersonalWorkspaceId)}/{movements[0].Id()}", await ExpenseBodyAsync(brian, account.Id(), 100m));
        Assert.Equal("ledger.transaction.not_editable", await Accounts.CodeOfAsync(edit));
    }

    [Fact]
    public async Task Editing_a_transaction_moves_the_money_and_the_audit_trail_keeps_both_versions()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var bank = await brian.OpenAccountAsync("BCP", 1000m);
        var cash = await brian.OpenAccountAsync("Efectivo", 100m, type: "cash");
        var expense = await brian.SpendAsync(bank.Id(), 54m, tags: ["mercado"]);
        var dining = await brian.CategoryIdAsync("Restaurantes y delivery/Delivery");

        var edited = await brian.Client.PatchAsJsonAsync($"{Books.TransactionsUrl(brian.PersonalWorkspaceId)}/{expense.Id()}", new
        {
            type = "expense", accountId = cash.Id(), amount = 60m, categoryId = dining, occurredOn = "2026-10-11", description = "Rappi", merchant = "Rappi",
            tags = new[] { "Salidas", "antojo" }
        });

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var transaction = await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("Rappi", "2026-10-11", "Efectivo", 60m), (
            transaction.GetProperty("description").GetString(), transaction.GetProperty("occurredOn").GetString(),
            transaction.GetProperty("accountName").GetString(), transaction.GetProperty("amount").GetDecimal()));
        Assert.Equal(("Delivery", "Restaurantes y delivery", "restaurant", "amber"), (
            transaction.GetProperty("category").GetProperty("name").GetString(), transaction.GetProperty("category").GetProperty("parentName").GetString(),
            transaction.GetProperty("category").GetProperty("icon").GetString(), transaction.GetProperty("category").GetProperty("color").GetString()));
        Assert.Equal(["antojo", "salidas"], transaction.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
        Assert.Equal(1000m, await brian.BalanceAsync(bank.Id()));
        Assert.Equal(40m, await brian.BalanceAsync(cash.Id()));

        var expenseId = expense.Id();
        var audit = await api.ExecuteAsync(services => services.GetRequiredService<AppDbContext>().AuditLog.AsNoTracking()
            .SingleAsync(entry => entry.Action == "ledger.transaction.edited" && entry.ResourceId == expenseId));
        var (before, after) = (JsonDocument.Parse(audit.Before!).RootElement, JsonDocument.Parse(audit.After!).RootElement);
        Assert.Equal((54m, bank.Id(), "Compra"), (before.GetProperty("amount").GetDecimal(), before.GetProperty("accountId").GetGuid(), before.GetProperty("description").GetString()));
        Assert.Equal((60m, cash.Id(), "Rappi"), (after.GetProperty("amount").GetDecimal(), after.GetProperty("accountId").GetGuid(), after.GetProperty("description").GetString()));
        Assert.Equal(brian.UserId, audit.OwnerUserId);
    }

    [Fact]
    public async Task A_pending_transaction_counts_from_the_start_and_is_confirmed_once()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var account = await brian.OpenAccountAsync("BCP", 500m);
        var pending = await brian.SpendAsync(account.Id(), 80m, pending: true);
        var url = $"{Books.TransactionsUrl(brian.PersonalWorkspaceId)}/{pending.Id()}/confirm";

        Assert.Equal("pending", pending.GetProperty("status").GetString());
        Assert.Equal(420m, await brian.BalanceAsync(account.Id()));
        Assert.Equal(["Compra"], (await brian.TransactionsAsync("?status=pending")).Descriptions());

        var confirmed = await brian.Client.PostAsync(url, null);
        var again = await brian.Client.PostAsync(url, null);

        Assert.Equal("confirmed", (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "ledger.transaction.not_pending"), (again.StatusCode, await Accounts.CodeOfAsync(again)));
        Assert.Equal(420m, await brian.BalanceAsync(account.Id()));
    }

    [Fact]
    public async Task Requests_that_break_a_rule_are_rejected_and_leave_the_balance_alone()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var account = await brian.OpenAccountAsync("BCP", 300m);
        var archived = await brian.OpenAccountAsync("Vieja", 50m);
        await brian.Client.PostAsync($"{Books.AccountsUrl(brian.PersonalWorkspaceId)}/{archived.Id()}/archive", null);
        var pets = await brian.CategoryIdAsync("Mascotas");
        await brian.Client.PostAsync($"/api/v1/workspaces/{brian.PersonalWorkspaceId}/categories/{pets}/archive", null);
        var url = Books.TransactionsUrl(brian.PersonalWorkspaceId);
        var body = await ExpenseBodyAsync(brian, account.Id(), 10m);

        async Task<(HttpStatusCode, string?)> PostAsync(object request)
        {
            var response = await brian.Client.PostWithKeyAsync(url, request);
            return (response.StatusCode, await Accounts.CodeOfAsync(response));
        }

        Assert.Equal((HttpStatusCode.BadRequest, "ledger.amount_invalid"), await PostAsync(body with { amount = 0m }));
        Assert.Equal((HttpStatusCode.BadRequest, "ledger.amount_invalid"), await PostAsync(body with { amount = -5m }));
        Assert.Equal((HttpStatusCode.BadRequest, "money.too_many_decimals"), await PostAsync(body with { amount = 1.005m }));
        Assert.Equal((HttpStatusCode.BadRequest, "validation.failed"), await PostAsync(body with { description = "" }));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "ledger.category_kind_mismatch"), await PostAsync(body with { type = "income" }));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "ledger.category_archived"), await PostAsync(body with { categoryId = pets }));
        Assert.Equal((HttpStatusCode.NotFound, "ledger.category_not_found"), await PostAsync(body with { categoryId = Guid.NewGuid() }));
        Assert.Equal((HttpStatusCode.NotFound, "ledger.account.not_found"), await PostAsync(body with { accountId = Guid.NewGuid() }));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "ledger.account.archived"), await PostAsync(body with { accountId = archived.Id() }));
        Assert.Equal((HttpStatusCode.BadRequest, "categories.tag.name_invalid"), await PostAsync(body with { tags = ["a,b"] }));
        Assert.Equal((HttpStatusCode.BadRequest, "validation.failed"), await PostAsync(body with { tags = Enumerable.Range(0, 11).Select(index => $"t{index}").ToArray() }));

        var withoutKey = await brian.Client.PostAsJsonAsync(url, body);
        Assert.Equal("idempotency.key_required", await Accounts.CodeOfAsync(withoutKey));
        var key = Guid.NewGuid().ToString();
        await brian.Client.PostWithKeyAsync(url, body, key);
        var reused = await brian.Client.PostWithKeyAsync(url, body with { amount = 11m }, key);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "idempotency.key_reused"), (reused.StatusCode, await Accounts.CodeOfAsync(reused)));

        Assert.Equal(290m, await brian.BalanceAsync(account.Id()));
        Assert.Equal(50m, await brian.BalanceAsync(archived.Id()));
        Assert.DoesNotContain(await TagNamesAsync(brian), name => name is "t0");
    }

    [Fact]
    public async Task The_list_filters_by_type_account_category_tag_text_and_dates_and_totals_each_currency_apart()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var soles = await brian.OpenAccountAsync("BCP", 1000m);
        var dollars = await brian.OpenAccountAsync("BCP Dólares", 500m, currency: "USD");
        await brian.EarnAsync(soles.Id(), 4500m, description: "Sueldo octubre", occurredOn: "2026-10-01");
        await brian.SpendAsync(soles.Id(), 54m, description: "Wong", occurredOn: "2026-10-05", tags: ["mercado"], merchant: "Wong Benavides");
        await brian.SpendAsync(soles.Id(), 1200m, "Vivienda/Alquiler", "Alquiler", "2026-10-02", tags: ["fijo"]);
        await brian.SpendAsync(soles.Id(), 80m, "Vivienda", "Gasfitero", "2026-09-28", note: "Fuga en la cocina");
        await brian.SpendAsync(dollars.Id(), 20m, "Suscripciones y ocio/Streaming", "Netflix", "2026-10-03", tags: ["fijo"]);
        var housing = await brian.CategoryIdAsync("Vivienda");

        async Task<List<string>> ListAsync(string query) => (await brian.TransactionsAsync(query)).Descriptions();

        Assert.Equal(["Wong", "Netflix", "Alquiler", "Sueldo octubre", "Gasfitero"], await ListAsync("?to=2026-10-05"));
        Assert.Equal(["Sueldo octubre"], await ListAsync("?type=income"));
        Assert.Equal(["Wong", "Netflix", "Alquiler", "Gasfitero"], await ListAsync("?type=expense"));
        Assert.Equal(["Netflix"], await ListAsync($"?accountId={dollars.Id()}&to=2026-10-05"));
        Assert.Equal(["Alquiler", "Gasfitero"], await ListAsync($"?categoryId={housing}"));
        Assert.Equal(["Netflix", "Alquiler"], await ListAsync("?tag=Fijo"));
        Assert.Empty(await ListAsync("?tag=no-existe"));
        Assert.Equal(["Wong"], await ListAsync("?q=benavides"));
        Assert.Equal(["Gasfitero"], await ListAsync("?q=FUGA"));
        Assert.Equal(["Netflix", "Alquiler"], await ListAsync("?from=2026-10-02&to=2026-10-03"));

        var october = (await brian.TransactionsAsync("?from=2026-10-01&to=2026-10-31")).GetProperty("totals").EnumerateArray().ToList();
        Assert.Equal(["PEN", "USD"], october.Select(total => total.GetProperty("currency").GetString()));
        Assert.Equal((4500m, 1254m, 3246m, 3), (
            october[0].GetProperty("income").GetDecimal(), october[0].GetProperty("expense").GetDecimal(), october[0].GetProperty("net").GetDecimal(), october[0].GetProperty("count").GetInt32()));
        Assert.Equal((0m, 20m, -20m), (october[1].GetProperty("income").GetDecimal(), october[1].GetProperty("expense").GetDecimal(), october[1].GetProperty("net").GetDecimal()));
    }

    [Fact]
    public async Task The_list_is_read_by_pages_without_repeating_or_skipping_anything()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var account = await brian.OpenAccountAsync("BCP");
        foreach (var (description, day) in new[] { ("A", "2026-10-03"), ("B", "2026-10-03"), ("C", "2026-10-02"), ("D", "2026-10-03"), ("E", "2026-10-01") })
        {
            await brian.SpendAsync(account.Id(), 10m, description: description, occurredOn: day);
        }

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await brian.TransactionsAsync($"?limit=2{(cursor is null ? "" : $"&cursor={cursor}")}");
            seen.AddRange(page.Descriptions());
            cursor = page.GetProperty("nextCursor").GetString();
            Assert.Equal(50m, page.GetProperty("totals")[0].GetProperty("expense").GetDecimal());
            pages++;
        }
        while (cursor is not null);

        // Newest day first; within a day, the last one registered first.
        Assert.Equal(["D", "B", "A", "C", "E"], seen);
        Assert.Equal(3, pages);
        var broken = await brian.Client.GetAsync($"{Books.TransactionsUrl(brian.PersonalWorkspaceId)}?cursor=no-es-un-cursor");
        Assert.Equal((HttpStatusCode.BadRequest, "ledger.cursor_invalid"), (broken.StatusCode, await Accounts.CodeOfAsync(broken)));
    }

    [Fact]
    public async Task Account_names_are_unique_among_the_active_ones_and_an_archived_account_keeps_its_money()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var url = Books.AccountsUrl(brian.PersonalWorkspaceId);
        var account = await brian.OpenAccountAsync("BCP Sueldo", 300m);

        var duplicate = await brian.Client.PostWithKeyAsync(url, new { name = " bcp  sueldo ", type = "bank", currency = "PEN" });
        var unsupported = await brian.Client.PostWithKeyAsync(url, new { name = "Yenes", type = "bank", currency = "JPY" });
        var withoutKey = await brian.Client.PostAsJsonAsync(url, new { name = "Otra", type = "bank", currency = "PEN" });
        var renamed = await brian.Client.PatchAsJsonAsync($"{url}/{account.Id()}", new { name = "BCP Principal", institution = "BCP" });

        Assert.Equal((HttpStatusCode.Conflict, "ledger.account.name_taken"), (duplicate.StatusCode, await Accounts.CodeOfAsync(duplicate)));
        Assert.Equal((HttpStatusCode.BadRequest, "currency.unsupported"), (unsupported.StatusCode, await Accounts.CodeOfAsync(unsupported)));
        Assert.Equal("idempotency.key_required", await Accounts.CodeOfAsync(withoutKey));
        Assert.Equal(("BCP Principal", "BCP", 300m), await ReadAsync(renamed));

        await brian.Client.PostAsync($"{url}/{account.Id()}/archive", null);
        var listed = await brian.Client.GetFromJsonAsync<JsonElement>(url);
        var withArchived = await brian.Client.GetFromJsonAsync<JsonElement>($"{url}?includeArchived=true");
        Assert.Empty(listed.EnumerateArray());
        Assert.Equal((300m, true), (withArchived[0].GetProperty("balance").GetDecimal(), withArchived[0].GetProperty("isArchived").GetBoolean()));
        Assert.Equal(["Saldo inicial"], (await brian.TransactionsAsync()).Descriptions());

        // Its name is free again; restoring the old one then conflicts.
        await brian.OpenAccountAsync("BCP Principal");
        var restore = await brian.Client.PostAsync($"{url}/{account.Id()}/restore", null);
        Assert.Equal((HttpStatusCode.Conflict, "ledger.account.name_taken"), (restore.StatusCode, await Accounts.CodeOfAsync(restore)));

        static async Task<(string?, string?, decimal)> ReadAsync(HttpResponseMessage response)
        {
            var account = await response.Content.ReadFromJsonAsync<JsonElement>();
            return (account.GetProperty("name").GetString(), account.GetProperty("institution").GetString(), account.GetProperty("balance").GetDecimal());
        }
    }

    [Fact]
    public async Task Two_members_of_a_workspace_keep_separate_books_and_cannot_reach_each_others()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;
        var juanEmail = Accounts.NewEmail("juan");
        var juan = await api.RegisterAsync("Juan", invitationToken: await api.InviteAsync(brian, workspaceId, juanEmail), email: juanEmail);
        var briansAccount = await brian.OpenAccountAsync("BCP", 1000m);
        var briansExpense = await brian.SpendAsync(briansAccount.Id(), 54m, description: "Terapia", tags: ["privado"]);
        var juansAccount = await juan.OpenAccountAsync("BCP", 20m, workspaceId: workspaceId);
        var (accounts, transactions) = (Books.AccountsUrl(workspaceId), Books.TransactionsUrl(workspaceId));

        // Juan sees only his own, even though the account has the same name.
        var juansAccounts = (await juan.Client.GetFromJsonAsync<JsonElement>($"{accounts}?includeArchived=true")).EnumerateArray().ToList();
        Assert.Equal([juansAccount.Id()], juansAccounts.Select(account => account.Id()));
        Assert.Equal(["Saldo inicial"], (await juan.TransactionsAsync(workspaceId: workspaceId)).Descriptions());
        Assert.Empty((await juan.TransactionsAsync($"?accountId={briansAccount.Id()}", workspaceId)).GetProperty("items").EnumerateArray());
        Assert.Empty((await juan.TransactionsAsync("?tag=privado&status=confirmed", workspaceId)).GetProperty("items").EnumerateArray());
        Assert.Empty((await juan.TransactionsAsync("?q=terapia", workspaceId)).GetProperty("items").EnumerateArray());

        var juansBody = await ExpenseBodyAsync(juan, juansAccount.Id(), 5m, workspaceId);
        var responses = new (string Code, HttpResponseMessage Response)[]
        {
            ("ledger.account.not_found", await juan.Client.GetAsync($"{accounts}/{briansAccount.Id()}")),
            ("ledger.account.not_found", await juan.Client.PatchAsJsonAsync($"{accounts}/{briansAccount.Id()}", new { name = "De Juan" })),
            ("ledger.account.not_found", await juan.Client.PostAsync($"{accounts}/{briansAccount.Id()}/archive", null)),
            ("ledger.account.not_found", await juan.Client.PostWithKeyAsync($"{accounts}/{briansAccount.Id()}/adjust-balance", new { actualBalance = 0m })),
            ("ledger.account.not_found", await juan.Client.PostWithKeyAsync(transactions, juansBody with { accountId = briansAccount.Id() })),
            ("ledger.category_not_found", await juan.Client.PostWithKeyAsync(transactions, juansBody with { categoryId = await brian.CategoryIdAsync("Supermercado") })),
            ("ledger.transaction.not_found", await juan.Client.GetAsync($"{transactions}/{briansExpense.Id()}")),
            ("ledger.transaction.not_found", await juan.Client.PatchAsJsonAsync($"{transactions}/{briansExpense.Id()}", juansBody)),
            ("ledger.transaction.not_found", await juan.Client.PostAsync($"{transactions}/{briansExpense.Id()}/confirm", null)),
            ("ledger.transaction.not_found", await juan.Client.PostAsJsonAsync($"{transactions}/{briansExpense.Id()}/void", new { reason = "Espía" }))
        };
        foreach (var (code, response) in responses)
        {
            Assert.Equal((HttpStatusCode.NotFound, code), (response.StatusCode, await Accounts.CodeOfAsync(response)));
        }

        // Being the owner of the workspace gives Brian nothing over Juan's books either.
        var ownerPeeks = await brian.Client.GetAsync($"{accounts}/{juansAccount.Id()}");
        Assert.Equal(HttpStatusCode.NotFound, ownerPeeks.StatusCode);

        Assert.Equal(946m, await brian.BalanceAsync(briansAccount.Id()));
        Assert.Equal(20m, await juan.BalanceAsync(juansAccount.Id(), workspaceId));
        var untouched = await brian.Client.GetFromJsonAsync<JsonElement>($"{transactions}/{briansExpense.Id()}");
        Assert.Equal(("Terapia", "confirmed", "BCP"), (untouched.GetProperty("description").GetString(), untouched.GetProperty("status").GetString(), untouched.GetProperty("accountName").GetString()));
    }

    [Fact]
    public async Task Books_stay_in_the_workspace_they_were_made_in_and_outsiders_get_nothing()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var pedro = await api.RegisterAsync("Pedro");
        var account = await brian.OpenAccountAsync("BCP", 100m);
        var second = await brian.Client.PostAsJsonAsync("/api/v1/workspaces", new { name = "Negocio" });
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var inOther = await brian.Client.GetFromJsonAsync<JsonElement>(Books.AccountsUrl(secondId));
        var fromOther = await brian.Client.GetAsync($"{Books.AccountsUrl(secondId)}/{account.Id()}");
        var outsider = await pedro.Client.GetAsync(Books.AccountsUrl(brian.PersonalWorkspaceId));
        var outsiderTransactions = await pedro.Client.GetAsync(Books.TransactionsUrl(brian.PersonalWorkspaceId));
        var anonymous = await api.AnonymousClient().GetAsync(Books.TransactionsUrl(brian.PersonalWorkspaceId));

        Assert.Empty(inOther.EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, fromOther.StatusCode);
        Assert.Equal((HttpStatusCode.NotFound, "workspace.not_found"), (outsider.StatusCode, await Accounts.CodeOfAsync(outsider)));
        Assert.Equal(HttpStatusCode.NotFound, outsiderTransactions.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private sealed record TransactionBody(string type, Guid accountId, decimal amount, Guid categoryId, string occurredOn, string description, string[]? tags = null);

    private static async Task<TransactionBody> ExpenseBodyAsync(TestAccount person, Guid accountId, decimal amount, Guid? workspaceId = null) =>
        new("expense", accountId, amount, await person.CategoryIdAsync("Supermercado", workspaceId), "2026-10-12", "Compra");

    private static async Task<List<string>> TagNamesAsync(TestAccount person) =>
        (await person.Client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{person.PersonalWorkspaceId}/tags"))
            .EnumerateArray().Select(tag => tag.GetProperty("name").GetString()!).ToList();
}
