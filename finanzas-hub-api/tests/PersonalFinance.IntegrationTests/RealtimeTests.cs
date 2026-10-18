using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using PersonalFinance.IntegrationTests.Support;

namespace PersonalFinance.IntegrationTests;

/// <summary>The realtime hub: a signal reaches the person it is for and nobody else, not even someone of the same workspace.</summary>
[Collection(ApiCollection.Name)]
public class RealtimeTests(ApiFixture api)
{
    [Fact]
    public async Task A_notice_for_Juan_reaches_his_browser_and_never_that_of_Pedro_in_the_same_workspace()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;
        var (juanEmail, pedroEmail) = (Accounts.NewEmail("juan"), Accounts.NewEmail("pedro"));
        var juan = await api.RegisterAsync("Juan", invitationToken: await api.InviteAsync(brian, workspaceId, juanEmail), email: juanEmail);
        var pedro = await api.RegisterAsync("Pedro", invitationToken: await api.InviteAsync(brian, workspaceId, pedroEmail), email: pedroEmail);
        var bcp = (await brian.OpenAccountAsync("BCP", 1000m)).Id();
        var created = await brian.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/goals", new { name = "Regalo", targetAmount = 300m, participantUserIds = new[] { juan.UserId } });
        var goalId = (await created.Content.ReadFromJsonAsync<JsonElement>()).Id();

        await using var juansBrowser = api.HubFor(juan.UserId);
        await using var pedrosBrowser = api.HubFor(pedro.UserId);
        var toJuan = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var toPedro = new List<string>();
        juansBrowser.On<JsonElement>("notification.created", notice => toJuan.TrySetResult(notice.GetProperty("title").GetString()!));
        pedrosBrowser.On<JsonElement>("notification.created", notice => { lock (toPedro) { toPedro.Add(notice.GetProperty("title").GetString()!); } });
        pedrosBrowser.On<JsonElement>("resource.changed", _ => { lock (toPedro) { toPedro.Add("resource.changed"); } });
        await juansBrowser.StartAsync();
        await pedrosBrowser.StartAsync();

        var contributed = await brian.Client.PostWithKeyAsync($"/api/v1/workspaces/{workspaceId}/goals/{goalId}/contributions", new { amount = 100m, accountId = bcp });
        Assert.Equal(HttpStatusCode.Created, contributed.StatusCode);

        Assert.Equal("Brian aportó a «Regalo»", await toJuan.Task.WaitAsync(TimeSpan.FromSeconds(15)));

        // Long polling brings what was queued at the latest on the next poll; by then Pedro would have had it too.
        await Task.Delay(TimeSpan.FromSeconds(2));
        lock (toPedro)
        {
            Assert.Empty(toPedro);
        }
    }
}
