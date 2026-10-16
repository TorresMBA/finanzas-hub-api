using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.Infrastructure.Persistence;
using PersonalFinance.Modules.Notifications.Application.Abstractions;
using PersonalFinance.Modules.Notifications.Domain;

namespace PersonalFinance.Infrastructure.WhatsApp;

/// <summary>
/// The WhatsApp Cloud API account (Meta). Without an access token nothing is sent: messages are written as text
/// files to <see cref="OutboxDirectory"/>, which is what a development machine and the tests use.
/// </summary>
public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    /// <summary>Compared with <c>hub.verify_token</c> when Meta checks the webhook address.</summary>
    public string? VerifyToken { get; set; }

    /// <summary>Signs every event Meta sends; an event that does not match it is rejected.</summary>
    public string? AppSecret { get; set; }

    public string? AccessToken { get; set; }

    public string? PhoneNumberId { get; set; }

    /// <summary>Where the Graph API answers; the version and the phone number id are appended to it.</summary>
    public string GraphApiBase { get; set; } = "https://graph.facebook.com/";

    public string GraphApiVersion { get; set; } = "v21.0";

    /// <summary>
    /// Approved template for notices, with two body parameters: the title and the text. Outside the 24 hours after
    /// the person last wrote, WhatsApp only lets a business start a conversation with a template.
    /// </summary>
    public string? NoticeTemplate { get; set; }

    /// <summary>Approved authentication template for the verification code, with the code as its parameter.</summary>
    public string? CodeTemplate { get; set; }

    public string TemplateLanguage { get; set; } = "es";

    /// <summary>Folder for the messages when there is no access token. Defaults to a folder in the temp directory.</summary>
    public string? OutboxDirectory { get; set; }

    /// <summary>Only for a development machine without an account: the verification code is also shown on screen.</summary>
    public bool ShowCodesWithoutAccount { get; set; }

    public bool IsConnected => !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(PhoneNumberId);

    public string ResolveOutboxDirectory() =>
        string.IsNullOrWhiteSpace(OutboxDirectory) ? Path.Combine(Path.GetTempPath(), "personal-hub-whatsapp") : OutboxDirectory;
}

internal sealed class WhatsAppSetup(IOptions<WhatsAppOptions> options) : IWhatsAppSetup
{
    public bool ShowsCodesOnScreen => options.Value.ShowCodesWithoutAccount && !options.Value.IsConnected;
}

/// <summary>Checks that an event comes from Meta: the HMAC-SHA256 of the raw body with the App Secret.</summary>
public static class WhatsAppSignature
{
    /// <param name="signature">The <c>X-Hub-Signature-256</c> header, as <c>sha256=&lt;hex&gt;</c>.</param>
    public static bool IsValid(string? signature, string? appSecret, ReadOnlySpan<byte> body)
    {
        const string prefix = "sha256=";
        if (string.IsNullOrWhiteSpace(appSecret) || signature is null || !signature.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(signature.AsSpan(prefix.Length));
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body);
        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }
}

/// <summary>Puts one message in front of WhatsApp. Returns the id WhatsApp gave it.</summary>
internal interface IWhatsAppSender
{
    Task<string?> SendAsync(Delivery delivery, CancellationToken cancellationToken);
}

/// <summary>Sends by the WhatsApp Cloud API, or writes to a folder when no account is configured.</summary>
internal sealed class WhatsAppSender(HttpClient http, IOptions<WhatsAppOptions> options) : IWhatsAppSender
{
    public async Task<string?> SendAsync(Delivery delivery, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConnected)
        {
            var directory = settings.ResolveOutboxDirectory();
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, $"{delivery.Id:N}.txt"), $"To: +{delivery.To}\nKind: {delivery.Kind}\n\n{delivery.Text}", cancellationToken);
            return $"local.{delivery.Id:N}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.GraphApiVersion.Trim('/')}/{settings.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(Body(delivery, settings))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        var answer = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"WhatsApp answered {(int)response.StatusCode}: {answer}");
        }

        using var document = JsonDocument.Parse(answer);
        return document.RootElement.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array && messages.GetArrayLength() > 0
            ? messages[0].GetProperty("id").GetString()
            : null;
    }

    private static object Body(Delivery delivery, WhatsAppOptions settings)
    {
        var template = delivery.Kind switch
        {
            DeliveryKind.Notice => settings.NoticeTemplate,
            DeliveryKind.VerificationCode => settings.CodeTemplate,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(template))
        {
            return new { messaging_product = "whatsapp", to = delivery.To, type = "text", text = new { body = delivery.Text } };
        }

        var parameters = delivery.Parameters.Select(value => new { type = "text", text = value }).ToArray();
        object[] components = delivery.Kind == DeliveryKind.VerificationCode
            // An authentication template repeats the code in its copy button.
            ? [new { type = "body", parameters }, new { type = "button", sub_type = "url", index = "0", parameters }]
            : [new { type = "body", parameters }];
        return new
        {
            messaging_product = "whatsapp",
            to = delivery.To,
            type = "template",
            template = new { name = template, language = new { code = settings.TemplateLanguage }, components }
        };
    }
}

/// <summary>Sends what is waiting in the queue of WhatsApp messages. Called by a Worker job.</summary>
public interface IDeliveryDispatcher
{
    /// <returns>How many messages were sent.</returns>
    Task<int> SendPendingAsync(CancellationToken cancellationToken);
}

internal sealed class DeliveryDispatcher(AppDbContext db, IWhatsAppSender sender, IClock clock, ILogger<DeliveryDispatcher> logger) : IDeliveryDispatcher
{
    private const int BatchSize = 20;

    private static readonly System.Diagnostics.Metrics.Counter<long> Sent =
        new System.Diagnostics.Metrics.Meter("PersonalFinance").CreateCounter<long>("whatsapp.deliveries", description: "WhatsApp messages sent or failed.");

    public async Task<int> SendPendingAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // The rows stay locked until the commit, so a second Worker skips them instead of sending them twice.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var batch = await db.Deliveries
            .FromSql($"""
                SELECT * FROM notifications.deliveries
                WHERE status = 'Pending' AND next_attempt_at <= {now}
                ORDER BY created_at
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var delivery in batch)
        {
            try
            {
                delivery.MarkSent(await sender.SendAsync(delivery, cancellationToken), clock.UtcNow);
                sent++;
                Sent.Add(1, new KeyValuePair<string, object?>("outcome", "sent"), new KeyValuePair<string, object?>("kind", delivery.Kind.ToString()));
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or JsonException or TaskCanceledException
                                                  && !cancellationToken.IsCancellationRequested)
            {
                delivery.MarkFailed(exception.Message, clock.UtcNow);
                Sent.Add(1, new KeyValuePair<string, object?>("outcome", "failed"), new KeyValuePair<string, object?>("kind", delivery.Kind.ToString()));
                logger.LogWarning(exception, "WhatsApp message {DeliveryId} could not be sent (attempt {Attempt})", delivery.Id, delivery.Attempts);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return sent;
    }
}
