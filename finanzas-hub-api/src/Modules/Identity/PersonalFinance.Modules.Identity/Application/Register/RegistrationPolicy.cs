namespace PersonalFinance.Modules.Identity.Application.Register;

/// <summary>
/// Who may create an account. PersonalHub is for a small circle: by default an account is created only from an
/// invitation. The very first account is the exception, so a new installation can be started without one.
/// </summary>
/// <param name="OpenRegistration">Anyone may register without an invitation (<c>Auth:OpenRegistration</c>).</param>
public sealed record RegistrationPolicy(bool OpenRegistration)
{
    public bool Allows(bool hasInvitation, bool anyAccountExists) => OpenRegistration || hasInvitation || !anyAccountExists;
}
