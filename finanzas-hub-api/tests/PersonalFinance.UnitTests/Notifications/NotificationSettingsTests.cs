using PersonalFinance.Modules.Notifications.Contracts;
using PersonalFinance.Modules.Notifications.Domain;

namespace PersonalFinance.UnitTests.Notifications;

public class NotificationSettingsTests
{
    private static readonly TimeSpan Lima = TimeSpan.FromHours(-5);

    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, Lima);

    [Theory]
    [InlineData(13, 21, 59, null, null)]
    [InlineData(13, 22, 0, 14, 7)]
    [InlineData(13, 23, 30, 14, 7)]
    [InlineData(14, 3, 0, 14, 7)]
    [InlineData(14, 7, 0, null, null)]
    public void An_email_written_in_the_quiet_hours_waits_until_they_end_also_across_midnight(int day, int hour, int minute, int? untilDay, int? untilHour)
    {
        // The defaults: quiet from 22:00 to 07:00.
        var settings = NotificationSettings.DefaultFor(Guid.NewGuid());

        var expected = untilDay is null ? (DateTimeOffset?)null : At(untilDay.Value, untilHour!.Value);
        Assert.Equal(expected, settings.HoldUntil(At(day, hour, minute)));
    }

    [Fact]
    public void A_person_changes_the_defaults_and_a_category_left_alone_keeps_its_own()
    {
        var settings = NotificationSettings.DefaultFor(Guid.NewGuid());
        Assert.True(settings.EmailEnabled(NotificationCategory.Budget));
        Assert.False(settings.EmailEnabled(NotificationCategory.LargeMovement));

        settings.SetEmail(NotificationCategory.Budget, false);
        Assert.True(settings.Update(false, new TimeOnly(22, 0), new TimeOnly(7, 0), null).IsSuccess);

        Assert.False(settings.EmailEnabled(NotificationCategory.Budget));
        Assert.False(settings.EmailEnabled(NotificationCategory.LargeMovement));
        Assert.Null(settings.HoldUntil(At(13, 23)));
        Assert.Equal("notifications.quiet_hours_invalid", settings.Update(true, new TimeOnly(8, 0), new TimeOnly(8, 0), null).Error.Code);
        Assert.Equal("notifications.threshold_invalid", settings.Update(false, new TimeOnly(22, 0), new TimeOnly(7, 0), 0m).Error.Code);
    }
}
