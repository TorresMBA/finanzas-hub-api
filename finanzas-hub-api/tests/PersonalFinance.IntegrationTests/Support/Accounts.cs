using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Infrastructure.Persistence;

namespace PersonalFinance.IntegrationTests.Support;

/// <summary>A registered person with a signed-in browser.</summary>
public sealed record TestAccount(HttpClient Client, Guid UserId, string Email, string DisplayName, Guid PersonalWorkspaceId);

/// <summary>Real accounts and workspaces, created through the API the way a person would.</summary>
public static partial class Accounts
{
    public const string Password = "Correcta-Bateria-9";

    public static string NewEmail(string name = "user") => $"{name}-{Guid.NewGuid():N}@example.com";

    public static async Task<HttpResponseMessage> PostRegisterAsync(HttpClient client, string email, string displayName, string password = Password, string? invitationToken = null) =>
        await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password, displayName, invitationToken });

    public static async Task<TestAccount> RegisterAsync(this ApiFixture api, string displayName = "Brian", string? invitationToken = null, string? email = null)
    {
        var client = api.BrowserClient();
        email ??= NewEmail(displayName.ToLowerInvariant());
        var response = await PostRegisterAsync(client, email, displayName, invitationToken: invitationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var userId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetGuid();

        var workspaces = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/workspaces");
        var personal = workspaces.EnumerateArray().Single(workspace => workspace.GetProperty("role").GetString() == "owner");
        return new TestAccount(client, userId, email, displayName, personal.GetProperty("id").GetGuid());
    }

    /// <summary>Registers a person and completes the onboarding of their personal workspace.</summary>
    public static async Task<TestAccount> RegisterWithActiveWorkspaceAsync(this ApiFixture api, string displayName = "Brian")
    {
        var account = await api.RegisterAsync(displayName);
        var onboarding = await account.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{account.PersonalWorkspaceId}/onboarding/complete", new { baseCurrency = "PEN", timeZone = "America/Lima" });
        Assert.Equal(HttpStatusCode.OK, onboarding.StatusCode);
        return account;
    }

    /// <summary>Invites an email and returns the token of the link that was emailed.</summary>
    public static async Task<string> InviteAsync(this ApiFixture api, TestAccount inviter, Guid workspaceId, string email, string role = "member")
    {
        var response = await inviter.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/invitations", new { email, role });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return InviteLink().Match(await api.LastEmailBodyAsync(email)).Groups[1].Value;
    }

    /// <summary>The body of the newest queued email for an address. Queued emails keep their body until they are sent.</summary>
    public static async Task<string> LastEmailBodyAsync(this ApiFixture api, string to)
    {
        var body = await api.ExecuteAsync(services => services.GetRequiredService<AppDbContext>().OutgoingEmails.AsNoTracking()
            .Where(email => email.To == to.ToLower() && email.SentAt == null || email.To == to && email.SentAt == null)
            .OrderByDescending(email => email.CreatedAt)
            .Select(email => email.Body)
            .FirstOrDefaultAsync());
        Assert.False(string.IsNullOrEmpty(body), $"No email is queued for {to}.");
        return body!;
    }

    public static async Task<int> QueuedEmailCountAsync(this ApiFixture api, string to) =>
        await api.ExecuteAsync(services => services.GetRequiredService<AppDbContext>().OutgoingEmails.AsNoTracking().CountAsync(email => email.To == to));

    public static string TokenIn(string emailBody) => TokenParameter().Match(emailBody).Groups[1].Value;

    public static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [GeneratedRegex(@"/invite/([\w-]+)")]
    private static partial Regex InviteLink();

    [GeneratedRegex(@"[?&]token=([\w-]+)")]
    private static partial Regex TokenParameter();
}
