using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Infrastructure.Email;
using PersonalFinance.Infrastructure.Persistence;
using PersonalFinance.IntegrationTests.Support;

namespace PersonalFinance.IntegrationTests;

/// <summary>Workspaces, members, roles and invitations, with real accounts and the real membership check.</summary>
[Collection(ApiCollection.Name)]
public class WorkspacesTests(ApiFixture api)
{
    [Fact]
    public async Task Brian_sets_up_his_workspace_and_invites_Juan_who_joins_as_a_member_and_cannot_invite()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;
        var juanEmail = Accounts.NewEmail("juan");
        var token = await api.InviteAsync(brian, workspaceId, juanEmail);

        var juan = await api.RegisterAsync("Juan", invitationToken: token, email: juanEmail);

        // Juan belongs to two workspaces: his own and Brian's, and opens on the one he was invited to.
        var juansWorkspaces = (await juan.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/workspaces")).EnumerateArray().ToList();
        Assert.Equal(2, juansWorkspaces.Count);
        Assert.Contains(juansWorkspaces, workspace => workspace.GetProperty("id").GetGuid() == workspaceId && workspace.GetProperty("role").GetString() == "member");
        var me = await juan.Client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Equal(workspaceId, me.GetProperty("preferences").GetProperty("defaultWorkspaceId").GetGuid());

        var members = (await brian.Client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{workspaceId}/members")).EnumerateArray().ToList();
        Assert.Equal(["Brian", "Juan"], members.Select(member => member.GetProperty("displayName").GetString()));
        Assert.Equal(["owner", "member"], members.Select(member => member.GetProperty("role").GetString()));

        var invite = await juan.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/invitations", new { email = Accounts.NewEmail(), role = "member" });
        var rename = await juan.Client.PatchAsJsonAsync($"/api/v1/workspaces/{workspaceId}", new { name = "De Juan", timeZone = "America/Lima" });
        var listInvitations = await juan.Client.GetAsync($"/api/v1/workspaces/{workspaceId}/invitations");
        Assert.Equal(HttpStatusCode.Forbidden, invite.StatusCode);
        Assert.Equal("workspace.insufficient_role", await Accounts.CodeOfAsync(invite));
        Assert.Equal(HttpStatusCode.Forbidden, rename.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, listInvitations.StatusCode);

        var joined = await api.ExecuteAsync(services => services.GetRequiredService<AppDbContext>().AuditLog.AsNoTracking()
            .SingleAsync(entry => entry.Action == "workspaces.member.joined" && entry.ResourceId == juan.UserId));
        Assert.Equal(workspaceId, joined.WorkspaceId);
    }

    [Fact]
    public async Task Onboarding_happens_once_and_only_with_a_supported_currency()
    {
        var brian = await api.RegisterAsync("Brian");
        var url = $"/api/v1/workspaces/{brian.PersonalWorkspaceId}/onboarding/complete";

        var unsupported = await brian.Client.PostAsJsonAsync(url, new { baseCurrency = "XXX", timeZone = "America/Lima" });
        var completed = await brian.Client.PostAsJsonAsync(url, new { baseCurrency = "PEN", timeZone = "America/Lima" });
        var again = await brian.Client.PostAsJsonAsync(url, new { baseCurrency = "USD", timeZone = "America/Lima" });

        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var workspace = await completed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("active", workspace.GetProperty("status").GetString());
        Assert.Equal("PEN", workspace.GetProperty("baseCurrency").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
        Assert.Equal("workspaces.onboarding_already_completed", await Accounts.CodeOfAsync(again));
    }

    [Fact]
    public async Task Someone_who_is_not_a_member_cannot_tell_that_the_workspace_exists()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var pedro = await api.RegisterAsync("Pedro");

        foreach (var path in new[] { "", "/members", "/invitations" })
        {
            var response = await pedro.Client.GetAsync($"/api/v1/workspaces/{brian.PersonalWorkspaceId}{path}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("workspace.not_found", await Accounts.CodeOfAsync(response));
        }
    }

    [Fact]
    public async Task An_existing_user_accepts_the_invitation_only_if_it_was_sent_to_their_email_and_only_once()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var juan = await api.RegisterAsync("Juan");
        var pedro = await api.RegisterAsync("Pedro");
        var token = await api.InviteAsync(brian, brian.PersonalWorkspaceId, juan.Email);

        var preview = await api.AnonymousClient().GetFromJsonAsync<JsonElement>($"/api/v1/invitations/{token}");
        Assert.Equal("Personal", preview.GetProperty("workspaceName").GetString());
        Assert.Equal("Brian", preview.GetProperty("invitedBy").GetString());
        Assert.True(preview.GetProperty("available").GetBoolean());

        var byPedro = await pedro.Client.PostAsync($"/api/v1/invitations/{token}/accept", null);
        var byJuan = await juan.Client.PostAsync($"/api/v1/invitations/{token}/accept", null);
        var again = await juan.Client.PostAsync($"/api/v1/invitations/{token}/accept", null);

        Assert.Equal(HttpStatusCode.Forbidden, byPedro.StatusCode);
        Assert.Equal("workspaces.invitation.other_recipient", await Accounts.CodeOfAsync(byPedro));
        Assert.Equal(HttpStatusCode.OK, byJuan.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await juan.Client.GetAsync($"/api/v1/workspaces/{brian.PersonalWorkspaceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await pedro.Client.GetAsync($"/api/v1/workspaces/{brian.PersonalWorkspaceId}")).StatusCode);
        Assert.False((await api.AnonymousClient().GetFromJsonAsync<JsonElement>($"/api/v1/invitations/{token}")).GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task Accepting_an_invitation_needs_a_session_and_an_unknown_link_is_not_found()
    {
        var anonymous = await api.BrowserClient().PostAsync("/api/v1/invitations/whatever/accept", null);
        var unknown = await api.AnonymousClient().GetAsync("/api/v1/invitations/no-such-token");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task A_revoked_invitation_disappears_from_the_list_and_stops_working()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var juan = await api.RegisterAsync("Juan");
        var workspace = $"/api/v1/workspaces/{brian.PersonalWorkspaceId}";
        var token = await api.InviteAsync(brian, brian.PersonalWorkspaceId, juan.Email);
        var invitation = Assert.Single((await brian.Client.GetFromJsonAsync<JsonElement>($"{workspace}/invitations")).EnumerateArray());

        var revoked = await brian.Client.DeleteAsync($"{workspace}/invitations/{invitation.GetProperty("id").GetGuid()}");

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Empty((await brian.Client.GetFromJsonAsync<JsonElement>($"{workspace}/invitations")).EnumerateArray());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await juan.Client.PostAsync($"/api/v1/invitations/{token}/accept", null)).StatusCode);
    }

    [Fact]
    public async Task An_email_is_not_invited_twice_a_member_is_not_invited_at_all_and_nobody_is_invited_as_owner()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var invitations = $"/api/v1/workspaces/{brian.PersonalWorkspaceId}/invitations";
        var email = Accounts.NewEmail("juan");
        await api.InviteAsync(brian, brian.PersonalWorkspaceId, email);

        var duplicate = await brian.Client.PostAsJsonAsync(invitations, new { email = email.ToUpperInvariant(), role = "member" });
        var alreadyMember = await brian.Client.PostAsJsonAsync(invitations, new { email = brian.Email, role = "member" });
        var asOwner = await brian.Client.PostAsJsonAsync(invitations, new { email = Accounts.NewEmail(), role = "owner" });

        Assert.Equal("workspaces.invitation.already_pending", await Accounts.CodeOfAsync(duplicate));
        Assert.Equal("workspaces.already_member", await Accounts.CodeOfAsync(alreadyMember));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, asOwner.StatusCode);
    }

    [Fact]
    public async Task An_admin_invites_members_but_only_the_owner_makes_admins()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspace = $"/api/v1/workspaces/{brian.PersonalWorkspaceId}";
        var juanEmail = Accounts.NewEmail("juan");
        var juan = await api.RegisterAsync("Juan", await api.InviteAsync(brian, brian.PersonalWorkspaceId, juanEmail), juanEmail);
        var pedroEmail = Accounts.NewEmail("pedro");
        var pedro = await api.RegisterAsync("Pedro", await api.InviteAsync(brian, brian.PersonalWorkspaceId, pedroEmail), pedroEmail);

        var promoteSelf = await juan.Client.PatchAsJsonAsync($"{workspace}/members/{juan.UserId}", new { role = "admin" });
        var promoted = await brian.Client.PatchAsJsonAsync($"{workspace}/members/{juan.UserId}", new { role = "admin" });
        Assert.Equal(HttpStatusCode.Forbidden, promoteSelf.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, promoted.StatusCode);

        var inviteMember = await juan.Client.PostAsJsonAsync($"{workspace}/invitations", new { email = Accounts.NewEmail(), role = "member" });
        var inviteAdmin = await juan.Client.PostAsJsonAsync($"{workspace}/invitations", new { email = Accounts.NewEmail(), role = "admin" });
        var promotePedro = await juan.Client.PatchAsJsonAsync($"{workspace}/members/{pedro.UserId}", new { role = "admin" });
        var demoteOwner = await juan.Client.PatchAsJsonAsync($"{workspace}/members/{brian.UserId}", new { role = "member" });
        Assert.Equal(HttpStatusCode.Created, inviteMember.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, inviteAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, promotePedro.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, demoteOwner.StatusCode);
    }

    [Fact]
    public async Task A_removed_member_loses_access_at_once_and_a_member_can_leave_but_the_owner_cannot()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspace = $"/api/v1/workspaces/{brian.PersonalWorkspaceId}";
        var juanEmail = Accounts.NewEmail("juan");
        var juan = await api.RegisterAsync("Juan", await api.InviteAsync(brian, brian.PersonalWorkspaceId, juanEmail), juanEmail);
        var pedroEmail = Accounts.NewEmail("pedro");
        var pedro = await api.RegisterAsync("Pedro", await api.InviteAsync(brian, brian.PersonalWorkspaceId, pedroEmail), pedroEmail);

        Assert.Equal(HttpStatusCode.Forbidden, (await juan.Client.DeleteAsync($"{workspace}/members/{pedro.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await brian.Client.DeleteAsync($"{workspace}/members/{juan.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await juan.Client.GetAsync(workspace)).StatusCode);
        Assert.Single((await juan.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/workspaces")).EnumerateArray());

        Assert.Equal(HttpStatusCode.NoContent, (await pedro.Client.DeleteAsync($"{workspace}/members/{pedro.UserId}")).StatusCode);
        var ownerLeaves = await brian.Client.DeleteAsync($"{workspace}/members/{brian.UserId}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, ownerLeaves.StatusCode);
        Assert.Equal("workspaces.owner_cannot_leave", await Accounts.CodeOfAsync(ownerLeaves));
        Assert.Single((await brian.Client.GetFromJsonAsync<JsonElement>($"{workspace}/members")).EnumerateArray());
    }

    [Fact]
    public async Task Ownership_moves_to_another_member_and_the_former_owner_becomes_admin()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspace = $"/api/v1/workspaces/{brian.PersonalWorkspaceId}";
        var juanEmail = Accounts.NewEmail("juan");
        var juan = await api.RegisterAsync("Juan", await api.InviteAsync(brian, brian.PersonalWorkspaceId, juanEmail), juanEmail);

        var byMember = await juan.Client.PostAsJsonAsync($"{workspace}/ownership-transfer", new { userId = juan.UserId });
        var byOwner = await brian.Client.PostAsJsonAsync($"{workspace}/ownership-transfer", new { userId = juan.UserId });

        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, byOwner.StatusCode);
        Assert.Equal("owner", (await juan.Client.GetFromJsonAsync<JsonElement>(workspace)).GetProperty("role").GetString());
        Assert.Equal("admin", (await brian.Client.GetFromJsonAsync<JsonElement>(workspace)).GetProperty("role").GetString());
    }

    [Fact]
    public async Task A_person_can_create_more_workspaces_and_rename_the_ones_they_administer()
    {
        var brian = await api.RegisterAsync("Brian");

        var created = await brian.Client.PostAsJsonAsync("/api/v1/workspaces", new { name = "Hogar Quispe" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var renamed = await brian.Client.PatchAsJsonAsync($"/api/v1/workspaces/{id}", new { name = "Casa", timeZone = "America/Bogota" });
        var badZone = await brian.Client.PatchAsJsonAsync($"/api/v1/workspaces/{id}", new { name = "Casa", timeZone = "Mars/Olympus" });

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badZone.StatusCode);
        var names = (await brian.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/workspaces")).EnumerateArray().Select(workspace => workspace.GetProperty("name").GetString());
        Assert.Equal(["Personal", "Casa"], names);
    }

    [Fact]
    public async Task Queued_emails_are_delivered_once_and_their_body_is_erased()
    {
        var brian = await api.RegisterAsync("Brian");

        // Other tests queue emails too and a pass sends one batch, so drain the queue.
        var sent = 0;
        while (await api.ExecuteAsync(services => services.GetRequiredService<IEmailDispatcher>().SendPendingAsync(CancellationToken.None)) is > 0 and var batch)
        {
            sent += batch;
        }

        var sentAgain = await api.ExecuteAsync(services => services.GetRequiredService<IEmailDispatcher>().SendPendingAsync(CancellationToken.None));

        Assert.True(sent >= 1);
        Assert.Equal(0, sentAgain);
        var email = await api.ExecuteAsync(services => services.GetRequiredService<AppDbContext>().OutgoingEmails.AsNoTracking().SingleAsync(row => row.To == brian.Email));
        Assert.NotNull(email.SentAt);
        Assert.Null(email.Body);
        Assert.Contains(Directory.EnumerateFiles(api.MailDirectory, "*.eml"), file => File.ReadAllText(file).Contains(brian.Email));
    }
}
