using PersonalFinance.Modules.Identity.Domain;

namespace PersonalFinance.UnitTests.Identity;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_user_signs_in_with_the_email_and_gets_a_time_ordered_id()
    {
        var user = User.Create(" brian@example.com ", " Brian ", Now).Value;

        Assert.Equal("brian@example.com", user.Email);
        Assert.Equal(user.Email, user.UserName);
        Assert.Equal("Brian", user.DisplayName);
        Assert.Equal(7, user.Id.Version);
    }

    [Theory]
    [InlineData("no-at-sign", "Brian", "identity.email_invalid")]
    [InlineData("a@b@c", "Brian", "identity.email_invalid")]
    [InlineData("brian@example.com", "  ", "identity.display_name_invalid")]
    public void A_user_needs_a_plausible_email_and_a_name(string email, string name, string expectedCode)
    {
        Assert.Equal(expectedCode, User.Create(email, name, Now).Error.Code);
    }

    [Fact]
    public void Changing_the_phone_clears_its_confirmation()
    {
        var user = User.Create("brian@example.com", "Brian", Now).Value;
        user.UpdateProfile("Brian", "+51 999 111 222");
        user.PhoneNumberConfirmed = true;

        Assert.True(user.UpdateProfile("Brian T.", "+51 999 111 222").IsSuccess);
        Assert.True(user.PhoneNumberConfirmed);
        Assert.True(user.UpdateProfile("Brian T.", "+51 988 000 000").IsSuccess);
        Assert.False(user.PhoneNumberConfirmed);
        Assert.Equal("identity.phone_invalid", user.UpdateProfile("Brian", "call me").Error.Code);
    }

    [Fact]
    public void Preferences_start_with_defaults_and_reject_values_outside_the_lists()
    {
        var preferences = UserPreferences.CreateDefault(Guid.NewGuid());

        Assert.Equal("es-PE", preferences.Locale);
        Assert.Equal(ThemePreference.Dark, preferences.Theme);
        Assert.True(preferences.Update("en-US", "yyyy-MM-dd", WeekStart.Sunday, ThemePreference.Light, true, null, 25).IsSuccess);
        Assert.Equal(25, preferences.SavingsRateTarget);
        Assert.Equal("identity.preferences.locale_unsupported", preferences.Update("xx", "yyyy-MM-dd", WeekStart.Sunday, ThemePreference.Light, true, null, 25).Error.Code);
        Assert.Equal("identity.preferences.savings_rate_invalid", preferences.Update("en-US", "yyyy-MM-dd", WeekStart.Sunday, ThemePreference.Light, true, null, 101).Error.Code);
        Assert.Equal("en-US", preferences.Locale);
    }
}
