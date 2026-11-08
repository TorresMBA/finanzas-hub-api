using Microsoft.Extensions.Configuration;
using PersonalFinance.Infrastructure.Email;
using PersonalFinance.Infrastructure.WhatsApp;

namespace PersonalFinance.Infrastructure;

/// <summary>
/// What Production refuses to start without. The fallbacks that make a development machine work (emails and
/// WhatsApp messages written to a folder, codes shown on screen) would fail silently in Production, so a missing
/// value stops the host at once instead of being found out by a person who never got their email.
/// </summary>
public static class ProductionSettings
{
    /// <summary>The problems found; empty when the configuration can serve Production.</summary>
    public static IReadOnlyList<string> Problems(IConfiguration configuration)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(DependencyInjection.ConnectionStringName)))
        {
            problems.Add($"ConnectionStrings:{DependencyInjection.ConnectionStringName} is not set.");
        }

        var email = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
        if (string.IsNullOrWhiteSpace(email.SmtpHost))
        {
            problems.Add("Email:SmtpHost is not set: emails would be written to a folder instead of sent.");
        }

        if (string.IsNullOrWhiteSpace(configuration["Frontend:BaseUrl"]))
        {
            problems.Add("Frontend:BaseUrl is not set: the links in the emails would not work.");
        }

        var whatsApp = configuration.GetSection(WhatsAppOptions.SectionName).Get<WhatsAppOptions>() ?? new WhatsAppOptions();
        if (whatsApp.ShowCodesWithoutAccount)
        {
            problems.Add("WhatsApp:ShowCodesWithoutAccount must be false: it shows verification codes on screen.");
        }

        if (whatsApp.Enabled)
        {
            foreach (var (name, value) in new[]
                     {
                         ("AccessToken", whatsApp.AccessToken), ("PhoneNumberId", whatsApp.PhoneNumberId),
                         ("AppSecret", whatsApp.AppSecret), ("VerifyToken", whatsApp.VerifyToken)
                     })
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    problems.Add($"WhatsApp:{name} is not set while WhatsApp:Enabled is true.");
                }
            }
        }

        return problems;
    }

    /// <summary>Stops the host when <see cref="Problems"/> finds anything.</summary>
    public static void EnsureValid(IConfiguration configuration)
    {
        var problems = Problems(configuration);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("The configuration cannot serve Production:\n- " + string.Join("\n- ", problems));
        }
    }
}
