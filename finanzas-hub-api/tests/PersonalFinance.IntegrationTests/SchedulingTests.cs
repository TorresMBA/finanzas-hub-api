using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.Infrastructure.Security;
using PersonalFinance.IntegrationTests.Support;
using PersonalFinance.Modules.Scheduling.Application;

namespace PersonalFinance.IntegrationTests;

/// <summary>Scheduled and recurring movements: what the calendar shows, what the cycle registers and who sees it.</summary>
[Collection(ApiCollection.Name)]
public class SchedulingTests(ApiFixture api)
{
    private static string Url(Guid workspaceId, string path) => $"/api/v1/workspaces/{workspaceId}/{path}";

    private static async Task<JsonElement> CalendarAsync(TestAccount person, DateOnly from, DateOnly to, Guid? workspaceId = null) =>
        await person.Client.GetFromJsonAsync<JsonElement>(Url(workspaceId ?? person.PersonalWorkspaceId, $"calendar?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"));

    private static JsonElement? Item(JsonElement calendar, string description) =>
        calendar.GetProperty("items").EnumerateArray().Cast<JsonElement?>().SingleOrDefault(item => item!.Value.GetProperty("description").GetString() == description);

    private static async Task<JsonElement> ScheduleAsync(
        TestAccount person, Guid accountId, string description, decimal amount, string frequency, DateOnly startsOn, string postingMode, int? reminderDaysBefore = null)
    {
        var response = await person.Client.PostAsJsonAsync(Url(person.PersonalWorkspaceId, "recurring-rules"), new
        {
            type = "expense", accountId, categoryId = await person.CategoryIdAsync("Vivienda"), amount, description, frequency,
            startsOn = startsOn.ToString("yyyy-MM-dd"), postingMode, reminderDaysBefore
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>What the Worker does for one person: finds them among the owners, acts as them and runs what is due.</summary>
    private Task<RunScheduleResponse> RunCycleAsync(TestAccount person) => api.ExecuteAsync(async services =>
    {
        var owners = await services.GetRequiredService<IQueryHandler<ListScheduleOwnersQuery, IReadOnlyList<ScheduleOwner>>>()
            .HandleAsync(new ListScheduleOwnersQuery(), CancellationToken.None);
        var owner = owners.Value.Single(candidate => candidate.UserId == person.UserId && candidate.WorkspaceId == person.PersonalWorkspaceId);
        var actor = services.GetRequiredService<IActorContextInitializer>();
        actor.ActAs(owner.UserId, ActorType.System, "Worker");
        actor.EnterWorkspace(new WorkspaceMembershipInfo(owner.WorkspaceId, owner.UserId, owner.Role, owner.BaseCurrency, owner.TimeZone));
        var result = await services.GetRequiredService<ICommandHandler<RunScheduleCommand, RunScheduleResponse>>()
            .HandleAsync(new RunScheduleCommand(), CancellationToken.None);
        return result.Value;
    });

    [Fact]
    public async Task A_monthly_rule_shows_in_the_months_to_come_is_registered_once_on_its_day_and_reminds_the_day_before()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;
        var juanEmail = Accounts.NewEmail("juan");
        var juan = await api.RegisterAsync("Juan", invitationToken: await api.InviteAsync(brian, workspaceId, juanEmail), email: juanEmail);
        var bcp = await brian.OpenAccountAsync("BCP", 1000m);

        // The day it is for the workspace, which is not always the day it is in UTC.
        var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
        var today = (await CalendarAsync(brian, utcToday.AddDays(-1), utcToday.AddDays(1))).GetProperty("today").GetDateTime() is var date
            ? DateOnly.FromDateTime(date)
            : utcToday;

        var rent = await ScheduleAsync(brian, bcp.Id(), "Alquiler", 300m, "monthly", today, "automatic");
        var gym = await ScheduleAsync(brian, bcp.Id(), "Gimnasio", 50m, "once", today.AddDays(1), "confirm", reminderDaysBefore: 1);
        var insurance = await ScheduleAsync(brian, bcp.Id(), "Seguro", 80m, "once", today.AddDays(-3), "automatic");

        var nextMonth = Item(await CalendarAsync(brian, today.AddMonths(1).AddDays(-1), today.AddMonths(1).AddDays(1)), "Alquiler");
        Assert.Equal(("scheduled", true, 300m), (
            nextMonth!.Value.GetProperty("status").GetString(), nextMonth.Value.GetProperty("isRecurring").GetBoolean(), nextMonth.Value.Money("amount")));

        // The cycle runs twice, as when two processes overlap: the rent is registered once and the reminder said once.
        Assert.Equal(new RunScheduleResponse(1, 0, 1), await RunCycleAsync(brian));
        Assert.Equal(new RunScheduleResponse(0, 0, 0), await RunCycleAsync(brian));
        Assert.Equal(700m, await brian.BalanceAsync(bcp.Id()));
        Assert.Equal(["Alquiler"], (await brian.TransactionsAsync("?type=expense")).Descriptions());
        var notices = (await brian.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(("bills", "Pago programado: Gimnasio"), (Assert.Single(notices).GetProperty("category").GetString(), notices[0].GetProperty("title").GetString()));

        // What was due before today is not made up for: it waits for Brian, and counts in what is still to be paid.
        var calendar = await CalendarAsync(brian, today.AddDays(-3), today.AddDays(1));
        Assert.Equal(("confirmed", "pending", "scheduled"), (
            Item(calendar, "Alquiler")!.Value.GetProperty("status").GetString(), Item(calendar, "Seguro")!.Value.GetProperty("status").GetString(),
            Item(calendar, "Gimnasio")!.Value.GetProperty("status").GetString()));
        Assert.Equal((130m, 570m), (calendar.Money("pendingExpenses"), calendar.Money("projectedBalance")));
        Assert.Contains((await brian.DashboardAsync(today.ToString("yyyy-MM"))).GetProperty("upcoming").EnumerateArray(), payment => payment.GetProperty("description").GetString() == "Gimnasio");

        // Confirming registers it with the real amount; asking again registers nothing more.
        var confirmUrl = Url(workspaceId, $"recurring-rules/{insurance.Id()}/occurrences/{today.AddDays(-3):yyyy-MM-dd}/confirm");
        Assert.Equal(HttpStatusCode.NoContent, (await brian.Client.PostAsJsonAsync(confirmUrl, new { amount = 85m })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await brian.Client.PostAsJsonAsync(confirmUrl, new { amount = 85m })).StatusCode);
        Assert.Equal(615m, await brian.BalanceAsync(bcp.Id()));

        var gymUrl = Url(workspaceId, $"recurring-rules/{gym.Id()}/occurrences/{today.AddDays(1):yyyy-MM-dd}");
        Assert.Equal("scheduling.occurrence.in_future", await Accounts.CodeOfAsync(await brian.Client.PostAsJsonAsync($"{gymUrl}/confirm", new { })));
        Assert.Equal(HttpStatusCode.NoContent, (await brian.Client.PostAsync($"{gymUrl}/skip", null)).StatusCode);
        Assert.Null(Item(await CalendarAsync(brian, today, today.AddDays(1)), "Gimnasio"));

        // Another member of the workspace sees none of it and cannot touch it.
        Assert.Empty((await juan.Client.GetFromJsonAsync<JsonElement>(Url(workspaceId, "recurring-rules"))).EnumerateArray());
        Assert.Empty((await CalendarAsync(juan, today.AddDays(-3), today.AddMonths(1), workspaceId)).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await juan.Client.PostAsync(Url(workspaceId, $"recurring-rules/{rent.Id()}/end"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await juan.Client.PostAsync(Url(workspaceId, $"recurring-rules/{rent.Id()}/occurrences/{today.AddMonths(1):yyyy-MM-dd}/skip"), null)).StatusCode);
        Assert.Equal(new RunScheduleResponse(0, 0, 0), await api.ExecuteAsync(async services =>
        {
            // Juan's own cycle in the same workspace finds nothing of Brian's to run.
            var owners = await services.GetRequiredService<IQueryHandler<ListScheduleOwnersQuery, IReadOnlyList<ScheduleOwner>>>().HandleAsync(new ListScheduleOwnersQuery(), CancellationToken.None);
            var owner = owners.Value.Single(candidate => candidate.UserId == juan.UserId && candidate.WorkspaceId == workspaceId);
            var actor = services.GetRequiredService<IActorContextInitializer>();
            actor.ActAs(owner.UserId, ActorType.System, "Worker");
            actor.EnterWorkspace(new WorkspaceMembershipInfo(owner.WorkspaceId, owner.UserId, owner.Role, owner.BaseCurrency, owner.TimeZone));
            return (await services.GetRequiredService<ICommandHandler<RunScheduleCommand, RunScheduleResponse>>().HandleAsync(new RunScheduleCommand(), CancellationToken.None)).Value;
        }));
    }
}
