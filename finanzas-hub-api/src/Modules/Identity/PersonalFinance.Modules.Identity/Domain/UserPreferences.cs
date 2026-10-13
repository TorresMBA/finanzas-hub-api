using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Identity.Domain;

public enum WeekStart
{
    Monday,
    Sunday
}

public enum ThemePreference
{
    Dark,
    Light
}

/// <summary>How one person wants the application to look and behave. One row per user.</summary>
public sealed class UserPreferences
{
    public static readonly IReadOnlyList<string> SupportedLocales = ["es-PE", "en-US"];
    public static readonly IReadOnlyList<string> SupportedDateFormats = ["dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd"];

    private UserPreferences()
    {
    }

    public Guid UserId { get; private set; }

    public string Locale { get; private set; } = null!;

    public string DateFormat { get; private set; } = null!;

    public WeekStart WeekStart { get; private set; }

    public ThemePreference Theme { get; private set; }

    public bool HideBalances { get; private set; }

    /// <summary>The workspace the application opens on. Null until the user belongs to one.</summary>
    public Guid? DefaultWorkspaceId { get; private set; }

    /// <summary>Share of income the user aims to save, as a percentage. Null when no target is set.</summary>
    public int? SavingsRateTarget { get; private set; }

    public static UserPreferences CreateDefault(Guid userId) => new()
    {
        UserId = userId,
        Locale = SupportedLocales[0],
        DateFormat = SupportedDateFormats[0],
        WeekStart = WeekStart.Monday,
        Theme = ThemePreference.Dark
    };

    public void OpenOn(Guid workspaceId) => DefaultWorkspaceId = workspaceId;

    public Result Update(string? locale, string? dateFormat, WeekStart weekStart, ThemePreference theme, bool hideBalances, Guid? defaultWorkspaceId, int? savingsRateTarget)
    {
        if (locale is null || !SupportedLocales.Contains(locale))
        {
            return Error.Validation("identity.preferences.locale_unsupported", $"Supported locales: {string.Join(", ", SupportedLocales)}.");
        }

        if (dateFormat is null || !SupportedDateFormats.Contains(dateFormat))
        {
            return Error.Validation("identity.preferences.date_format_unsupported", $"Supported date formats: {string.Join(", ", SupportedDateFormats)}.");
        }

        if (!Enum.IsDefined(weekStart) || !Enum.IsDefined(theme))
        {
            return Error.Validation("identity.preferences.value_invalid", "The week start or the theme is not valid.");
        }

        if (savingsRateTarget is < 0 or > 100)
        {
            return Error.Validation("identity.preferences.savings_rate_invalid", "The savings rate target must be between 0 and 100.");
        }

        Locale = locale;
        DateFormat = dateFormat;
        WeekStart = weekStart;
        Theme = theme;
        HideBalances = hideBalances;
        DefaultWorkspaceId = defaultWorkspaceId;
        SavingsRateTarget = savingsRateTarget;
        return Result.Success();
    }
}
