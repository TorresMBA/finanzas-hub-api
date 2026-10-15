using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Notifications.Contracts.Events;

namespace PersonalFinance.Modules.Notifications.Domain;

/// <summary>A phone number in international form, kept as its digits: "+51 987 654 321" is "51987654321".</summary>
public static class PhoneNumber
{
    public const int MinDigits = 10;
    public const int MaxDigits = 15;

    /// <summary>Null when it is not a number with its country code.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => !char.IsDigit(character) && character is not (' ' or '+' or '-' or '(' or ')')))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length is >= MinDigits and <= MaxDigits && digits[0] != '0' ? digits : null;
    }

    /// <summary>"51987654321" as "+51 ••• ••• 321", for showing a number without spelling it out.</summary>
    public static string Mask(string digits) => $"+{digits[..2]} ••• ••• {digits[^3..]}";
}

/// <summary>
/// The WhatsApp number of a person. It counts only once they typed the code sent to it, which proves the number is
/// theirs: only then do notices go to it and do messages from it speak for them. One per person.
/// </summary>
public sealed class ChannelEndpoint : AggregateRoot
{
    public const int CodeLength = 6;
    public const int MaxAttempts = 5;
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendAfter = TimeSpan.FromMinutes(1);

    private ChannelEndpoint()
    {
    }

    private ChannelEndpoint(Guid id) : base(id)
    {
    }

    public Guid UserId { get; private set; }

    public NotificationChannel Channel { get; private set; }

    /// <summary>The verified number, as digits. Null until a code is confirmed.</summary>
    public string? Phone { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>The number a code was sent to and not yet confirmed.</summary>
    public string? PendingPhone { get; private set; }

    public string? CodeHash { get; private set; }

    public DateTimeOffset? CodeSentAt { get; private set; }

    public DateTimeOffset? CodeExpiresAt { get; private set; }

    public int Attempts { get; private set; }

    public bool IsVerified => Phone is not null;

    public static ChannelEndpoint WhatsAppOf(Guid userId) => new(Guid.CreateVersion7()) { UserId = userId, Channel = NotificationChannel.WhatsApp };

    /// <summary>Starts proving a number: the code returned is to be sent to it, and only its hash is kept.</summary>
    public Result<string> StartVerification(string phone, DateTimeOffset now)
    {
        if (CodeSentAt is { } sentAt && now - sentAt < ResendAfter)
        {
            return Error.Conflict("notifications.code_too_soon", "Wait a minute before asking for another code.");
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        PendingPhone = phone;
        CodeHash = Hash(code);
        CodeSentAt = now;
        CodeExpiresAt = now + CodeLifetime;
        Attempts = 0;
        return code;
    }

    public Result Confirm(string code, DateTimeOffset now)
    {
        if (PendingPhone is null || CodeHash is null)
        {
            return Error.Validation("notifications.code_invalid", "The code is not correct.");
        }

        if (Attempts >= MaxAttempts)
        {
            return Error.Validation("notifications.code_attempts_exceeded", "Too many wrong codes. Ask for a new one.");
        }

        if (now > CodeExpiresAt)
        {
            return Error.Validation("notifications.code_expired", "The code expired. Ask for a new one.");
        }

        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(code.Trim())), Encoding.ASCII.GetBytes(CodeHash)))
        {
            Attempts++;
            return Error.Validation("notifications.code_invalid", "The code is not correct.");
        }

        Phone = PendingPhone;
        VerifiedAt = now;
        PendingPhone = null;
        CodeHash = null;
        CodeExpiresAt = null;
        Attempts = 0;
        return Result.Success();
    }

    private string Hash(string code) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{Id:N}:{code}")));
}

public enum DeliveryKind
{
    /// <summary>A reply to a message of the person: free text, allowed in the 24 hours after it.</summary>
    Reply,

    /// <summary>A notice the person did not ask for at that moment: outside those 24 hours it needs an approved template.</summary>
    Notice,

    VerificationCode
}

public enum DeliveryStatus
{
    Pending,
    Sent,
    Delivered,
    Read,
    Failed
}

/// <summary>
/// A message waiting to go out by WhatsApp, or that went. It is written in the transaction of what caused it and a
/// Worker job sends it with retries; WhatsApp then tells how it went. What it said is cleared once sent.
/// </summary>
public sealed class Delivery : AggregateRoot
{
    public const int MaxAttempts = 8;
    public const int MaxTextLength = 1000;

    private Delivery()
    {
    }

    private Delivery(Guid id) : base(id)
    {
    }

    public Guid UserId { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public DeliveryKind Kind { get; private set; }

    /// <summary>The number, as digits.</summary>
    public string To { get; private set; } = null!;

    /// <summary>The whole text, used when there is no template; the parameters fill the template when there is.</summary>
    public string? Text { get; private set; }

    public string[] Parameters { get; private set; } = [];

    public Guid? NotificationId { get; private set; }

    public DeliveryStatus Status { get; private set; }

    /// <summary>The id WhatsApp gave the message, which its delivery reports name.</summary>
    public string? ExternalId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public static Delivery WhatsApp(
        Guid userId, string to, DeliveryKind kind, string text, IReadOnlyList<string> parameters, DateTimeOffset now, DateTimeOffset? notBefore = null,
        Guid? notificationId = null) => new(Guid.CreateVersion7())
    {
        UserId = userId,
        Channel = NotificationChannel.WhatsApp,
        Kind = kind,
        To = to,
        Text = text.Length > MaxTextLength ? text[..(MaxTextLength - 1)] + "…" : text,
        Parameters = [.. parameters],
        NotificationId = notificationId,
        Status = DeliveryStatus.Pending,
        CreatedAt = now,
        NextAttemptAt = notBefore > now ? notBefore.Value : now
    };

    public void MarkSent(string? externalId, DateTimeOffset now)
    {
        Status = DeliveryStatus.Sent;
        ExternalId = externalId;
        SentAt = now;
        Text = null;
        Parameters = [];
        LastError = null;
    }

    /// <summary>Waits longer after each failure: 1, 2, 4... minutes, up to an hour; after the last attempt it gives up.</summary>
    public void MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
        NextAttemptAt = now.AddMinutes(Math.Min(60, Math.Pow(2, Attempts - 1)));
        if (Attempts >= MaxAttempts)
        {
            Status = DeliveryStatus.Failed;
            Text = null;
            Parameters = [];
        }
    }

    /// <summary>What WhatsApp reported. A report arriving late never takes the message back: read stays read.</summary>
    public void Report(DeliveryStatus status, string? error)
    {
        if (status == DeliveryStatus.Failed)
        {
            Status = DeliveryStatus.Failed;
            LastError = error is { Length: > 500 } ? error[..500] : error;
        }
        else if (Status != DeliveryStatus.Failed && status > Status)
        {
            Status = status;
        }
    }
}

/// <summary>
/// Something WhatsApp told us, kept so that the same event arriving twice is acted on once. A message from a
/// verified number raises <see cref="InboundMessageReceived"/>, which the module that answers reacts to.
/// </summary>
public sealed class ReceivedWebhookEvent : AggregateRoot
{
    public const string WhatsAppProvider = "whatsapp";
    public const int MaxExternalIdLength = 200;

    private ReceivedWebhookEvent()
    {
    }

    private ReceivedWebhookEvent(Guid id) : base(id)
    {
    }

    public string Provider { get; private set; } = null!;

    /// <summary>The id of the message, or of the message and its status for a delivery report.</summary>
    public string ExternalId { get; private set; } = null!;

    public Guid? UserId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public static ReceivedWebhookEvent Status(string externalId, DateTimeOffset now) =>
        new(Guid.CreateVersion7()) { Provider = WhatsAppProvider, ExternalId = Truncate(externalId), ReceivedAt = now };

    public static ReceivedWebhookEvent Message(string externalId, Guid userId, string text, DateTimeOffset now)
    {
        var received = new ReceivedWebhookEvent(Guid.CreateVersion7()) { Provider = WhatsAppProvider, ExternalId = Truncate(externalId), UserId = userId, ReceivedAt = now };
        received.Raise(new InboundMessageReceived(userId, text));
        return received;
    }

    private static string Truncate(string value) => value.Length > MaxExternalIdLength ? value[..MaxExternalIdLength] : value;
}

/// <summary>A text someone sent by WhatsApp.</summary>
/// <param name="From">The number, as digits.</param>
public sealed record WhatsAppMessage(string Id, string From, string Text);

public sealed record WhatsAppStatusReport(string MessageId, DeliveryStatus Status, string? Error);

/// <summary>
/// The parts of a WhatsApp Cloud API event this application uses: text messages and delivery reports, under
/// <c>entry[].changes[].value</c>. Anything else (images, reactions, unknown statuses) is skipped.
/// </summary>
public sealed record WhatsAppPayload(IReadOnlyList<WhatsAppMessage> Messages, IReadOnlyList<WhatsAppStatusReport> Statuses)
{
    public static WhatsAppPayload Parse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var messages = new List<WhatsAppMessage>();
        var statuses = new List<WhatsAppStatusReport>();
        foreach (var value in Values(document.RootElement))
        {
            foreach (var message in Items(value, "messages"))
            {
                if (Text(message, "id") is { Length: > 0 } id
                    && PhoneNumber.Normalize(Text(message, "from")) is { } from
                    && message.TryGetProperty("text", out var text) && Text(text, "body") is { } said)
                {
                    messages.Add(new WhatsAppMessage(id, from, said));
                }
            }

            foreach (var status in Items(value, "statuses"))
            {
                var reported = Text(status, "status") switch
                {
                    "sent" => DeliveryStatus.Sent,
                    "delivered" => DeliveryStatus.Delivered,
                    "read" => DeliveryStatus.Read,
                    "failed" => DeliveryStatus.Failed,
                    _ => (DeliveryStatus?)null
                };
                if (Text(status, "id") is { Length: > 0 } id && reported is { } known)
                {
                    var error = Items(status, "errors").Select(failure => Text(failure, "title") ?? Text(failure, "message")).FirstOrDefault();
                    statuses.Add(new WhatsAppStatusReport(id, known, error));
                }
            }
        }

        return new WhatsAppPayload(messages, statuses);
    }

    private static IEnumerable<JsonElement> Values(JsonElement root) =>
        Items(root, "entry").SelectMany(entry => Items(entry, "changes"))
            .Where(change => change.ValueKind == JsonValueKind.Object && change.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Object)
            .Select(change => change.GetProperty("value"));

    private static IEnumerable<JsonElement> Items(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().ToList()
            : [];

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
