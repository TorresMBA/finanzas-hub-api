using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Notifications.Contracts;

namespace PersonalFinance.Modules.Notifications.Domain;

public enum NotificationChannel
{
    Email
}

/// <summary>A choice a person made for one category and channel. Without one, the default of the category applies.</summary>
public sealed class NotificationPreference
{
    private NotificationPreference()
    {
    }

    internal NotificationPreference(NotificationCategory category, NotificationChannel channel, bool enabled)
    {
        Category = category;
        Channel = channel;
        Enabled = enabled;
    }

    public NotificationCategory Category { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public bool Enabled { get; internal set; }
}

/// <summary>
/// How one person wants to be told: which categories also go to their email, the hours in which an email
/// waits, and from which amount a movement is worth a notice. The inbox always gets everything.
/// </summary>
public sealed class NotificationSettings : AggregateRoot
{
    public const decimal DefaultLargeMovementThreshold = 500m;
    public const decimal MaxLargeMovementThreshold = 999_999_999.99m;

    private readonly List<NotificationPreference> _preferences = [];

    private NotificationSettings()
    {
    }

    private NotificationSettings(Guid userId) : base(userId)
    {
    }

    /// <summary>The settings are one per person: their id is that of the person.</summary>
    public Guid UserId => Id;

    public bool QuietHoursEnabled { get; private set; }

    public TimeOnly QuietFrom { get; private set; }

    public TimeOnly QuietTo { get; private set; }

    /// <summary>In the base currency of the workspace of the movement. Null: no notice for large movements.</summary>
    public decimal? LargeMovementThreshold { get; private set; }

    public IReadOnlyCollection<NotificationPreference> Preferences => _preferences.AsReadOnly();

    /// <summary>What applies to someone who never opened their settings.</summary>
    public static NotificationSettings DefaultFor(Guid userId) => new(userId)
    {
        QuietHoursEnabled = true,
        QuietFrom = new TimeOnly(22, 0),
        QuietTo = new TimeOnly(7, 0),
        LargeMovementThreshold = DefaultLargeMovementThreshold
    };

    /// <summary>A budget about to run out or a payment coming up is worth an email; a large movement the person just made is not.</summary>
    public static bool EmailByDefault(NotificationCategory category) => category != NotificationCategory.LargeMovement;

    public bool EmailEnabled(NotificationCategory category) =>
        _preferences.FirstOrDefault(preference => preference.Category == category && preference.Channel == NotificationChannel.Email)?.Enabled
        ?? EmailByDefault(category);

    public void SetEmail(NotificationCategory category, bool enabled)
    {
        if (_preferences.FirstOrDefault(preference => preference.Category == category && preference.Channel == NotificationChannel.Email) is { } existing)
        {
            existing.Enabled = enabled;
        }
        else
        {
            _preferences.Add(new NotificationPreference(category, NotificationChannel.Email, enabled));
        }
    }

    public Result Update(bool quietHoursEnabled, TimeOnly quietFrom, TimeOnly quietTo, decimal? largeMovementThreshold)
    {
        if (quietHoursEnabled && quietFrom == quietTo)
        {
            return Error.Validation("notifications.quiet_hours_invalid", "The quiet hours must start and end at different times.");
        }

        if (largeMovementThreshold is { } threshold && (threshold <= 0 || threshold > MaxLargeMovementThreshold || decimal.Round(threshold, 2) != threshold))
        {
            return Error.Validation("notifications.threshold_invalid", "The amount must be greater than zero, with two decimals at most.");
        }

        QuietHoursEnabled = quietHoursEnabled;
        QuietFrom = quietFrom;
        QuietTo = quietTo;
        LargeMovementThreshold = largeMovementThreshold;
        return Result.Success();
    }

    /// <summary>
    /// When an email written now may go out: null for right away, or the end of the quiet hours it falls in.
    /// The hours are the wall clock of the person, so <paramref name="localNow"/> comes in their time zone.
    /// They may span midnight, as 22:00 to 07:00 does.
    /// </summary>
    public DateTimeOffset? HoldUntil(DateTimeOffset localNow)
    {
        if (!QuietHoursEnabled)
        {
            return null;
        }

        var time = TimeOnly.FromTimeSpan(localNow.TimeOfDay);
        var spansMidnight = QuietFrom > QuietTo;
        var quiet = spansMidnight ? time >= QuietFrom || time < QuietTo : time >= QuietFrom && time < QuietTo;
        if (!quiet)
        {
            return null;
        }

        var endsTomorrow = spansMidnight && time >= QuietFrom;
        var day = DateOnly.FromDateTime(localNow.DateTime).AddDays(endsTomorrow ? 1 : 0);
        return new DateTimeOffset(day.ToDateTime(QuietTo), localNow.Offset);
    }
}
