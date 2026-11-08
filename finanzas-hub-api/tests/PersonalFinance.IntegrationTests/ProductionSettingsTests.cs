using Microsoft.Extensions.Configuration;
using PersonalFinance.Infrastructure;

namespace PersonalFinance.IntegrationTests;

/// <summary>What stops Production from starting. No database: only the configuration is read.</summary>
public class ProductionSettingsTests
{
    private static readonly Dictionary<string, string?> Complete = new()
    {
        ["ConnectionStrings:PersonalFinance"] = "Host=db;Database=pf",
        ["Email:SmtpHost"] = "smtp.example.com",
        ["Frontend:BaseUrl"] = "https://finanzas.example.com"
    };

    private static IReadOnlyList<string> ProblemsWith(params (string Key, string? Value)[] changes)
    {
        var values = new Dictionary<string, string?>(Complete);
        foreach (var (key, value) in changes)
        {
            values[key] = value;
        }

        return ProductionSettings.Problems(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    [Fact]
    public void A_complete_configuration_with_WhatsApp_off_can_serve_production()
    {
        Assert.Empty(ProblemsWith());
    }

    [Theory]
    [InlineData("Email:SmtpHost", "", "Email:SmtpHost")]
    [InlineData("Frontend:BaseUrl", "", "Frontend:BaseUrl")]
    [InlineData("WhatsApp:ShowCodesWithoutAccount", "true", "WhatsApp:ShowCodesWithoutAccount")]
    [InlineData("WhatsApp:Enabled", "true", "WhatsApp:AccessToken")]
    public void Production_refuses_what_would_otherwise_fail_silently(string key, string value, string reported)
    {
        Assert.Contains(ProblemsWith((key, value)), problem => problem.StartsWith(reported, StringComparison.Ordinal));
    }
}
