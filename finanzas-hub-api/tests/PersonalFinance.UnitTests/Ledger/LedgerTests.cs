using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.UnitTests.Ledger;

public class LedgerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 13);
    private static readonly Guid Workspace = Guid.NewGuid();
    private static readonly Guid Brian = Guid.NewGuid();
    private static readonly Guid Groceries = Guid.NewGuid();

    private static Account Bank(string name = "BCP Sueldo", string currency = "PEN") =>
        Account.Open(Workspace, Brian, name, AccountType.Bank, currency, "BCP", Now).Value;

    private static Operation Expense(Account account, decimal amount = 54m, bool pending = false) =>
        Operation.PostTransaction(OperationKind.Expense, account, amount, Groceries, Today, "Wong", null, null, pending, Now).Value;

    /// <summary>What the ledger would answer for the account: the sum of the entries that count.</summary>
    private static decimal Balance(Account account, params Operation[] operations) =>
        operations.SelectMany(operation => operation.Entries)
            .Where(entry => entry.AccountId == account.Id && entry.IsEffective)
            .Sum(entry => entry.Direction == EntryDirection.In ? entry.Amount : -entry.Amount);

    [Fact]
    public void An_account_belongs_to_its_owner_and_has_one_currency()
    {
        var account = Account.Open(Workspace, Brian, "  BCP   Sueldo ", AccountType.Bank, "pen", "  BCP ", Now).Value;

        Assert.Equal((Workspace, Brian), (account.WorkspaceId, account.OwnerUserId));
        Assert.Equal(("BCP Sueldo", "bcp sueldo", "PEN", "BCP"), (account.Name, account.NameKey, account.Currency, account.Institution));
        Assert.False(account.IsArchived);
    }

    [Fact]
    public void An_account_has_no_balance_of_its_own()
    {
        Assert.DoesNotContain(typeof(Account).GetProperties(), property => property.Name.Contains("Balance", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("", "PEN", "ledger.account.name_invalid")]
    [InlineData("Ahorros", "XXX", "currency.unsupported")]
    [InlineData("Ahorros", null, "currency.unsupported")]
    public void An_account_needs_a_name_and_a_supported_currency(string name, string? currency, string code)
    {
        Assert.Equal(code, Account.Open(Workspace, Brian, name, AccountType.Cash, currency, null, Now).Error.Code);
    }

    [Fact]
    public void An_archived_account_cannot_be_renamed_and_takes_no_new_entries()
    {
        var account = Bank();
        account.Archive(Now);

        Assert.Equal("ledger.account.archived", account.Update("Otro", null, true).Error.Code);
        Assert.Equal("ledger.account.archived", Operation.PostTransaction(OperationKind.Expense, account, 10m, Groceries, Today, "Pan", null, null, false, Now).Error.Code);
        Assert.Equal("ledger.account.archived", Operation.Adjust(account, 10m, Today, Now).Error.Code);
    }

    [Fact]
    public void The_balance_is_the_opening_balance_plus_income_minus_expenses()
    {
        var account = Bank();
        var opening = Operation.OpenBalance(account, 1000m, Today, Now).Value;
        var salary = Operation.PostTransaction(OperationKind.Income, account, 4500m, Guid.NewGuid(), Today, "Sueldo", null, null, false, Now).Value;
        var groceries = Expense(account);

        Assert.Equal(5446m, Balance(account, opening, salary, groceries));
        Assert.Equal((OperationKind.OpeningBalance, OperationStatus.Confirmed, EntryDirection.In), (opening.Kind, opening.Status, opening.Entries.Single().Direction));
        Assert.Equal(EntryDirection.Out, groceries.Entries.Single().Direction);
        Assert.Equal("PEN", groceries.Entries.Single().Currency);
    }

    [Fact]
    public void A_negative_opening_balance_or_adjustment_is_an_entry_going_out()
    {
        var account = Bank();

        var overdraft = Operation.OpenBalance(account, -250m, Today, Now).Value;
        var adjustment = Operation.Adjust(account, -0.5m, Today, Now).Value;

        Assert.Equal((EntryDirection.Out, 250m), (overdraft.Entries.Single().Direction, overdraft.Entries.Single().Amount));
        Assert.Equal((OperationKind.Adjustment, EntryDirection.Out, 0.5m), (adjustment.Kind, adjustment.Entries.Single().Direction, adjustment.Entries.Single().Amount));
    }

    [Fact]
    public void A_pending_transaction_counts_in_the_balance_and_can_be_confirmed_once()
    {
        var account = Bank();
        var pending = Expense(account, pending: true);

        Assert.Equal(OperationStatus.Pending, pending.Status);
        Assert.Equal(-54m, Balance(account, pending));
        Assert.True(pending.Confirm().IsSuccess);
        Assert.Equal("ledger.transaction.not_pending", pending.Confirm().Error.Code);
    }

    [Fact]
    public void Voiding_takes_the_operation_out_of_the_balance_and_keeps_it_with_its_reason()
    {
        var account = Bank();
        var opening = Operation.OpenBalance(account, 1000m, Today, Now).Value;
        var groceries = Expense(account);

        Assert.True(groceries.Void("  Duplicado ", Now).IsSuccess);

        Assert.Equal(1000m, Balance(account, opening, groceries));
        Assert.Equal((OperationStatus.Voided, "Duplicado", Now), (groceries.Status, groceries.VoidReason, groceries.VoidedAt));
        Assert.Single(groceries.Entries);

        // Voiding again changes nothing, not even the first reason.
        Assert.True(groceries.Void("Otro motivo", Now.AddDays(1)).IsSuccess);
        Assert.Equal(("Duplicado", Now), (groceries.VoidReason, groceries.VoidedAt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void An_amount_must_be_positive(int amount)
    {
        Assert.Equal("ledger.amount_invalid", Operation.PostTransaction(OperationKind.Expense, Bank(), amount, Groceries, Today, "Pan", null, null, false, Now).Error.Code);
    }

    [Fact]
    public void An_amount_cannot_have_more_decimals_than_its_currency()
    {
        var result = Operation.PostTransaction(OperationKind.Expense, Bank(), 10.005m, Groceries, Today, "Pan", null, null, false, Now);

        Assert.Equal("money.too_many_decimals", result.Error.Code);
    }

    [Theory]
    [InlineData(OperationKind.Transfer)]
    [InlineData(OperationKind.OpeningBalance)]
    [InlineData(OperationKind.Adjustment)]
    public void A_person_registers_only_income_and_expenses(OperationKind kind)
    {
        Assert.Equal("ledger.transaction.kind_invalid", Operation.PostTransaction(kind, Bank(), 10m, Groceries, Today, "Pan", null, null, false, Now).Error.Code);
    }

    [Fact]
    public void A_transaction_needs_a_description()
    {
        Assert.Equal("ledger.transaction.description_invalid", Operation.PostTransaction(OperationKind.Expense, Bank(), 10m, Groceries, Today, "  ", null, null, false, Now).Error.Code);
    }

    [Fact]
    public void Editing_moves_the_amount_to_the_new_account_date_and_kind()
    {
        var bank = Bank();
        var cash = Account.Open(Workspace, Brian, "Efectivo", AccountType.Cash, "PEN", null, Now).Value;
        var transaction = Expense(bank);
        var salary = Guid.NewGuid();

        var edited = transaction.Edit(OperationKind.Income, cash, 80m, salary, Today.AddDays(-2), "Devolución", "Wong", "Era un ingreso");

        Assert.True(edited.IsSuccess);
        Assert.Equal(0m, Balance(bank, transaction));
        Assert.Equal(80m, Balance(cash, transaction));
        var entry = transaction.Entries.Single();
        Assert.Equal((cash.Id, EntryDirection.In, Today.AddDays(-2)), (entry.AccountId, entry.Direction, entry.OccurredOn));
        Assert.Equal((OperationKind.Income, salary, "Devolución", "Wong", "Era un ingreso"), (transaction.Kind, transaction.CategoryId, transaction.Description, transaction.Merchant, transaction.Note));
    }

    [Fact]
    public void A_rejected_edit_changes_nothing()
    {
        var account = Bank();
        var transaction = Expense(account);

        Assert.Equal("ledger.amount_invalid", transaction.Edit(OperationKind.Income, account, 0m, Groceries, Today.AddDays(1), "Otra", null, null).Error.Code);

        Assert.Equal((OperationKind.Expense, "Wong", Today), (transaction.Kind, transaction.Description, transaction.OccurredOn));
        Assert.Equal(-54m, Balance(account, transaction));
    }

    [Fact]
    public void A_transaction_stays_on_an_account_archived_later_but_cannot_move_to_one()
    {
        var bank = Bank();
        var old = Account.Open(Workspace, Brian, "Antigua", AccountType.Bank, "PEN", null, Now).Value;
        var transaction = Expense(bank);
        bank.Archive(Now);
        old.Archive(Now);

        Assert.True(transaction.Edit(OperationKind.Expense, bank, 60m, Groceries, Today, "Wong", null, null).IsSuccess);
        Assert.Equal("ledger.account.archived", transaction.Edit(OperationKind.Expense, old, 60m, Groceries, Today, "Wong", null, null).Error.Code);
    }

    [Fact]
    public void What_the_application_registered_and_what_was_voided_cannot_be_edited()
    {
        var account = Bank();
        var opening = Operation.OpenBalance(account, 1000m, Today, Now).Value;
        var voided = Expense(account);
        voided.Void(null, Now);

        Assert.Equal("ledger.transaction.not_editable", opening.Edit(OperationKind.Income, account, 900m, Groceries, Today, "Saldo", null, null).Error.Code);
        Assert.Equal("ledger.transaction.not_editable", opening.SetTags([Guid.NewGuid()]).Error.Code);
        Assert.Equal("ledger.transaction.voided", voided.Edit(OperationKind.Expense, account, 10m, Groceries, Today, "Pan", null, null).Error.Code);
        Assert.True(opening.Void("Me equivoqué de saldo", Now).IsSuccess);
    }

    [Fact]
    public void A_transaction_has_at_most_ten_tags_without_repeats()
    {
        var transaction = Expense(Bank());
        var (fijo, hogar) = (Guid.NewGuid(), Guid.NewGuid());

        Assert.True(transaction.SetTags([fijo, hogar, fijo]).IsSuccess);
        Assert.Equal([fijo, hogar], transaction.Tags.Select(tag => tag.TagId));
        Assert.True(transaction.SetTags([hogar]).IsSuccess);
        Assert.Equal([hogar], transaction.Tags.Select(tag => tag.TagId));
        Assert.Equal("ledger.transaction.too_many_tags", transaction.SetTags(Enumerable.Range(0, Operation.MaxTags + 1).Select(_ => Guid.NewGuid()).ToList()).Error.Code);
    }
}
