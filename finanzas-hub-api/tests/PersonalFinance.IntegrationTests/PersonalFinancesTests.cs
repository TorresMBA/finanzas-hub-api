using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.IntegrationTests.Support;
using PersonalFinance.Modules.Scheduling.Application;

namespace PersonalFinance.IntegrationTests;

/// <summary>Private finances belong to the person: they are the same whichever workspace is open, and nobody else's.</summary>
[Collection(ApiCollection.Name)]
public class PersonalFinancesTests(ApiFixture api)
{
    private static async Task<List<string?>> AccountNamesAsync(TestAccount person, Guid workspaceId) =>
        (await person.Client.GetFromJsonAsync<JsonElement>(Books.AccountsUrl(workspaceId))).EnumerateArray()
            .Select(account => account.GetProperty("name").GetString()).ToList();

    [Fact]
    public async Task Someone_invited_to_another_workspace_finds_their_own_accounts_there_and_contributes_to_its_goal_from_them()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var juan = await api.RegisterWithActiveWorkspaceAsync("Juan");
        var briansHub = brian.PersonalWorkspaceId;
        await brian.OpenAccountAsync("BCP", 2000m);
        var interbank = await juan.OpenAccountAsync("Interbank", 1000m);

        var token = await api.InviteAsync(brian, briansHub, juan.Email);
        Assert.Equal(HttpStatusCode.OK, (await juan.Client.PostAsync($"/api/v1/invitations/{token}/accept", null)).StatusCode);

        // Looking at Brian's workspace, Juan has his own books, not an empty set, and Brian's are still only Brian's.
        Assert.Equal(["Interbank"], await AccountNamesAsync(juan, briansHub));
        Assert.Equal(["Interbank"], await AccountNamesAsync(juan, juan.PersonalWorkspaceId));
        Assert.Equal(["BCP"], await AccountNamesAsync(brian, briansHub));
        Assert.Equal(HttpStatusCode.NotFound, (await brian.Client.GetAsync($"{Books.AccountsUrl(briansHub)}/{interbank.Id()}")).StatusCode);
        Assert.NotEmpty((await juan.Client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{briansHub}/categories")).EnumerateArray());

        // The goal is of Brian's workspace; Juan puts into it from the account he always had.
        var goals = $"/api/v1/workspaces/{briansHub}/goals";
        var created = await brian.Client.PostAsJsonAsync(goals, new { name = "Viaje Japón", targetAmount = 1000m, participantUserIds = new[] { juan.UserId } });
        var goalId = (await created.Content.ReadFromJsonAsync<JsonElement>()).Id();
        var contributed = await juan.Client.PostWithKeyAsync($"{goals}/{goalId}/contributions", new { amount = 200m, accountId = interbank.Id() });
        Assert.Equal(HttpStatusCode.Created, contributed.StatusCode);
        Assert.Equal(800m, await juan.BalanceAsync(interbank.Id()));
        Assert.Equal(200m, (await brian.Client.GetFromJsonAsync<JsonElement>($"{goals}/{goalId}")).GetProperty("goal").Money("saved"));

        // From his own workspace the goal does not exist: what is shared stays in the workspace it was created in.
        Assert.Empty((await juan.Client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{juan.PersonalWorkspaceId}/goals")).GetProperty("goals").EnumerateArray());
        Assert.Contains("Meta: Viaje Japón", await AccountNamesAsync(juan, juan.PersonalWorkspaceId));
    }

    [Fact]
    public async Task A_business_keeps_its_own_books_that_only_its_owner_sees_and_admits_no_one_else()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var juan = await api.RegisterWithActiveWorkspaceAsync("Juan");
        await brian.OpenAccountAsync("BCP", 2000m);
        Assert.Equal(HttpStatusCode.OK, (await juan.Client.PostAsync($"/api/v1/invitations/{await api.InviteAsync(brian, brian.PersonalWorkspaceId, juan.Email)}/accept", null)).StatusCode);

        var created = await brian.Client.PostAsJsonAsync("/api/v1/workspaces", new { name = "Bodega", kind = "business" });
        var business = (await created.Content.ReadFromJsonAsync<JsonElement>()).Id();
        Assert.Equal(HttpStatusCode.OK, (await brian.Client.PostAsJsonAsync($"/api/v1/workspaces/{business}/onboarding/complete", new { baseCurrency = "USD", timeZone = "America/Lima" })).StatusCode);
        await brian.OpenAccountAsync("Caja", 500m, currency: "USD", workspaceId: business);

        // Two sets of books: what is registered in the business is not in the personal ones, nor the other way round.
        Assert.Equal(["Caja"], await AccountNamesAsync(brian, business));
        Assert.Equal(["BCP"], await AccountNamesAsync(brian, brian.PersonalWorkspaceId));
        Assert.NotEqual(default, await brian.CategoryIdAsync("Ventas", business));

        // Nobody else gets in, not even someone who shares Brian's personal workspace.
        Assert.Equal(HttpStatusCode.NotFound, (await juan.Client.GetAsync(Books.AccountsUrl(business))).StatusCode);
        var goal = await brian.Client.PostAsJsonAsync($"/api/v1/workspaces/{business}/goals", new { name = "Local nuevo", targetAmount = 1000m });
        var invitation = await brian.Client.PostAsJsonAsync($"/api/v1/workspaces/{business}/invitations", new { email = juan.Email, role = "member" });
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "workspaces.kind_not_shareable"), (goal.StatusCode, await Accounts.CodeOfAsync(goal)));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "workspaces.kind_not_shareable"), (invitation.StatusCode, await Accounts.CodeOfAsync(invitation)));

        // It has no goals or shared expenses at all: not even an empty list.
        var group = await brian.Client.PostAsJsonAsync($"/api/v1/workspaces/{business}/shared-groups", new { name = "Socios" });
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "workspaces.kind_not_shareable"), (group.StatusCode, await Accounts.CodeOfAsync(group)));
        foreach (var section in new[] { "goals", "shared-groups" })
        {
            var listed = await brian.Client.GetAsync($"/api/v1/workspaces/{business}/{section}");
            Assert.Equal((HttpStatusCode.UnprocessableEntity, "workspaces.kind_not_shareable"), (listed.StatusCode, await Accounts.CodeOfAsync(listed)));
        }

        // The scheduled cycle runs on each set of books of Brian, and only once on Juan's.
        var owners = await api.ExecuteAsync(async services => (await services.GetRequiredService<IQueryHandler<ListScheduleOwnersQuery, IReadOnlyList<ScheduleOwner>>>()
            .HandleAsync(new ListScheduleOwnersQuery(), CancellationToken.None)).Value);
        Assert.Equal(
            new HashSet<Guid> { brian.PersonalWorkspaceId, business },
            owners.Where(owner => owner.UserId == brian.UserId).Select(owner => owner.WorkspaceId).ToHashSet());
        Assert.Equal([juan.PersonalWorkspaceId], owners.Where(owner => owner.UserId == juan.UserId).Select(owner => owner.WorkspaceId));
    }

    [Fact]
    public async Task Someone_who_registers_from_an_invitation_gets_a_personal_workspace_ready_to_use()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var pedroEmail = Accounts.NewEmail("pedro");
        var pedro = await api.RegisterAsync("Pedro", invitationToken: await api.InviteAsync(brian, brian.PersonalWorkspaceId, pedroEmail), email: pedroEmail);

        // It takes the currency and the time zone of the workspace that invited him, and comes with the suggested categories.
        var mine = (await pedro.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/workspaces")).EnumerateArray()
            .Single(workspace => workspace.GetProperty("id").GetGuid() == pedro.PersonalWorkspaceId);
        Assert.Equal((true, "active", "PEN", "America/Lima"), (
            mine.GetProperty("isPersonal").GetBoolean(), mine.GetProperty("status").GetString(), mine.GetProperty("baseCurrency").GetString(),
            mine.GetProperty("timeZone").GetString()));

        // His first account is his, wherever he registers it from.
        var opened = await pedro.OpenAccountAsync("BBVA", 100m, workspaceId: brian.PersonalWorkspaceId);
        Assert.Equal(["BBVA"], await AccountNamesAsync(pedro, pedro.PersonalWorkspaceId));
        Assert.Equal(HttpStatusCode.Created, (await pedro.PostTransactionAsync(opened.Id(), "expense", 10m, "Supermercado", workspaceId: brian.PersonalWorkspaceId)).StatusCode);
        Assert.DoesNotContain("BBVA", await AccountNamesAsync(brian, brian.PersonalWorkspaceId));
    }
}
