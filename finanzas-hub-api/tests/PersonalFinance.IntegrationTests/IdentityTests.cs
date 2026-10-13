using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PersonalFinance.IntegrationTests.Support;

namespace PersonalFinance.IntegrationTests;

/// <summary>Accounts and sessions through the real cookie: registering, signing in, lockout, recovery and the profile.</summary>
[Collection(ApiCollection.Name)]
public class IdentityTests(ApiFixture api)
{
    [Fact]
    public async Task Registering_starts_a_session_and_creates_the_personal_workspace()
    {
        var brian = await api.RegisterAsync("Brian");

        var me = await brian.Client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Equal(brian.UserId, me.GetProperty("id").GetGuid());
        Assert.Equal(brian.Email, me.GetProperty("email").GetString());
        Assert.Equal("Brian", me.GetProperty("displayName").GetString());
        Assert.False(me.GetProperty("emailConfirmed").GetBoolean());
        Assert.Equal("es-PE", me.GetProperty("preferences").GetProperty("locale").GetString());
        Assert.Equal(brian.PersonalWorkspaceId, me.GetProperty("preferences").GetProperty("defaultWorkspaceId").GetGuid());

        var workspace = Assert.Single((await brian.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/workspaces")).EnumerateArray());
        Assert.Equal("Personal", workspace.GetProperty("name").GetString());
        Assert.Equal("pendingSetup", workspace.GetProperty("status").GetString());
        Assert.Equal("owner", workspace.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Without_a_session_the_account_endpoints_answer_401()
    {
        var response = await api.BrowserClient().GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_email_can_only_be_registered_once_whatever_its_casing()
    {
        var brian = await api.RegisterAsync("Brian");

        var again = await Accounts.PostRegisterAsync(api.BrowserClient(), brian.Email.ToUpperInvariant(), "Otro");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("identity.email_taken", await Accounts.CodeOfAsync(again));
    }

    [Theory]
    [InlineData("corta-1", "validation.failed")]
    [InlineData("sin-digitos-pero-larga", "identity.invalid")]
    [InlineData("sindigitonisimbolo12", "identity.invalid")]
    public async Task A_weak_password_is_rejected_with_a_message_on_the_password_field(string password, string expectedCode)
    {
        var response = await Accounts.PostRegisterAsync(api.BrowserClient(), Accounts.NewEmail(), "Brian", password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedCode, problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("password", out _));
    }

    [Fact]
    public async Task A_registration_that_fails_at_the_invitation_leaves_no_account_behind()
    {
        var email = Accounts.NewEmail();

        var failed = await Accounts.PostRegisterAsync(api.BrowserClient(), email, "Juan", invitationToken: "no-such-invitation");
        var retried = await Accounts.PostRegisterAsync(api.BrowserClient(), email, "Juan");

        Assert.Equal(HttpStatusCode.NotFound, failed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
    }

    [Fact]
    public async Task Signing_in_needs_the_right_password_and_signing_out_ends_the_session()
    {
        var brian = await api.RegisterAsync("Brian");
        var browser = api.BrowserClient();

        var wrong = await browser.PostAsJsonAsync("/api/v1/auth/login", new { email = brian.Email, password = "Otra-Clave-Larga-1" });
        var unknown = await browser.PostAsJsonAsync("/api/v1/auth/login", new { email = Accounts.NewEmail(), password = Accounts.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("auth.invalid_credentials", await Accounts.CodeOfAsync(wrong));
        Assert.Equal("auth.invalid_credentials", await Accounts.CodeOfAsync(unknown));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/me")).StatusCode);

        var right = await browser.PostAsJsonAsync("/api/v1/auth/login", new { email = brian.Email.ToUpperInvariant(), password = Accounts.Password });
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_one()
    {
        var brian = await api.RegisterAsync("Brian");
        var browser = api.BrowserClient();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var wrong = await browser.PostAsJsonAsync("/api/v1/auth/login", new { email = brian.Email, password = "Otra-Clave-Larga-1" });
            Assert.Equal("auth.invalid_credentials", await Accounts.CodeOfAsync(wrong));
        }

        var fifth = await browser.PostAsJsonAsync("/api/v1/auth/login", new { email = brian.Email, password = "Otra-Clave-Larga-1" });
        var withTheRightOne = await browser.PostAsJsonAsync("/api/v1/auth/login", new { email = brian.Email, password = Accounts.Password });

        Assert.Equal("auth.locked_out", await Accounts.CodeOfAsync(fifth));
        Assert.Equal(HttpStatusCode.Unauthorized, withTheRightOne.StatusCode);
        Assert.Equal("auth.locked_out", await Accounts.CodeOfAsync(withTheRightOne));
    }

    [Fact]
    public async Task The_emailed_link_confirms_the_address_and_a_made_up_token_does_not()
    {
        var brian = await api.RegisterAsync("Brian");
        var token = Accounts.TokenIn(await api.LastEmailBodyAsync(brian.Email));

        var forged = await api.BrowserClient().PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId = brian.UserId, token = "bm8tdmFsZQ" });
        var confirmed = await api.BrowserClient().PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId = brian.UserId, token });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, forged.StatusCode);
        Assert.Equal("identity.token_invalid", await Accounts.CodeOfAsync(forged));
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        Assert.True((await brian.Client.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("emailConfirmed").GetBoolean());
    }

    [Fact]
    public async Task Asking_to_reset_a_password_answers_the_same_for_any_email_and_only_mails_real_accounts()
    {
        var stranger = Accounts.NewEmail();

        var response = await api.BrowserClient().PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = stranger });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(0, await api.QueuedEmailCountAsync(stranger));
    }

    [Fact]
    public async Task The_reset_link_sets_a_new_password_once_and_unlocks_the_account()
    {
        var brian = await api.RegisterAsync("Brian");
        var browser = api.BrowserClient();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await browser.PostAsJsonAsync("/api/v1/auth/login", new { email = brian.Email, password = "Otra-Clave-Larga-1" });
        }

        Assert.Equal(HttpStatusCode.Accepted, (await browser.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = brian.Email })).StatusCode);
        var token = Accounts.TokenIn(await api.LastEmailBodyAsync(brian.Email));
        const string newPassword = "Nueva-Clave-Segura-2";

        var reset = await browser.PostAsJsonAsync("/api/v1/auth/reset-password", new { email = brian.Email, token, newPassword });
        var reused = await browser.PostAsJsonAsync("/api/v1/auth/reset-password", new { email = brian.Email, token, newPassword = "Tercera-Clave-Larga-3" });
        var withOld = await browser.PostAsJsonAsync("/api/v1/auth/login", new { email = brian.Email, password = Accounts.Password });
        var withNew = await browser.PostAsJsonAsync("/api/v1/auth/login", new { email = brian.Email, password = newPassword });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_requires_the_current_one()
    {
        var brian = await api.RegisterAsync("Brian");

        var wrong = await brian.Client.PostAsJsonAsync("/api/v1/auth/change-password", new { currentPassword = "No-Es-La-Actual-1", newPassword = "Nueva-Clave-Segura-2" });
        var right = await brian.Client.PostAsJsonAsync("/api/v1/auth/change-password", new { currentPassword = Accounts.Password, newPassword = "Nueva-Clave-Segura-2" });

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("identity.password_incorrect", await Accounts.CodeOfAsync(wrong));
        Assert.Equal(HttpStatusCode.NoContent, right.StatusCode);
        var login = await api.BrowserClient().PostAsJsonAsync("/api/v1/auth/login", new { email = brian.Email, password = "Nueva-Clave-Segura-2" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task A_person_edits_their_own_profile_and_preferences()
    {
        var brian = await api.RegisterAsync("Brian");

        var profile = await brian.Client.PatchAsJsonAsync("/api/v1/me/profile", new { displayName = "Brian T.", phone = "+51 999 111 222" });
        var preferences = await brian.Client.PutAsJsonAsync("/api/v1/me/preferences", new
        {
            locale = "en-US", dateFormat = "yyyy-MM-dd", weekStart = "sunday", theme = "light", hideBalances = true,
            defaultWorkspaceId = brian.PersonalWorkspaceId, savingsRateTarget = 25
        });
        var invalid = await brian.Client.PutAsJsonAsync("/api/v1/me/preferences", new
        {
            locale = "xx-XX", dateFormat = "yyyy-MM-dd", weekStart = "sunday", theme = "light", hideBalances = true
        });

        Assert.Equal(HttpStatusCode.NoContent, profile.StatusCode);
        Assert.Equal(HttpStatusCode.OK, preferences.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var me = await brian.Client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Equal("Brian T.", me.GetProperty("displayName").GetString());
        Assert.Equal("+51 999 111 222", me.GetProperty("phone").GetString());
        Assert.Equal("light", me.GetProperty("preferences").GetProperty("theme").GetString());
        Assert.Equal(25, me.GetProperty("preferences").GetProperty("savingsRateTarget").GetInt32());
    }

    [Fact]
    public async Task A_state_changing_request_from_another_site_is_refused()
    {
        var brian = await api.RegisterAsync("Brian");
        HttpRequestMessage Logout(string origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
            request.Headers.Add("Origin", origin);
            return request;
        }

        var foreign = await brian.Client.SendAsync(Logout("https://evil.example"));
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal("request.cross_site", await Accounts.CodeOfAsync(foreign));
        Assert.Equal(HttpStatusCode.OK, (await brian.Client.GetAsync("/api/v1/me")).StatusCode);

        var own = await brian.Client.SendAsync(Logout("https://localhost"));
        Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
    }
}
