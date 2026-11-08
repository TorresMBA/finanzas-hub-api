using PersonalFinance.Modules.Identity.Application.Register;

namespace PersonalFinance.UnitTests.Identity;

public class RegistrationPolicyTests
{
    [Theory]
    [InlineData(false, false, true, false)] // A stranger, once there are accounts: refused.
    [InlineData(false, true, true, true)] // Someone with an invitation.
    [InlineData(false, false, false, true)] // The very first account of an installation.
    [InlineData(true, false, true, true)] // An installation opened on purpose.
    public void An_account_needs_an_invitation_unless_it_is_the_first_or_registration_is_open(
        bool openRegistration, bool hasInvitation, bool anyAccountExists, bool allowed)
    {
        Assert.Equal(allowed, new RegistrationPolicy(openRegistration).Allows(hasInvitation, anyAccountExists));
    }
}
