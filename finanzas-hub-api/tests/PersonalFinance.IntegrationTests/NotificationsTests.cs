using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Infrastructure.Persistence;
using PersonalFinance.IntegrationTests.Support;
using PersonalFinance.Modules.Notifications.Application;
using PersonalFinance.Modules.Notifications.Contracts;
using PersonalFinance.Modules.Notifications.Domain;

namespace PersonalFinance.IntegrationTests;

/// <summary>The inbox: what puts a notice in it, that it is said once, where else it goes and who reads it.</summary>
[Collection(ApiCollection.Name)]
public class NotificationsTests(ApiFixture api)
{
    private const string Inbox = "/api/v1/me/notifications";
    private const string Settings = "/api/v1/me/notification-settings";

    // Budgets start in the real current month, so the movements of this test are dated on its first day.
    private static readonly string Day = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).ToString("yyyy-MM-dd");

    private static async Task<List<JsonElement>> NoticesAsync(TestAccount person, string query = "") =>
        (await person.Client.GetFromJsonAsync<JsonElement>(Inbox + query)).GetProperty("items").EnumerateArray().ToList();

    private static async Task<int> UnreadAsync(TestAccount person) =>
        (await person.Client.GetFromJsonAsync<JsonElement>($"{Inbox}/unread-count")).GetProperty("count").GetInt32();

    [Fact]
    public async Task Crossing_the_threshold_of_a_budget_puts_one_notice_in_the_inbox_of_its_owner_and_an_email_only_if_they_want_it()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;
        var juanEmail = Accounts.NewEmail("juan");
        var juan = await api.RegisterAsync("Juan", invitationToken: await api.InviteAsync(brian, workspaceId, juanEmail), email: juanEmail);
        var bcp = await brian.OpenAccountAsync("BCP", 5000m);
        var created = await brian.Client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/budgets", new
        {
            categoryId = await brian.CategoryIdAsync("Supermercado"), amount = 1000m, alertThresholdPercent = 90
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var emailsBefore = await api.QueuedEmailCountAsync(brian.Email);

        // 30 %: under the warning of the budget and under the 500 that make a movement large.
        await brian.SpendAsync(bcp.Id(), 300m, occurredOn: Day);
        Assert.Empty(await NoticesAsync(brian));

        // 91 %: the budget warns, by email too, and 610 is a large movement, which by default stays in the inbox.
        await brian.SpendAsync(bcp.Id(), 610m, description: "Wong", occurredOn: Day);
        var notices = await NoticesAsync(brian);
        Assert.Equal(["budget", "largeMovement"], notices.Select(notice => notice.GetProperty("category").GetString()).Order());
        var warning = notices.Single(notice => notice.GetProperty("category").GetString() == "budget");
        Assert.Equal(("Supermercado al 91 %", "warning", "/budgets", false), (
            warning.GetProperty("title").GetString(), warning.GetProperty("severity").GetString(), warning.GetProperty("link").GetString(),
            warning.GetProperty("isRead").GetBoolean()));
        Assert.Equal(2, await UnreadAsync(brian));
        Assert.Equal(emailsBefore + 1, await api.QueuedEmailCountAsync(brian.Email));

        // Still over the warning: nothing is said twice.
        await brian.SpendAsync(bcp.Id(), 20m, occurredOn: Day);
        Assert.Equal(2, (await NoticesAsync(brian)).Count);

        // With the email of budgets switched off, passing the limit is told in the inbox only.
        var settings = await brian.Client.PutAsJsonAsync(Settings, new
        {
            quietHoursEnabled = false, quietFrom = "22:00", quietTo = "07:00", largeMovementThreshold = (decimal?)null,
            preferences = new[] { new { category = "budget", emailEnabled = false } }
        });
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        await brian.SpendAsync(bcp.Id(), 900m, occurredOn: Day);
        notices = await NoticesAsync(brian);
        Assert.Equal(("Supermercado excedido", "critical"), (notices[0].GetProperty("title").GetString(), notices[0].GetProperty("severity").GetString()));
        Assert.Equal(3, notices.Count);
        Assert.Equal(emailsBefore + 1, await api.QueuedEmailCountAsync(brian.Email));

        // The inbox belongs to one person: another member of the workspace neither sees it nor marks it.
        Assert.Empty(await NoticesAsync(juan));
        Assert.Equal(0, await UnreadAsync(juan));
        var warningUrl = $"{Inbox}/{warning.Id()}/read";
        Assert.Equal(HttpStatusCode.NotFound, (await juan.Client.PostAsync(warningUrl, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await juan.Client.PostAsync($"{Inbox}/read-all", null)).StatusCode);
        Assert.Equal(3, await UnreadAsync(brian));

        Assert.Equal(HttpStatusCode.NoContent, (await brian.Client.PostAsync(warningUrl, null)).StatusCode);
        Assert.Equal(2, (await NoticesAsync(brian, "?unreadOnly=true")).Count);
        Assert.Equal(HttpStatusCode.NoContent, (await brian.Client.PostAsync($"{Inbox}/read-all", null)).StatusCode);
        Assert.Equal(0, await UnreadAsync(brian));
    }

    [Fact]
    public async Task The_purge_removes_only_what_is_old_and_never_a_message_still_waiting_to_go_out()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var old = now.AddDays(-100);
        var request = new NotificationRequest(userId, null, NotificationCategory.Shared, NotificationSeverity.Info, "Aviso", "Texto", null, null);
        var sentLongAgo = Delivery.WhatsApp(userId, "51900000000", DeliveryKind.Notice, "Hola", [], old);
        sentLongAgo.MarkSent("wamid.old", old);
        var waitingLongAgo = Delivery.WhatsApp(userId, "51900000000", DeliveryKind.Notice, "Hola", [], old);
        var eventIds = (Old: $"purge-old-{userId:N}", Recent: $"purge-new-{userId:N}");
        await api.ExecuteAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            db.Notifications.AddRange(Notification.Create(request, old), Notification.Create(request, now));
            db.Deliveries.AddRange(sentLongAgo, waitingLongAgo);
            db.ReceivedWebhookEvents.AddRange(ReceivedWebhookEvent.Status(eventIds.Old, now.AddDays(-40)), ReceivedWebhookEvent.Status(eventIds.Recent, now.AddDays(-1)));
            return await db.SaveChangesAsync();
        });

        var purged = await api.ExecuteAsync(services => services
            .GetRequiredService<ICommandHandler<PurgeNotificationHistoryCommand, PurgeNotificationHistoryResponse>>()
            .HandleAsync(new PurgeNotificationHistoryCommand(), CancellationToken.None));

        Assert.True(purged.IsSuccess);
        var left = await api.ExecuteAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            return (
                Notices: await db.Notifications.Where(notification => notification.UserId == userId).Select(notification => notification.CreatedAt).ToListAsync(),
                Deliveries: await db.Deliveries.Where(delivery => delivery.UserId == userId).Select(delivery => delivery.Status).ToListAsync(),
                Events: await db.ReceivedWebhookEvents.Where(received => received.ExternalId.Contains(userId.ToString("N"))).Select(received => received.ExternalId).ToListAsync());
        });
        Assert.True(Assert.Single(left.Notices) > now.AddDays(-1));
        Assert.Equal(DeliveryStatus.Pending, Assert.Single(left.Deliveries));
        Assert.StartsWith(eventIds.Recent, Assert.Single(left.Events));
    }

    [Fact]
    public async Task A_category_taken_out_of_the_inbox_no_longer_shows_there_and_is_not_said_twice_when_it_comes_back()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var bcp = await brian.OpenAccountAsync("BCP", 5000m);
        var off = await brian.Client.PutAsJsonAsync(Settings, new
        {
            quietHoursEnabled = false, quietFrom = "22:00", quietTo = "07:00", largeMovementThreshold = 500m,
            preferences = new[] { new { category = "largeMovement", emailEnabled = false, inboxEnabled = false } }
        });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);

        // 610 is a large movement: the notice is kept, already read, so it shows nowhere and the badge stays at zero.
        await brian.SpendAsync(bcp.Id(), 610m, description: "Wong", occurredOn: Day);
        Assert.Empty(await NoticesAsync(brian));
        Assert.Equal(0, await UnreadAsync(brian));
        var kept = await api.ExecuteAsync(services => services.GetRequiredService<AppDbContext>().Set<Notification>()
            .Where(notice => notice.UserId == brian.UserId).Select(notice => notice.ReadAt).ToListAsync());
        Assert.NotNull(Assert.Single(kept));

        // Back in the inbox, it shows read, and a filter by another category leaves it out.
        await brian.Client.PutAsJsonAsync(Settings, new
        {
            quietHoursEnabled = false, quietFrom = "22:00", quietTo = "07:00", largeMovementThreshold = 500m,
            preferences = new[] { new { category = "largeMovement", emailEnabled = false, inboxEnabled = true } }
        });
        Assert.True(Assert.Single(await NoticesAsync(brian)).GetProperty("isRead").GetBoolean());
        Assert.Empty(await NoticesAsync(brian, "?category=budget"));
    }
}
