using PersonalFinance.Modules.Ledger.Application.ExchangeRates;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.UnitTests.Ledger;

public class TransfersCardsAndRatesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 13);
    private static readonly Guid Workspace = Guid.NewGuid();
    private static readonly Guid Brian = Guid.NewGuid();

    private static Account Bank(string name, string currency = "PEN", Guid? owner = null) =>
        Account.Open(Workspace, owner ?? Brian, name, AccountType.Bank, currency, null, Now).Value;

    private static Account Card(decimal limit = 5000m, int paymentDay = 5) =>
        Account.Open(Workspace, Brian, "Visa", AccountType.CreditCard, "PEN", "BCP", Now, new CreditCardTerms(limit, paymentDay, null)).Value;

    private static decimal Balance(Account account, params Operation[] operations) =>
        operations.SelectMany(operation => operation.Entries)
            .Where(entry => entry.AccountId == account.Id && entry.IsEffective)
            .Sum(entry => entry.Direction == EntryDirection.In ? entry.Amount : -entry.Amount);

    private static ExchangeRate Rate(decimal rate, int month = 1, int day = 1, string currency = "USD") =>
        ExchangeRate.Set(Workspace, Brian, "PEN", currency, rate, new DateOnly(2026, month, day), Now).Value;

    [Fact]
    public void A_transfer_is_one_operation_that_takes_from_one_account_what_it_gives_the_other()
    {
        var (bcp, interbank) = (Bank("BCP"), Bank("Interbank"));

        var transfer = Operation.PostTransfer(bcp, interbank, 500m, null, Today, null, "  Para el alquiler ", Now).Value;

        Assert.Equal((OperationKind.Transfer, OperationStatus.Confirmed, "Transferencia", "Para el alquiler"), (transfer.Kind, transfer.Status, transfer.Description, transfer.Note));
        Assert.Null(transfer.CategoryId);
        Assert.Equal(2, transfer.Entries.Count);
        Assert.Equal(-500m, Balance(bcp, transfer));
        Assert.Equal(500m, Balance(interbank, transfer));
        Assert.False(transfer.IsTransaction);
    }

    [Fact]
    public void Voiding_a_transfer_takes_both_entries_out_at_once()
    {
        var (bcp, interbank) = (Bank("BCP"), Bank("Interbank"));
        var transfer = Operation.PostTransfer(bcp, interbank, 500m, 500m, Today, "Ahorro", null, Now).Value;

        Assert.True(transfer.Void("Me equivoqué", Now).IsSuccess);

        Assert.All(transfer.Entries, entry => Assert.False(entry.IsEffective));
        Assert.Equal((0m, 0m), (Balance(bcp, transfer), Balance(interbank, transfer)));
    }

    [Fact]
    public void Between_two_currencies_the_person_states_both_amounts_and_nothing_is_converted()
    {
        var (soles, dollars) = (Bank("BCP"), Bank("BCP Dólares", "USD"));

        var transfer = Operation.PostTransfer(soles, dollars, 375m, 100m, Today, null, null, Now).Value;

        var leaving = transfer.Entries.Single(entry => entry.Direction == EntryDirection.Out);
        var arriving = transfer.Entries.Single(entry => entry.Direction == EntryDirection.In);
        Assert.Equal((375m, "PEN", 100m, "USD"), (leaving.Amount, leaving.Currency, arriving.Amount, arriving.Currency));
        Assert.Equal("ledger.transfer.destination_amount_required", Operation.PostTransfer(soles, dollars, 375m, null, Today, null, null, Now).Error.Code);
    }

    [Fact]
    public void A_transfer_needs_two_open_accounts_of_the_same_person_and_a_coherent_amount()
    {
        var (bcp, interbank, archived) = (Bank("BCP"), Bank("Interbank"), Bank("Vieja"));
        archived.Archive(Now);
        var juans = Bank("De Juan", owner: Guid.NewGuid());

        Assert.Equal("ledger.transfer.same_account", Operation.PostTransfer(bcp, bcp, 10m, null, Today, null, null, Now).Error.Code);
        Assert.Equal("ledger.transfer.amount_mismatch", Operation.PostTransfer(bcp, interbank, 10m, 9m, Today, null, null, Now).Error.Code);
        Assert.Equal("ledger.amount_invalid", Operation.PostTransfer(bcp, interbank, 0m, null, Today, null, null, Now).Error.Code);
        Assert.Equal("money.too_many_decimals", Operation.PostTransfer(bcp, interbank, 1.001m, null, Today, null, null, Now).Error.Code);
        Assert.Equal("ledger.account.archived", Operation.PostTransfer(bcp, archived, 10m, null, Today, null, null, Now).Error.Code);
        Assert.Equal("ledger.account.archived", Operation.PostTransfer(archived, bcp, 10m, null, Today, null, null, Now).Error.Code);
        Assert.Equal("ledger.transfer.different_owners", Operation.PostTransfer(bcp, juans, 10m, null, Today, null, null, Now).Error.Code);
    }

    [Fact]
    public void A_transfer_is_voided_and_registered_again_never_edited_or_tagged()
    {
        var (bcp, interbank) = (Bank("BCP"), Bank("Interbank"));
        var transfer = Operation.PostTransfer(bcp, interbank, 500m, null, Today, null, null, Now).Value;

        Assert.Equal("ledger.transaction.not_editable", transfer.Edit(OperationKind.Expense, bcp, 400m, Guid.NewGuid(), Today, "Otra", null, null).Error.Code);
        Assert.Equal("ledger.transaction.not_editable", transfer.SetTags([Guid.NewGuid()]).Error.Code);
        Assert.Equal("ledger.transaction.not_pending", transfer.Confirm().Error.Code);
    }

    [Fact]
    public void A_credit_card_owes_its_negative_balance_and_has_left_its_line_minus_that()
    {
        var card = Card(5000m);
        var groceries = Operation.PostTransaction(OperationKind.Expense, card, 200m, Guid.NewGuid(), Today, "Wong", null, null, false, Now).Value;
        var payment = Operation.PostTransfer(Bank("BCP"), card, 150m, null, Today, "Pago de tarjeta", null, Now).Value;

        Assert.True(card.IsCreditCard);
        Assert.Equal(-200m, Balance(card, groceries));
        Assert.Equal(4800m, card.CreditCard!.AvailableWith(Balance(card, groceries)));
        Assert.Equal(4950m, card.CreditCard.AvailableWith(Balance(card, groceries, payment)));
    }

    [Fact]
    public void Only_a_credit_card_has_a_line_and_it_always_has_one()
    {
        Assert.Equal("ledger.card.terms_required", Account.Open(Workspace, Brian, "Visa", AccountType.CreditCard, "PEN", null, Now).Error.Code);
        Assert.Equal("ledger.card.not_a_card", Account.Open(Workspace, Brian, "BCP", AccountType.Bank, "PEN", null, Now, new CreditCardTerms(5000m, 5, null)).Error.Code);
        Assert.Equal("ledger.card.not_a_card", Bank("BCP").Update("BCP", null, true, new CreditCardTerms(5000m, 5, null)).Error.Code);
        Assert.Null(Bank("BCP").CreditCard);
    }

    [Theory]
    [InlineData(0, 5, null, "ledger.card.limit_invalid")]
    [InlineData(-1, 5, null, "ledger.card.limit_invalid")]
    [InlineData(5000.001, 5, null, "money.too_many_decimals")]
    [InlineData(5000, 0, null, "ledger.card.day_invalid")]
    [InlineData(5000, 32, null, "ledger.card.day_invalid")]
    [InlineData(5000, 5, 0, "ledger.card.day_invalid")]
    public void The_line_is_positive_and_the_days_are_days_of_the_month(double limit, int paymentDay, int? closingDay, string code)
    {
        var opened = Account.Open(Workspace, Brian, "Visa", AccountType.CreditCard, "PEN", null, Now, new CreditCardTerms((decimal)limit, paymentDay, closingDay));

        Assert.Equal(code, opened.Error.Code);
    }

    [Fact]
    public void Changing_a_card_keeps_its_terms_unless_new_ones_are_given_and_a_rejected_change_changes_nothing()
    {
        var card = Card(5000m, 5);

        Assert.True(card.Update("Visa Oro", "BCP", true).IsSuccess);
        Assert.Equal((5000m, 5), (card.CreditCard!.CreditLimit, card.CreditCard.PaymentDay));

        Assert.Equal("ledger.card.limit_invalid", card.Update("Otra", null, false, new CreditCardTerms(0m, 10, 25)).Error.Code);
        Assert.Equal(("Visa Oro", true, 5000m), (card.Name, card.IncludeInNetWorth, card.CreditCard.CreditLimit));

        Assert.True(card.Update("Visa Oro", "BCP", true, new CreditCardTerms(8000m, 10, 25)).IsSuccess);
        Assert.Equal((8000m, 10, 25), (card.CreditCard.CreditLimit, card.CreditCard.PaymentDay, card.CreditCard.ClosingDay));
    }

    [Theory]
    [InlineData(5, "2026-10-01", "2026-10-05")]
    [InlineData(5, "2026-10-05", "2026-10-05")]
    [InlineData(5, "2026-10-06", "2026-11-05")]
    [InlineData(31, "2026-02-10", "2026-02-28")]
    [InlineData(31, "2026-04-30", "2026-04-30")]
    [InlineData(15, "2026-12-20", "2027-01-15")]
    public void The_next_payment_falls_on_the_payment_day_or_on_the_last_day_of_a_shorter_month(int paymentDay, string today, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), Card(paymentDay: paymentDay).CreditCard!.NextPaymentDate(DateOnly.Parse(today)));
    }

    [Fact]
    public void An_account_counts_in_the_net_worth_unless_the_person_takes_it_out()
    {
        var account = Bank("BCP");
        var apart = Account.Open(Workspace, Brian, "Fondo", AccountType.Bank, "PEN", null, Now, includeInNetWorth: false).Value;

        Assert.True(account.IncludeInNetWorth);
        Assert.False(apart.IncludeInNetWorth);
        Assert.True(apart.Update("Fondo", null, true).IsSuccess);
        Assert.True(apart.IncludeInNetWorth);
    }

    [Theory]
    [InlineData("PEN", 3.75, "ledger.exchange_rate.same_currency")]
    [InlineData("JPY", 3.75, "currency.unsupported")]
    [InlineData("USD", 0, "ledger.exchange_rate.rate_invalid")]
    [InlineData("USD", -1, "ledger.exchange_rate.rate_invalid")]
    [InlineData("USD", 3.1234567, "ledger.exchange_rate.rate_invalid")]
    [InlineData("USD", 1000001, "ledger.exchange_rate.rate_invalid")]
    public void An_exchange_rate_is_positive_and_values_another_currency(string currency, double rate, string code)
    {
        Assert.Equal(code, ExchangeRate.Set(Workspace, Brian, "PEN", currency, (decimal)rate, Today, Now).Error.Code);
    }

    [Fact]
    public void A_conversion_is_rounded_to_the_decimals_of_the_base_currency()
    {
        Assert.Equal(37.54m, Rate(3.754m).Convert(10m));
        Assert.Equal(0.02m, Rate(3.5m).Convert(0.005m));
        Assert.Equal(-375m, Rate(3.75m).Convert(-100m));
    }

    [Fact]
    public void The_rate_of_a_day_is_the_latest_one_that_had_started_by_then()
    {
        var table = new RateTable("PEN", [Rate(3.50m, 1), Rate(3.80m, 6), Rate(4.10m, 3, currency: "EUR")]);

        Assert.Null(table.At("USD", new DateOnly(2025, 12, 31)));
        Assert.Equal(3.50m, table.At("USD", new DateOnly(2026, 5, 31))!.Rate);
        Assert.Equal(3.80m, table.At("USD", new DateOnly(2026, 6, 1))!.Rate);
        Assert.Equal(3.80m, table.Latest("USD")!.Rate);
        Assert.Equal((380m, 100m, 410m), (table.ToBase("USD", 100m), table.ToBase("PEN", 100m), table.ToBase("EUR", 100m)));
    }

    [Fact]
    public void A_sum_in_the_base_currency_leaves_out_what_has_no_rate_and_says_which_currency_it_was()
    {
        var sum = new BaseSum(new RateTable("PEN", [Rate(4m)]));

        sum.Add("PEN", 100m);
        sum.Add("USD", 10m, alreadyInBase: 35m);
        sum.Add("EUR", 50m);
        sum.Add("EUR", 0m);

        Assert.Equal(175m, sum.Total);
        Assert.Equal(["EUR"], sum.Missing);
        Assert.Null(new RateTable(null, []).ToBase("PEN", 100m));
    }
}
