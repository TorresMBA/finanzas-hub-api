using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Infrastructure.Persistence;
using PersonalFinance.IntegrationTests.Support;

namespace PersonalFinance.IntegrationTests;

/// <summary>Transfers, credit cards, exchange rates, net worth and the dashboard: money that moves without being earned or spent.</summary>
[Collection(ApiCollection.Name)]
public class LedgerTransfersTests(ApiFixture api)
{
    [Fact]
    public async Task Transferring_500_takes_them_from_one_account_gives_them_to_the_other_and_leaves_net_worth_alone()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var bcp = await brian.OpenAccountAsync("BCP", 1000m);
        var interbank = await brian.OpenAccountAsync("Interbank", 200m);
        Assert.Equal(1200m, (await brian.SummaryAsync()).Money("netWorth"));

        var key = Guid.NewGuid().ToString();
        var first = await brian.PostTransferAsync(bcp.Id(), interbank.Id(), 500m, key: key);
        var replay = await brian.PostTransferAsync(bcp.Id(), interbank.Id(), 500m, key: key);

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (first.StatusCode, replay.StatusCode));
        var transfer = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(transfer.Id(), (await replay.Content.ReadFromJsonAsync<JsonElement>()).Id());
        Assert.Equal((500m, 700m), (await brian.BalanceAsync(bcp.Id()), await brian.BalanceAsync(interbank.Id())));
        Assert.Equal(1200m, (await brian.SummaryAsync()).Money("netWorth"));

        // One row, neither income nor expense, told from the account the list is filtered by.
        var listed = await brian.TransactionsAsync("?type=transfer");
        var row = Assert.Single(listed.GetProperty("items").EnumerateArray());
        Assert.Equal(("transfer", "Transferencia", false, -500m, "BCP"), (
            row.GetProperty("kind").GetString(), row.GetProperty("description").GetString(), row.GetProperty("isEditable").GetBoolean(),
            row.Money("signedAmount"), row.GetProperty("accountName").GetString()));
        var sides = row.GetProperty("transfer");
        Assert.Equal(("BCP", "Interbank", 500m, 500m, false), (
            sides.GetProperty("fromAccountName").GetString(), sides.GetProperty("toAccountName").GetString(), sides.Money("amountOut"), sides.Money("amountIn"),
            sides.GetProperty("isCardPayment").GetBoolean()));
        Assert.Empty(listed.GetProperty("totals").EnumerateArray());
        Assert.Equal((0m, 0m), (listed.GetProperty("baseTotals").Money("income"), listed.GetProperty("baseTotals").Money("expense")));
        var fromDestination = (await brian.TransactionsAsync($"?accountId={interbank.Id()}&type=transfer")).GetProperty("items")[0];
        Assert.Equal((500m, "Interbank"), (fromDestination.Money("signedAmount"), fromDestination.GetProperty("accountName").GetString()));

        var edit = await brian.Client.PatchAsJsonAsync($"{Books.TransactionsUrl(brian.PersonalWorkspaceId)}/{transfer.Id()}", new
        {
            type = "expense", accountId = bcp.Id(), amount = 500m, categoryId = await brian.CategoryIdAsync("Supermercado"), occurredOn = "2026-10-12", description = "Otra"
        });
        Assert.Equal("ledger.transaction.not_editable", await Accounts.CodeOfAsync(edit));

        // Voiding takes both sides out at once.
        var voided = await brian.Client.PostAsJsonAsync($"{Books.TransactionsUrl(brian.PersonalWorkspaceId)}/{transfer.Id()}/void", new { reason = "Era otra cuenta" });
        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
        Assert.Equal((1000m, 200m), (await brian.BalanceAsync(bcp.Id()), await brian.BalanceAsync(interbank.Id())));
        var transferId = transfer.Id();
        var entries = await api.ExecuteAsync(services => services.GetRequiredService<AppDbContext>().Entries.IgnoreQueryFilters().AsNoTracking()
            .Where(entry => entry.OperationId == transferId).ToListAsync());
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.False(entry.IsEffective));
    }

    [Fact]
    public async Task A_transfer_between_two_currencies_states_both_amounts_and_bad_transfers_move_nothing()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var soles = await brian.OpenAccountAsync("BCP", 1000m);
        var other = await brian.OpenAccountAsync("Interbank");
        var dollars = await brian.OpenAccountAsync("BCP Dólares", currency: "USD");
        var archived = await brian.OpenAccountAsync("Vieja");
        await brian.Client.PostAsync($"{Books.AccountsUrl(brian.PersonalWorkspaceId)}/{archived.Id()}/archive", null);

        async Task<(HttpStatusCode, string?)> TryAsync(Guid from, Guid to, decimal amount, decimal? destinationAmount = null)
        {
            var response = await brian.PostTransferAsync(from, to, amount, destinationAmount);
            return (response.StatusCode, await Accounts.CodeOfAsync(response));
        }

        Assert.Equal((HttpStatusCode.BadRequest, "ledger.transfer.destination_amount_required"), await TryAsync(soles.Id(), dollars.Id(), 375m));
        Assert.Equal((HttpStatusCode.BadRequest, "ledger.transfer.amount_mismatch"), await TryAsync(soles.Id(), other.Id(), 100m, 90m));
        Assert.Equal((HttpStatusCode.BadRequest, "ledger.transfer.same_account"), await TryAsync(soles.Id(), soles.Id(), 100m));
        Assert.Equal((HttpStatusCode.BadRequest, "ledger.amount_invalid"), await TryAsync(soles.Id(), other.Id(), 0m));
        Assert.Equal((HttpStatusCode.BadRequest, "money.too_many_decimals"), await TryAsync(soles.Id(), dollars.Id(), 375m, 100.001m));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "ledger.account.archived"), await TryAsync(soles.Id(), archived.Id(), 100m));
        Assert.Equal((HttpStatusCode.NotFound, "ledger.account.not_found"), await TryAsync(soles.Id(), Guid.NewGuid(), 100m));
        var withoutKey = await brian.Client.PostAsJsonAsync(
            Books.TransfersUrl(brian.PersonalWorkspaceId), new { fromAccountId = soles.Id(), toAccountId = other.Id(), amount = 10m, occurredOn = "2026-10-12" });
        Assert.Equal("idempotency.key_required", await Accounts.CodeOfAsync(withoutKey));
        Assert.Equal((1000m, 0m, 0m), (await brian.BalanceAsync(soles.Id()), await brian.BalanceAsync(other.Id()), await brian.BalanceAsync(dollars.Id())));

        var transfer = await brian.TransferAsync(soles.Id(), dollars.Id(), 375m, 100m);

        var sides = transfer.GetProperty("transfer");
        Assert.Equal((375m, "PEN", 100m, "USD"), (
            sides.Money("amountOut"), sides.GetProperty("currencyOut").GetString(), sides.Money("amountIn"), sides.GetProperty("currencyIn").GetString()));
        Assert.Equal((625m, 100m), (await brian.BalanceAsync(soles.Id()), await brian.BalanceAsync(dollars.Id())));
    }

    [Fact]
    public async Task A_purchase_on_a_credit_card_reduces_what_is_available_and_a_payment_gives_it_back()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var bank = await brian.OpenAccountAsync("BCP", 1000m);
        var card = await brian.OpenCardAsync("Visa", 5000m, paymentDay: 5);
        var cardUrl = $"{Books.AccountsUrl(brian.PersonalWorkspaceId)}/{card.Id()}";

        Assert.Equal(("creditCard", 0m), (card.GetProperty("type").GetString(), card.Money("balance")));
        Assert.Equal((5000m, 0m, 5000m, 5), (
            card.GetProperty("creditCard").Money("creditLimit"), card.GetProperty("creditCard").Money("used"), card.GetProperty("creditCard").Money("available"),
            card.GetProperty("creditCard").GetProperty("paymentDay").GetInt32()));
        var nextPayment = DateOnly.Parse(card.GetProperty("creditCard").GetProperty("nextPaymentDate").GetString()!);
        Assert.Equal(5, nextPayment.Day);
        Assert.InRange(nextPayment.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber, -1, 32);
        Assert.Equal(JsonValueKind.Null, bank.GetProperty("creditCard").ValueKind);

        await brian.SpendAsync(card.Id(), 200m, description: "Wong");

        var afterPurchase = await brian.AccountAsync(card.Id());
        Assert.Equal((-200m, 200m, 4800m), (afterPurchase.Money("balance"), afterPurchase.GetProperty("creditCard").Money("used"), afterPurchase.GetProperty("creditCard").Money("available")));
        var owing = await brian.SummaryAsync();
        Assert.Equal((1000m, 200m, 800m), (owing.Money("assets"), owing.Money("debts"), owing.Money("netWorth")));

        var paid = await brian.PayCardAsync(card.Id(), bank.Id(), 200m);

        Assert.Equal(HttpStatusCode.Created, paid.StatusCode);
        var payment = await paid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("transfer", "Pago de tarjeta", true, "BCP", "Visa"), (
            payment.GetProperty("kind").GetString(), payment.GetProperty("description").GetString(), payment.GetProperty("transfer").GetProperty("isCardPayment").GetBoolean(),
            payment.GetProperty("transfer").GetProperty("fromAccountName").GetString(), payment.GetProperty("transfer").GetProperty("toAccountName").GetString()));
        var afterPayment = await brian.AccountAsync(card.Id());
        Assert.Equal((0m, 0m, 5000m), (afterPayment.Money("balance"), afterPayment.GetProperty("creditCard").Money("used"), afterPayment.GetProperty("creditCard").Money("available")));
        Assert.Equal(800m, await brian.BalanceAsync(bank.Id()));

        // Paying a debt is not spending: the purchase was the expense, and net worth stays where it was.
        Assert.Equal(800m, (await brian.SummaryAsync()).Money("netWorth"));
        var totals = Assert.Single((await brian.TransactionsAsync()).GetProperty("totals").EnumerateArray());
        Assert.Equal((0m, 200m), (totals.Money("income"), totals.Money("expense")));

        // A card that already owes something starts with that debt, and its line can change.
        var used = await brian.OpenCardAsync("Mastercard", 3000m, debt: 450m);
        Assert.Equal((-450m, 450m, 2550m), (used.Money("balance"), used.GetProperty("creditCard").Money("used"), used.GetProperty("creditCard").Money("available")));
        var raised = await brian.Client.PatchAsJsonAsync(cardUrl, new { name = "Visa Oro", creditCard = new { creditLimit = 8000m, paymentDay = 10, closingDay = 25 } });
        var renamed = await brian.Client.PatchAsJsonAsync(cardUrl, new { name = "Visa Platinum" });
        Assert.Equal((8000m, 10, 25), await TermsAsync(raised));
        Assert.Equal((8000m, 10, 25), await TermsAsync(renamed));

        static async Task<(decimal, int, int)> TermsAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var terms = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("creditCard");
            return (terms.Money("creditLimit"), terms.GetProperty("paymentDay").GetInt32(), terms.GetProperty("closingDay").GetInt32());
        }
    }

    [Fact]
    public async Task Cards_and_their_payments_follow_their_rules()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var url = Books.AccountsUrl(brian.PersonalWorkspaceId);
        var bank = await brian.OpenAccountAsync("BCP", 1000m);
        var dollars = await brian.OpenAccountAsync("BCP Dólares", 300m, currency: "USD");
        var visa = await brian.OpenCardAsync("Visa", 5000m, debt: 400m);
        var mastercard = await brian.OpenCardAsync("Mastercard", 3000m);

        async Task<(HttpStatusCode, string?)> OpenAsync(object body)
        {
            var response = await brian.Client.PostWithKeyAsync(url, body);
            return (response.StatusCode, await Accounts.CodeOfAsync(response));
        }

        async Task<(HttpStatusCode, string?)> PayAsync(Guid cardId, Guid fromAccountId, decimal amount, decimal? sourceAmount = null)
        {
            var response = await brian.PayCardAsync(cardId, fromAccountId, amount, sourceAmount);
            return (response.StatusCode, await Accounts.CodeOfAsync(response));
        }

        Assert.Equal((HttpStatusCode.BadRequest, "ledger.card.terms_required"), await OpenAsync(new { name = "Sin línea", type = "creditCard", currency = "PEN" }));
        Assert.Equal((HttpStatusCode.BadRequest, "ledger.card.not_a_card"),
            await OpenAsync(new { name = "Banco con línea", type = "bank", currency = "PEN", creditCard = new { creditLimit = 100m, paymentDay = 5 } }));
        Assert.Equal((HttpStatusCode.BadRequest, "ledger.card.limit_invalid"),
            await OpenAsync(new { name = "Línea cero", type = "creditCard", currency = "PEN", creditCard = new { creditLimit = 0m, paymentDay = 5 } }));
        Assert.Equal((HttpStatusCode.BadRequest, "validation.failed"),
            await OpenAsync(new { name = "Día 40", type = "creditCard", currency = "PEN", creditCard = new { creditLimit = 100m, paymentDay = 40 } }));

        Assert.Equal((HttpStatusCode.UnprocessableEntity, "ledger.card.not_a_card"), await PayAsync(bank.Id(), dollars.Id(), 10m, 3m));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "ledger.card.paid_from_card"), await PayAsync(visa.Id(), mastercard.Id(), 10m));
        Assert.Equal((HttpStatusCode.BadRequest, "ledger.card.source_amount_required"), await PayAsync(visa.Id(), dollars.Id(), 100m));
        Assert.Equal((HttpStatusCode.NotFound, "ledger.account.not_found"), await PayAsync(Guid.NewGuid(), bank.Id(), 10m));
        Assert.Equal((HttpStatusCode.NotFound, "ledger.account.not_found"), await PayAsync(visa.Id(), Guid.NewGuid(), 10m));
        Assert.Equal((-400m, 1000m, 300m), (await brian.BalanceAsync(visa.Id()), await brian.BalanceAsync(bank.Id()), await brian.BalanceAsync(dollars.Id())));

        // Paid from an account in another currency: each side in its own.
        Assert.Equal(HttpStatusCode.Created, (await brian.PayCardAsync(visa.Id(), dollars.Id(), 375m, 100m)).StatusCode);
        Assert.Equal((-25m, 200m), (await brian.BalanceAsync(visa.Id()), await brian.BalanceAsync(dollars.Id())));

        // Paying more than is owed leaves the card in favour, and nothing "used".
        Assert.Equal(HttpStatusCode.Created, (await brian.PayCardAsync(visa.Id(), bank.Id(), 100m)).StatusCode);
        var inFavour = await brian.AccountAsync(visa.Id());
        Assert.Equal((75m, 0m, 5075m), (inFavour.Money("balance"), inFavour.GetProperty("creditCard").Money("used"), inFavour.GetProperty("creditCard").Money("available")));
    }

    [Fact]
    public async Task Net_worth_counts_the_accounts_in_use_that_the_person_has_not_taken_out()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var url = Books.AccountsUrl(brian.PersonalWorkspaceId);
        var bank = await brian.OpenAccountAsync("BCP", 1000m);
        var cash = await brian.OpenAccountAsync("Efectivo", 150m, type: "cash");
        var opened = await brian.Client.PostWithKeyAsync(url, new { name = "Fondo viaje", type = "bank", currency = "PEN", openingBalance = 400m, includeInNetWorth = false });
        var fund = await opened.Content.ReadFromJsonAsync<JsonElement>();
        await brian.OpenCardAsync("Visa", 5000m, debt: 250m);

        Assert.False(fund.GetProperty("includeInNetWorth").GetBoolean());
        Assert.True(bank.GetProperty("includeInNetWorth").GetBoolean());
        var summary = await brian.SummaryAsync();
        Assert.Equal(("PEN", 1150m, 250m, 900m), (summary.GetProperty("baseCurrency").GetString(), summary.Money("assets"), summary.Money("debts"), summary.Money("netWorth")));
        Assert.Empty(summary.GetProperty("missingRates").EnumerateArray());

        // Moving money to an account that does not count lowers what counts; the money is still in the account.
        await brian.TransferAsync(bank.Id(), fund.Id(), 100m);
        Assert.Equal(800m, (await brian.SummaryAsync()).Money("netWorth"));
        Assert.Equal(500m, await brian.BalanceAsync(fund.Id()));

        // The person puts it back in; renaming without saying anything keeps the choice.
        var included = await brian.Client.PatchAsJsonAsync($"{url}/{fund.Id()}", new { name = "Fondo viaje", includeInNetWorth = true });
        var renamed = await brian.Client.PatchAsJsonAsync($"{url}/{fund.Id()}", new { name = "Fondo Japón" });
        Assert.True((await included.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("includeInNetWorth").GetBoolean());
        Assert.True((await renamed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("includeInNetWorth").GetBoolean());
        Assert.Equal(1300m, (await brian.SummaryAsync()).Money("netWorth"));

        // An archived account never counts, with or without money in it.
        await brian.Client.PostAsync($"{url}/{cash.Id()}/archive", null);
        var afterArchive = await brian.SummaryAsync();
        Assert.Equal((1400m, 250m, 1150m), (afterArchive.Money("assets"), afterArchive.Money("debts"), afterArchive.Money("netWorth")));
        await brian.Client.PostAsync($"{url}/{cash.Id()}/restore", null);
        Assert.Equal(1300m, (await brian.SummaryAsync()).Money("netWorth"));
    }

    [Fact]
    public async Task Net_worth_brings_every_currency_to_the_base_one_and_says_which_it_could_not_convert()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        await brian.OpenAccountAsync("BCP", 625m);
        await brian.OpenAccountAsync("BCP Dólares", 100m, currency: "USD");
        await brian.OpenCardAsync("Visa USD", 2000m, debt: 20m, currency: "USD");

        var without = await brian.SummaryAsync();
        Assert.Equal((625m, 0m, 625m), (without.Money("assets"), without.Money("debts"), without.Money("netWorth")));
        Assert.Equal(["USD"], without.GetProperty("missingRates").EnumerateArray().Select(currency => currency.GetString()));
        var positions = without.GetProperty("currencies").EnumerateArray().ToList();
        Assert.Equal(["PEN", "USD"], positions.Select(position => position.GetProperty("currency").GetString()));
        Assert.Equal((100m, 20m, 80m, JsonValueKind.Null), (positions[1].Money("assets"), positions[1].Money("debts"), positions[1].Money("net"), positions[1].GetProperty("rate").ValueKind));

        await brian.SetRateAsync("USD", 3.75m);
        var with = await brian.SummaryAsync();
        Assert.Equal((1000m, 75m, 925m), (with.Money("assets"), with.Money("debts"), with.Money("netWorth")));
        Assert.Empty(with.GetProperty("missingRates").EnumerateArray());
        Assert.Equal((3.75m, "2026-01-01"), (with.GetProperty("currencies")[1].Money("rate"), with.GetProperty("currencies")[1].GetProperty("rateDate").GetString()));

        // Balances are valued at the latest rate.
        await brian.SetRateAsync("USD", 4m, "2026-06-01");
        Assert.Equal(945m, (await brian.SummaryAsync()).Money("netWorth"));
    }

    [Fact]
    public async Task An_amount_in_another_currency_keeps_what_it_was_worth_when_it_was_registered()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var dollars = await brian.OpenAccountAsync("BCP Dólares", currency: "USD");
        var soles = await brian.OpenAccountAsync("BCP");
        await brian.SetRateAsync("USD", 3.50m, "2026-01-01");
        var first = await brian.SpendAsync(dollars.Id(), 100m, description: "Hotel", occurredOn: "2026-10-05");
        await brian.SpendAsync(soles.Id(), 50m, description: "Taxi", occurredOn: "2026-10-05");

        // The rate goes up later: what was already registered does not move.
        await brian.SetRateAsync("USD", 4m, "2026-10-10");
        await brian.SpendAsync(dollars.Id(), 10m, description: "Café", occurredOn: "2026-10-11");
        await brian.EarnAsync(dollars.Id(), 200m, description: "Freelance", occurredOn: "2026-10-12");

        var october = await brian.TransactionsAsync("?from=2026-10-01&to=2026-10-31");
        var inBase = october.GetProperty("baseTotals");
        Assert.Equal(("PEN", 800m, 440m, 360m), (inBase.GetProperty("currency").GetString(), inBase.Money("income"), inBase.Money("expense"), inBase.Money("net")));
        Assert.Equal(["PEN", "USD"], october.GetProperty("totals").EnumerateArray().Select(total => total.GetProperty("currency").GetString()));
        Assert.Equal(110m, october.GetProperty("totals")[1].Money("expense"));

        var firstId = first.Id();
        var entry = await api.ExecuteAsync(services => services.GetRequiredService<AppDbContext>().Entries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(entry => entry.OperationId == firstId));
        Assert.Equal((350m, 3.5m, new DateOnly(2026, 1, 1)), (entry.BaseAmount, entry.FxRate, entry.FxRateDate));

        // Something dated before the first rate has no value of its own: it counts at the latest rate.
        await brian.SpendAsync(dollars.Id(), 1m, description: "Antiguo", occurredOn: "2025-12-31");
        Assert.Equal(4m, (await brian.TransactionsAsync("?to=2025-12-31")).GetProperty("baseTotals").Money("expense"));

        // Moving a transaction to another day values it again at the rate of that day.
        var edited = await brian.Client.PatchAsJsonAsync($"{Books.TransactionsUrl(brian.PersonalWorkspaceId)}/{firstId}", new
        {
            type = "expense", accountId = dollars.Id(), amount = 100m, categoryId = await brian.CategoryIdAsync("Supermercado"), occurredOn = "2026-10-12", description = "Hotel"
        });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(490m, (await brian.TransactionsAsync("?from=2026-10-01&to=2026-10-31")).GetProperty("baseTotals").Money("expense"));
    }

    [Fact]
    public async Task Exchange_rates_are_kept_by_each_person_and_follow_their_rules()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;
        var juanEmail = Accounts.NewEmail("juan");
        var juan = await api.RegisterAsync("Juan", invitationToken: await api.InviteAsync(brian, workspaceId, juanEmail), email: juanEmail);
        var url = $"/api/v1/workspaces/{workspaceId}/exchange-rates";

        async Task<(HttpStatusCode, string?)> SetAsync(string currency, object body)
        {
            var response = await brian.Client.PutAsJsonAsync($"{url}/{currency}", body);
            return (response.StatusCode, await Accounts.CodeOfAsync(response));
        }

        Assert.Equal((HttpStatusCode.BadRequest, "ledger.exchange_rate.same_currency"), await SetAsync("PEN", new { rate = 1m }));
        Assert.Equal((HttpStatusCode.BadRequest, "currency.unsupported"), await SetAsync("JPY", new { rate = 0.025m }));
        Assert.Equal((HttpStatusCode.BadRequest, "ledger.exchange_rate.rate_invalid"), await SetAsync("USD", new { rate = 0m }));
        Assert.Equal((HttpStatusCode.BadRequest, "ledger.exchange_rate.rate_invalid"), await SetAsync("USD", new { rate = 3.1234567m }));
        Assert.Equal((HttpStatusCode.BadRequest, "ledger.exchange_rate.date_in_future"), await SetAsync("USD", new { rate = 3.7m, asOf = "2999-01-01" }));
        Assert.Equal((HttpStatusCode.BadRequest, "validation.failed"), await SetAsync("USD", new { }));
        Assert.Empty((await brian.Client.GetFromJsonAsync<JsonElement>(url)).EnumerateArray());

        // Giving it again for the same day replaces it; a later day becomes the one in force.
        await brian.SetRateAsync("USD", 3.70m, "2026-02-01");
        await brian.SetRateAsync("USD", 3.80m, "2026-02-01");
        await brian.SetRateAsync("EUR", 4.10m, "2026-03-01");
        await brian.SetRateAsync("USD", 3.75m, "2026-01-15");
        var today = await brian.Client.PutAsJsonAsync($"{url}/usd", new { rate = 3.9m });
        Assert.Equal(HttpStatusCode.OK, today.StatusCode);

        var rates = (await brian.Client.GetFromJsonAsync<JsonElement>(url)).EnumerateArray().ToList();
        Assert.Equal([("EUR", "PEN", 4.10m, "2026-03-01"), ("USD", "PEN", 3.9m, (await today.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("asOf").GetString())],
            rates.Select(rate => (rate.GetProperty("currency").GetString(), rate.GetProperty("baseCurrency").GetString(), rate.Money("rate"), rate.GetProperty("asOf").GetString())));
        var stored = await api.ExecuteAsync(services => services.GetRequiredService<AppDbContext>().ExchangeRates.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(rate => rate.OwnerUserId == brian.UserId && rate.Currency == "USD"));
        Assert.Equal(3, stored);

        // Juan is in the same workspace and keeps his own: Brian's do not value his money.
        Assert.Empty((await juan.Client.GetFromJsonAsync<JsonElement>(url)).EnumerateArray());
        await juan.OpenAccountAsync("Dólares", 10m, currency: "USD", workspaceId: workspaceId);
        Assert.Equal(["USD"], (await juan.SummaryAsync(workspaceId)).GetProperty("missingRates").EnumerateArray().Select(currency => currency.GetString()));
    }

    [Fact]
    public async Task The_dashboard_puts_a_month_together_in_the_base_currency()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        await brian.SetRateAsync("USD", 4m);
        var bcp = await brian.OpenAccountAsync("BCP");
        var dollars = await brian.OpenAccountAsync("Dólares", currency: "USD");
        var visa = await brian.OpenCardAsync("Visa", 5000m);
        await brian.EarnAsync(bcp.Id(), 5000m, description: "Sueldo setiembre", occurredOn: "2026-09-01");
        await brian.SpendAsync(bcp.Id(), 1000m, description: "Compras setiembre", occurredOn: "2026-09-10");
        await brian.EarnAsync(bcp.Id(), 4000m, description: "Sueldo octubre", occurredOn: "2026-10-01");
        await brian.SpendAsync(bcp.Id(), 1200m, "Vivienda/Alquiler", "Alquiler", "2026-10-02");
        await brian.SpendAsync(bcp.Id(), 300m, "Vivienda", "Gasfitero", "2026-10-03");
        await brian.SpendAsync(dollars.Id(), 25m, "Suscripciones y ocio/Streaming", "Netflix", "2026-10-04");
        await brian.SpendAsync(visa.Id(), 400m, "Supermercado", "Wong", "2026-10-05");
        await brian.TransferAsync(bcp.Id(), dollars.Id(), 400m, 100m, "2026-10-06");
        var voided = await brian.SpendAsync(bcp.Id(), 999m, description: "Anulado", occurredOn: "2026-10-07");
        await brian.Client.PostAsJsonAsync($"{Books.TransactionsUrl(brian.PersonalWorkspaceId)}/{voided.Id()}/void", new { });

        var october = await brian.DashboardAsync("2026-10");

        Assert.Equal(("2026-10", "PEN"), (october.GetProperty("month").GetString(), october.GetProperty("baseCurrency").GetString()));
        Assert.Equal((4000m, 2000m, 5000m, 1000m, 50m), (
            october.Money("income"), october.Money("expense"), october.Money("previousIncome"), october.Money("previousExpense"), october.Money("savingsRate")));
        var netWorth = october.GetProperty("netWorth");
        Assert.Equal((6000m, 6400m, 400m, 4000m, 2000m), (
            netWorth.Money("amount"), netWorth.Money("assets"), netWorth.Money("debts"), netWorth.Money("previousAmount"), netWorth.Money("change")));

        var cashflow = october.GetProperty("cashflow").EnumerateArray().ToList();
        Assert.Equal(["2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10"], cashflow.Select(month => month.GetProperty("month").GetString()));
        Assert.Equal([(0m, 0m), (0m, 0m), (0m, 0m), (0m, 0m), (5000m, 1000m), (4000m, 2000m)], cashflow.Select(month => (month.Money("income"), month.Money("expense"))));

        // A subcategory adds to its parent; a transfer and a voided expense count nowhere.
        Assert.Equal([("Vivienda", 1500m, 75m), ("Supermercado", 400m, 20m), ("Suscripciones y ocio", 100m, 5m)],
            october.GetProperty("topCategories").EnumerateArray().Select(category => (category.GetProperty("name").GetString(), category.Money("amount"), category.Money("share"))));

        var recent = october.GetProperty("recentMovements").EnumerateArray().ToList();
        Assert.Equal(["Transferencia", "Wong", "Netflix", "Gasfitero", "Alquiler"], recent.Select(movement => movement.GetProperty("description").GetString()));
        Assert.Equal(("transfer", -400m, "PEN", "BCP", "Dólares"), (
            recent[0].GetProperty("kind").GetString(), recent[0].Money("signedAmount"), recent[0].GetProperty("currency").GetString(),
            recent[0].GetProperty("accountName").GetString(), recent[0].GetProperty("counterAccountName").GetString()));
        Assert.Equal((-25m, "USD", "Streaming"), (recent[2].Money("signedAmount"), recent[2].GetProperty("currency").GetString(), recent[2].GetProperty("categoryName").GetString()));

        Assert.Equal([("BCP", 6100m), ("Visa", -400m), ("Dólares", 75m)],
            october.GetProperty("accounts").EnumerateArray().Select(account => (account.GetProperty("name").GetString(), account.Money("balance"))));
        Assert.Empty(october.GetProperty("missingRates").EnumerateArray());

        // A month with nothing in it has no savings rate, and net worth stays where October left it.
        var november = await brian.DashboardAsync("2026-11");
        Assert.Equal((0m, 0m, JsonValueKind.Null), (november.Money("income"), november.Money("expense"), november.GetProperty("savingsRate").ValueKind));
        Assert.Equal((6000m, 0m), (november.GetProperty("netWorth").Money("amount"), november.GetProperty("netWorth").Money("change")));
        Assert.Empty(november.GetProperty("topCategories").EnumerateArray());

        var url = $"/api/v1/workspaces/{brian.PersonalWorkspaceId}";
        var invalid = await brian.Client.GetAsync($"{url}/dashboard?month=octubre");
        Assert.Equal((HttpStatusCode.BadRequest, "reporting.month_invalid"), (invalid.StatusCode, await Accounts.CodeOfAsync(invalid)));
        Assert.Equal(HttpStatusCode.OK, (await brian.Client.GetAsync($"{url}/dashboard")).StatusCode);

        // What each category moved, a subcategory apart from its parent.
        var totals = await brian.Client.GetFromJsonAsync<JsonElement>($"{url}/transactions/category-totals?from=2026-10-01&to=2026-10-31");
        var byCategory = totals.GetProperty("categories").EnumerateArray().ToDictionary(row => row.GetProperty("categoryId").GetGuid());
        Assert.Equal("PEN", totals.GetProperty("baseCurrency").GetString());
        Assert.Equal(5, byCategory.Count);
        Assert.Equal((1200m, 1), (byCategory[await brian.CategoryIdAsync("Vivienda/Alquiler")].Money("amount"), byCategory[await brian.CategoryIdAsync("Vivienda/Alquiler")].GetProperty("count").GetInt32()));
        Assert.Equal(300m, byCategory[await brian.CategoryIdAsync("Vivienda")].Money("amount"));
        Assert.Equal(100m, byCategory[await brian.CategoryIdAsync("Suscripciones y ocio/Streaming")].Money("amount"));
        Assert.Equal(4000m, byCategory[await brian.CategoryIdAsync("Sueldo")].Money("amount"));
        var backwards = await brian.Client.GetAsync($"{url}/transactions/category-totals?from=2026-10-31&to=2026-10-01");
        var incomplete = await brian.Client.GetAsync($"{url}/transactions/category-totals?from=2026-10-01");
        Assert.Equal((HttpStatusCode.BadRequest, "ledger.period_invalid"), (backwards.StatusCode, await Accounts.CodeOfAsync(backwards)));
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
    }

    [Fact]
    public async Task Nobody_moves_money_through_or_reads_the_figures_of_another_member()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;
        var juanEmail = Accounts.NewEmail("juan");
        var juan = await api.RegisterAsync("Juan", invitationToken: await api.InviteAsync(brian, workspaceId, juanEmail), email: juanEmail);
        var briansBank = await brian.OpenAccountAsync("BCP", 1000m);
        var briansOther = await brian.OpenAccountAsync("Interbank", 300m);
        var briansCard = await brian.OpenCardAsync("Visa", 5000m, debt: 200m);
        await brian.SpendAsync(briansBank.Id(), 54m, description: "Terapia", occurredOn: "2026-10-12");
        var briansTransfer = await brian.TransferAsync(briansBank.Id(), briansOther.Id(), 100m);
        var juansBank = await juan.OpenAccountAsync("BCP", 50m, workspaceId: workspaceId);

        // Either side being someone else's account is the same as it not existing, and nothing moves.
        var attempts = new[]
        {
            await juan.PostTransferAsync(juansBank.Id(), briansBank.Id(), 10m, workspaceId: workspaceId),
            await juan.PostTransferAsync(briansBank.Id(), juansBank.Id(), 10m, workspaceId: workspaceId),
            await juan.PostTransferAsync(briansBank.Id(), briansOther.Id(), 10m, workspaceId: workspaceId),
            await juan.PayCardAsync(briansCard.Id(), juansBank.Id(), 10m, workspaceId: workspaceId),
            await juan.PayCardAsync(briansCard.Id(), briansBank.Id(), 10m, workspaceId: workspaceId)
        };
        foreach (var attempt in attempts)
        {
            Assert.Equal((HttpStatusCode.NotFound, "ledger.account.not_found"), (attempt.StatusCode, await Accounts.CodeOfAsync(attempt)));
        }

        var voidAttempt = await juan.Client.PostAsJsonAsync($"{Books.TransactionsUrl(workspaceId)}/{briansTransfer.Id()}/void", new { });
        Assert.Equal((HttpStatusCode.NotFound, "ledger.transaction.not_found"), (voidAttempt.StatusCode, await Accounts.CodeOfAsync(voidAttempt)));
        Assert.Equal((846m, 400m, -200m), (await brian.BalanceAsync(briansBank.Id()), await brian.BalanceAsync(briansOther.Id()), await brian.BalanceAsync(briansCard.Id())));
        Assert.Equal(50m, await juan.BalanceAsync(juansBank.Id(), workspaceId));

        // Each one's figures are only about their own money, the owner of the workspace included.
        var (juansSummary, briansSummary) = (await juan.SummaryAsync(workspaceId), await brian.SummaryAsync());
        Assert.Equal((50m, 0m, 50m), (juansSummary.Money("assets"), juansSummary.Money("debts"), juansSummary.Money("netWorth")));
        Assert.Equal((1246m, 200m, 1046m), (briansSummary.Money("assets"), briansSummary.Money("debts"), briansSummary.Money("netWorth")));

        var month = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2).ToString("yyyy-MM");
        var juansDashboard = await juan.DashboardAsync(month, workspaceId);
        Assert.Equal((50m, 0m), (juansDashboard.GetProperty("netWorth").Money("amount"), juansDashboard.Money("expense")));
        Assert.Equal(["BCP"], juansDashboard.GetProperty("accounts").EnumerateArray().Select(account => account.GetProperty("name").GetString()));
        Assert.Empty((await juan.DashboardAsync("2026-10", workspaceId)).GetProperty("recentMovements").EnumerateArray());
        Assert.Empty((await juan.DashboardAsync("2026-10", workspaceId)).GetProperty("topCategories").EnumerateArray());
        Assert.Empty((await juan.TransactionsAsync("?type=transfer", workspaceId)).GetProperty("items").EnumerateArray());
        var juansTotals = await juan.Client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{workspaceId}/transactions/category-totals?from=2026-10-01&to=2026-10-31");
        Assert.Empty(juansTotals.GetProperty("categories").EnumerateArray());

        // Outside the workspace there is nothing to ask.
        var pedro = await api.RegisterAsync("Pedro");
        foreach (var path in new[] { "accounts/summary", "dashboard", "exchange-rates", "transactions/category-totals?from=2026-10-01&to=2026-10-31" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await pedro.Client.GetAsync($"/api/v1/workspaces/{workspaceId}/{path}")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await api.AnonymousClient().GetAsync($"/api/v1/workspaces/{workspaceId}/{path}")).StatusCode);
        }
    }
}
