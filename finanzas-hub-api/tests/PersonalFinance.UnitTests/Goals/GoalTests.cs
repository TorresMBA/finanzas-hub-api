using PersonalFinance.Modules.Goals.Domain;

namespace PersonalFinance.UnitTests.Goals;

public class GoalTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 13);
    private static readonly Guid Brian = Guid.NewGuid();
    private static readonly Guid Juan = Guid.NewGuid();
    private static readonly Guid Pedro = Guid.NewGuid();

    private static Goal Trip(decimal target = 1000m, DateOnly? date = null)
    {
        var goal = Goal.Create(Guid.NewGuid(), Brian, "Viaje Japón", target, "PEN", date, Now).Value;
        goal.AddParticipant(Brian, Juan, Now);
        return goal;
    }

    private static bool Put(Goal goal, Guid userId, decimal amount, DateOnly? on = null) =>
        goal.Record(Guid.NewGuid(), userId, ContributionType.Contribution, amount, on ?? Today, Guid.NewGuid(), Now);

    [Fact]
    public void Each_participant_withdraws_only_what_is_theirs_and_the_target_is_reached_once()
    {
        var goal = Trip();

        Assert.False(Put(goal, Brian, 500m));
        Assert.True(Put(goal, Juan, 500m));
        Assert.Equal((1000m, 500m, 500m), (goal.Saved, goal.SavedBy(Brian), goal.SavedBy(Juan)));

        // Juan can take out up to his 500, never part of Brian's; the goal dropping and reaching again is not news.
        Assert.Equal("goals.withdrawal.exceeds_own", goal.CanWithdraw(Juan, 600m).Error.Code);
        Assert.True(goal.CanWithdraw(Juan, 500m).IsSuccess);
        goal.Record(Guid.NewGuid(), Juan, ContributionType.Withdrawal, 200m, Today, Guid.NewGuid(), Now);
        Assert.Equal((800m, 300m), (goal.Saved, goal.SavedBy(Juan)));
        Assert.False(Put(goal, Juan, 200m));

        Assert.Equal("goals.participant.not_found", goal.CanContribute(Pedro, 100m).Error.Code);
        Assert.Equal("goals.amount_invalid", goal.CanContribute(Brian, 0m).Error.Code);
    }

    [Fact]
    public void Only_the_owner_runs_the_goal_and_nobody_leaves_with_money_in_it()
    {
        var goal = Trip();
        Put(goal, Juan, 100m);

        Assert.Equal("goals.owner_required", goal.Update(Juan, "Otro", 2000m, null).Error.Code);
        Assert.Equal("goals.owner_required", goal.AddParticipant(Juan, Pedro, Now).Error.Code);
        Assert.Equal("goals.owner_required", goal.Close(Juan, GoalStatus.Cancelled).Error.Code);
        Assert.Equal("goals.participant.has_balance", goal.RemoveParticipant(Brian, Juan).Error.Code);
        Assert.Equal("goals.owner_cannot_leave", goal.RemoveParticipant(Brian, Brian).Error.Code);

        goal.Record(Guid.NewGuid(), Juan, ContributionType.Withdrawal, 100m, Today, Guid.NewGuid(), Now);
        Assert.True(goal.RemoveParticipant(Juan, Juan).IsSuccess);
        Assert.Null(goal.ActiveParticipant(Juan));
        Assert.False(goal.IsShared);

        // A closed goal takes nothing more, but what is in it can still be taken out.
        Put(goal, Brian, 50m);
        Assert.True(goal.Close(Brian, GoalStatus.Cancelled).IsSuccess);
        Assert.Equal("goals.not_active", goal.CanContribute(Brian, 10m).Error.Code);
        Assert.True(goal.CanWithdraw(Brian, 50m).IsSuccess);
    }

    [Theory]
    [InlineData(1000, GoalHealth.Completed)]
    [InlineData(700, GoalHealth.OnTrack)]
    [InlineData(280, GoalHealth.AdjustPace)]
    [InlineData(200, GoalHealth.Behind)]
    public void The_health_compares_what_is_needed_each_month_with_what_has_been_going_in(decimal saved, GoalHealth expected)
    {
        // Created this month, so the average is what went in this month; two months are left to the date.
        var goal = Trip(1000m, new DateOnly(2026, 12, 31));
        Put(goal, Brian, saved);

        var projection = GoalProjection.Of(goal, Today);

        Assert.Equal(expected, projection.Health);
        Assert.Equal(saved >= 1000m ? null : decimal.Round((1000m - saved) / 2, 2), projection.RequiredMonthly);
    }
}
