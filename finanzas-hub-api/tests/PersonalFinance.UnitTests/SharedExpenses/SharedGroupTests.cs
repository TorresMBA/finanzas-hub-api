using PersonalFinance.Modules.SharedExpenses.Domain;

namespace PersonalFinance.UnitTests.SharedExpenses;

public class SharedGroupTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 13);
    private static readonly Guid Brian = Guid.NewGuid();
    private static readonly Guid Juan = Guid.NewGuid();
    private static readonly Guid Pedro = Guid.NewGuid();

    private static decimal[] Split(decimal total, SplitType type, params decimal?[] values) =>
        SplitCalculator.Split(total, type, new[] { Brian, Juan, Pedro }.Select((userId, index) => new SplitInput(userId, values.ElementAtOrDefault(index))).ToList())
            .Value.Select(part => part.Amount).ToArray();

    [Fact]
    public void A_dinner_of_300_is_split_equally_by_percentage_by_amount_and_by_parts_and_always_adds_up_to_the_cent()
    {
        Assert.Equal([100m, 100m, 100m], Split(300m, SplitType.Equal));
        Assert.Equal([150m, 90m, 60m], Split(300m, SplitType.Percentage, 50, 30, 20));
        Assert.Equal([150m, 100m, 50m], Split(300m, SplitType.FixedAmount, 150, 100, 50));
        Assert.Equal([150m, 75m, 75m], Split(300m, SplitType.Shares, 2, 1, 1));

        // 100 among three does not divide: the cent left over goes to the first, and nothing is lost.
        Assert.Equal([33.34m, 33.33m, 33.33m], Split(100m, SplitType.Equal));
        Assert.Equal([0.01m, 0.01m, 0m], Split(0.02m, SplitType.Equal));

        var people = new[] { new SplitInput(Brian, 60), new SplitInput(Juan, 30) };
        Assert.Equal("shared.split.percentages_invalid", SplitCalculator.Split(300m, SplitType.Percentage, people).Error.Code);
        Assert.Equal("shared.split.amounts_invalid", SplitCalculator.Split(300m, SplitType.FixedAmount, people).Error.Code);
    }

    private static (SharedGroup Group, SharedExpense Dinner) Dinner()
    {
        var group = SharedGroup.Create(Guid.NewGuid(), Brian, "Depa", "PEN", Now).Value;
        group.AddMember(Brian, Juan, Now);
        group.AddMember(Brian, Pedro, Now);
        var dinner = group.AddExpense(
            Brian, "Cena", 300m, Brian, Today, SplitType.Equal, [new SplitInput(Brian, null), new SplitInput(Juan, null), new SplitInput(Pedro, null)], Now).Value;
        return (group, dinner);
    }

    [Fact]
    public void An_expense_goes_from_pending_to_partially_paid_to_settled_as_each_one_repays_and_never_more_than_what_is_owed()
    {
        var (group, dinner) = Dinner();
        Assert.Equal((SharedExpenseStatus.Pending, 200m, -100m), (dinner.Status, group.NetOf(Brian), group.NetOf(Juan)));

        Assert.Equal("shared.settlement.exceeds_debt", group.RecordSettlement(Juan, Juan, Brian, 100.01m, Today, Now).Error.Code);
        Assert.Equal("shared.settlement.not_a_party", group.RecordSettlement(Pedro, Juan, Brian, 100m, Today, Now).Error.Code);

        var juans = group.RecordSettlement(Juan, Juan, Brian, 100m, Today, Now).Value;
        Assert.Equal((SharedExpenseStatus.PartiallyPaid, 0m, 100m), (dinner.Status, group.Owed(Juan, Brian), group.Owed(Pedro, Brian)));

        // Who is paid can register it too.
        group.RecordSettlement(Brian, Pedro, Brian, 100m, Today, Now);
        Assert.Equal(SharedExpenseStatus.Settled, dinner.Status);
        Assert.False(group.HasPendingDebts);

        // Voiding a payment, only by who registered it, brings the debt back; an expense with payments is not cancelled.
        Assert.Equal("shared.expense.has_payments", group.CancelExpense(Brian, dinner.Id, Now).Error.Code);
        Assert.Equal("shared.settlement.not_yours", group.VoidSettlement(Brian, juans.Id, Now).Error.Code);
        Assert.True(group.VoidSettlement(Juan, juans.Id, Now).IsSuccess);
        Assert.Equal((SharedExpenseStatus.PartiallyPaid, 100m), (dinner.Status, group.Owed(Juan, Brian)));
        Assert.Equal("shared.member.has_balance", group.RemoveMember(Brian, Juan).Error.Code);
    }

    [Fact]
    public void A_payment_covers_the_oldest_expenses_first_and_is_voided_only_while_recent_and_a_few_times_a_day()
    {
        var (group, dinner) = Dinner();
        var taxi = group.AddExpense(Juan, "Taxi", 60m, Brian, Today.AddDays(1), SplitType.Equal, [new SplitInput(Brian, null), new SplitInput(Juan, null)], Now).Value;

        var payment = group.RecordSettlement(Juan, Juan, Brian, 110m, Today.AddDays(2), Now).Value;

        Assert.Equal([(dinner.Id, 100m), (taxi.Id, 10m)], payment.Allocations.Select(allocation => (allocation.ExpenseId, allocation.Amount)));
        Assert.Equal((0m, 20m), (dinner.ShareOf(Juan)!.Pending, taxi.ShareOf(Juan)!.Pending));

        // A day after it was registered the payment stands; before that it is voided once.
        var later = Now.AddHours(23);
        Assert.Equal("shared.settlement.void_window_passed", group.VoidSettlement(Juan, payment.Id, Now.AddHours(25)).Error.Code);
        Assert.True(group.VoidSettlement(Juan, payment.Id, later).IsSuccess);
        Assert.Equal("shared.settlement.already_voided", group.VoidSettlement(Juan, payment.Id, later).Error.Code);
        Assert.Equal(130m, group.Owed(Juan, Brian));

        // Paying and voiding is not a game: three a day in a group, each person their own count.
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var again = group.RecordSettlement(Juan, Juan, Brian, 10m, Today, later).Value;
            Assert.True(group.VoidSettlement(Juan, again.Id, later).IsSuccess);
        }

        var fourth = group.RecordSettlement(Juan, Juan, Brian, 10m, Today, later).Value;
        Assert.Equal("shared.settlement.too_many_voids", group.VoidSettlement(Juan, fourth.Id, later).Error.Code);
        Assert.Equal((0, 3), (group.VoidsLeft(Juan, later), group.VoidsLeft(Pedro, later)));
        Assert.Equal(3, group.VoidsLeft(Juan, later.AddDays(1).AddMinutes(1)));
    }
}
