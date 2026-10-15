using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Infrastructure.Persistence;
using PersonalFinance.Infrastructure.WhatsApp;
using PersonalFinance.IntegrationTests.Support;
using PersonalFinance.Modules.Notifications.Domain;

namespace PersonalFinance.IntegrationTests;

/// <summary>The WhatsApp channel: a number counts once proven with a code, notices go to it, and it can ask for balances.</summary>
[Collection(ApiCollection.Name)]
public partial class WhatsAppTests(ApiFixture api)
{
    private const string Channel = "/api/v1/me/channels/whatsapp";
    private const string Settings = "/api/v1/me/notification-settings";
    private const string Webhook = "/webhooks/v1/whatsapp";

    private static string NewPhone() => "519" + RandomNumberGenerator.GetInt32(10_000_000, 99_999_999);

    private Task<List<Delivery>> DeliveriesAsync(Guid userId) =>
        api.ExecuteAsync(services => services.GetRequiredService<AppDbContext>().Deliveries.AsNoTracking()
            .Where(delivery => delivery.UserId == userId).OrderBy(delivery => delivery.CreatedAt).ToListAsync());

    private async Task<TestAccount> VerifiedAsync(string name, string phone)
    {
        var person = await api.RegisterWithActiveWorkspaceAsync(name);
        Assert.Equal(HttpStatusCode.OK, (await person.Client.PostAsJsonAsync($"{Channel}/verify-start", new { phone = "+" + phone })).StatusCode);
        var code = SixDigits().Match((await DeliveriesAsync(person.UserId)).Last().Text!).Value;
        Assert.Equal(HttpStatusCode.OK, (await person.Client.PostAsJsonAsync($"{Channel}/verify-confirm", new { code })).StatusCode);
        return person;
    }

    private static HttpRequestMessage Event(object payload, string secret = ApiFixture.WhatsAppSecret)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, Webhook) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)));
        return request;
    }

    private static object Payload(object value) => new { @object = "whatsapp_business_account", entry = new[] { new { changes = new[] { new { field = "messages", value } } } } };

    private static object TextFrom(string phone, string text) =>
        Payload(new { messages = new[] { new { from = phone, id = "wamid." + Guid.NewGuid().ToString("N"), type = "text", text = new { body = text } } } });

    [Fact]
    public async Task A_number_counts_only_with_its_code_and_then_receives_the_notices_the_person_chose()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var phone = NewPhone();

        // Nothing can go to WhatsApp before a number is proven.
        var early = await brian.Client.PutAsJsonAsync(Settings, new
        {
            quietHoursEnabled = false, quietFrom = "22:00", quietTo = "07:00", largeMovementThreshold = 500m,
            preferences = new[] { new { category = "largeMovement", emailEnabled = false, whatsAppEnabled = true } }
        });
        Assert.Equal("notifications.whatsapp_not_verified", await Accounts.CodeOfAsync(early));
        Assert.Equal("notifications.phone_invalid", await Accounts.CodeOfAsync(await brian.Client.PostAsJsonAsync($"{Channel}/verify-start", new { phone = "987 654 321" })));

        var started = await brian.Client.PostAsJsonAsync($"{Channel}/verify-start", new { phone = $"+{phone[..2]} {phone[2..]}" });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var verification = await started.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, verification.GetProperty("developmentCode").ValueKind);
        var codeMessage = Assert.Single(await DeliveriesAsync(brian.UserId));
        Assert.Equal((DeliveryKind.VerificationCode, phone), (codeMessage.Kind, codeMessage.To));
        var code = SixDigits().Match(codeMessage.Text!).Value;

        // A wrong code is counted even though it is rejected.
        var wrong = await brian.Client.PostAsJsonAsync($"{Channel}/verify-confirm", new { code = code == "000000" ? "111111" : "000000" });
        Assert.Equal("notifications.code_invalid", await Accounts.CodeOfAsync(wrong));
        Assert.Equal(1, await api.ExecuteAsync(services => services.GetRequiredService<AppDbContext>().ChannelEndpoints.AsNoTracking()
            .Where(endpoint => endpoint.UserId == brian.UserId).Select(endpoint => endpoint.Attempts).SingleAsync()));

        var confirmed = await brian.Client.PostAsJsonAsync($"{Channel}/verify-confirm", new { code });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal("+" + phone, (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("phone").GetString());

        // The number is his now: nobody else can prove it.
        var juan = await api.RegisterWithActiveWorkspaceAsync("Juan");
        Assert.Equal("notifications.phone_taken", await Accounts.CodeOfAsync(await juan.Client.PostAsJsonAsync($"{Channel}/verify-start", new { phone = "+" + phone })));

        // With large movements on WhatsApp, one of 600 is told there too.
        var settings = await brian.Client.PutAsJsonAsync(Settings, new
        {
            quietHoursEnabled = false, quietFrom = "22:00", quietTo = "07:00", largeMovementThreshold = 500m,
            preferences = new[] { new { category = "largeMovement", emailEnabled = false, whatsAppEnabled = true } }
        });
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        var bcp = await brian.OpenAccountAsync("BCP", 2000m);
        await brian.SpendAsync(bcp.Id(), 600m, description: "Televisor");
        var notice = (await DeliveriesAsync(brian.UserId)).Last();
        Assert.Equal((DeliveryKind.Notice, phone, DeliveryStatus.Pending), (notice.Kind, notice.To, notice.Status));
        Assert.Contains("Televisor", notice.Text);
    }

    [Fact]
    public async Task Saldo_answers_once_with_the_accounts_of_whoever_wrote_and_only_signed_events_count()
    {
        var brianPhone = NewPhone();
        var brian = await VerifiedAsync("Brian", brianPhone);
        await brian.OpenAccountAsync("BCP", 1000m);
        var juan = await VerifiedAsync("Juan", NewPhone());
        await juan.OpenAccountAsync("Interbank de Juan", 777m);
        var anonymous = api.AnonymousClient();

        // Meta registering the address.
        var handshake = await anonymous.GetAsync($"{Webhook}?hub.mode=subscribe&hub.verify_token={ApiFixture.WhatsAppVerifyToken}&hub.challenge=4815");
        Assert.Equal("4815", await handshake.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync($"{Webhook}?hub.mode=subscribe&hub.verify_token=guess&hub.challenge=1")).StatusCode);

        // Without the signature of the App Secret nothing is read.
        var saldo = TextFrom(brianPhone, "Saldo");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(Event(saldo, secret: "someone-else"))).StatusCode);
        var before = (await DeliveriesAsync(brian.UserId)).Count;

        Assert.Equal(HttpStatusCode.OK, (await anonymous.SendAsync(Event(saldo))).StatusCode);
        var reply = (await DeliveriesAsync(brian.UserId)).Last();
        Assert.Equal((DeliveryKind.Reply, brianPhone), (reply.Kind, reply.To));
        Assert.Contains("BCP: PEN 1,000.00", reply.Text);
        Assert.DoesNotContain("Juan", reply.Text);

        // Meta sends the same event again: it is not answered twice.
        Assert.Equal(HttpStatusCode.OK, (await anonymous.SendAsync(Event(saldo))).StatusCode);
        Assert.Equal(before + 1, (await DeliveriesAsync(brian.UserId)).Count);

        // A number nobody proved speaks for no one.
        var juanBefore = (await DeliveriesAsync(juan.UserId)).Count;
        Assert.Equal(HttpStatusCode.OK, (await anonymous.SendAsync(Event(TextFrom(NewPhone(), "saldo")))).StatusCode);
        Assert.Equal(juanBefore, (await DeliveriesAsync(juan.UserId)).Count);

        // Once sent, WhatsApp reports how it went; a late report never takes it back.
        while (await api.ExecuteAsync(services => services.GetRequiredService<IDeliveryDispatcher>().SendPendingAsync(CancellationToken.None)) > 0)
        {
        }

        var sent = (await DeliveriesAsync(brian.UserId)).Last();
        Assert.Equal((DeliveryStatus.Sent, (string?)null), (sent.Status, sent.Text));
        foreach (var status in new[] { "read", "delivered" })
        {
            var report = Payload(new { statuses = new[] { new { id = sent.ExternalId, status, recipient_id = brianPhone, timestamp = "1760000000" } } });
            Assert.Equal(HttpStatusCode.OK, (await anonymous.SendAsync(Event(report))).StatusCode);
        }

        Assert.Equal(DeliveryStatus.Read, (await DeliveriesAsync(brian.UserId)).Last().Status);
    }

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex SixDigits();
}
