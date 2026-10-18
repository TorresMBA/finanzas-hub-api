using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.Infrastructure.Observability;
using PersonalFinance.Infrastructure.Persistence;
using PersonalFinance.Infrastructure.Platform;

namespace PersonalFinance.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string From { get; set; } = "PersonalHub <no-reply@personalhub.local>";

    /// <summary>SMTP server. When empty, emails are written as .eml files to <see cref="PickupDirectory"/> instead.</summary>
    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;

    public string? SmtpUser { get; set; }

    public string? SmtpPassword { get; set; }

    public bool SmtpEnableSsl { get; set; } = true;

    /// <summary>Folder for the .eml files when there is no SMTP server. Defaults to a folder in the temp directory.</summary>
    public string? PickupDirectory { get; set; }

    public string ResolvePickupDirectory() =>
        string.IsNullOrWhiteSpace(PickupDirectory) ? Path.Combine(Path.GetTempPath(), "personal-hub-mail") : PickupDirectory;
}

public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    /// <summary>Public address of the web application, without a trailing slash. Links in emails start with it.</summary>
    public string BaseUrl { get; set; } = "http://localhost:4200";
}

internal sealed class AppLinks(IOptions<FrontendOptions> options) : IAppLinks
{
    public string To(string path) => options.Value.BaseUrl.TrimEnd('/') + path;
}

/// <summary>Adds the email to the context; it is saved with the rest of the command's transaction.</summary>
internal sealed class EmailQueue(AppDbContext db, IClock clock) : IEmailSender
{
    public void Queue(EmailMessage message) =>
        db.OutgoingEmails.Add(OutgoingEmail.Create(message.To, message.Subject, message.Body, clock.UtcNow, message.NotBefore));
}

/// <summary>Sends what is waiting in the email queue. Called by a Worker job.</summary>
public interface IEmailDispatcher
{
    /// <returns>How many emails were sent.</returns>
    Task<int> SendPendingAsync(CancellationToken cancellationToken);
}

internal sealed class EmailDispatcher(AppDbContext db, IOptions<EmailOptions> options, IClock clock, ILogger<EmailDispatcher> logger) : IEmailDispatcher
{
    private const int BatchSize = 20;
    private const int MaxAttempts = 8;

    public async Task<int> SendPendingAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // The rows stay locked until the commit, so a second Worker skips them instead of sending them twice.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var batch = await db.OutgoingEmails
            .FromSql($"""
                SELECT * FROM platform.outgoing_emails
                WHERE sent_at IS NULL AND next_attempt_at <= {now} AND attempts < {MaxAttempts}
                ORDER BY created_at
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var email in batch)
        {
            try
            {
                await SendAsync(email, cancellationToken);
                email.MarkSent(clock.UtcNow);
                sent++;
            }
            catch (Exception exception) when (exception is SmtpException or IOException or InvalidOperationException)
            {
                email.MarkFailed(exception.Message, clock.UtcNow);
                logger.LogWarning(exception, "Email {EmailId} could not be sent (attempt {Attempt})", email.Id, email.Attempts);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        Telemetry.RecordPendingEmails(await db.OutgoingEmails.LongCountAsync(email => email.SentAt == null && email.Attempts < MaxAttempts, cancellationToken));
        return sent;
    }

    private async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var message = new MailMessage(settings.From, email.To, email.Subject, email.Body ?? string.Empty);
        using var client = new SmtpClient();
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            var directory = settings.ResolvePickupDirectory();
            Directory.CreateDirectory(directory);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = directory;
        }
        else
        {
            client.Host = settings.SmtpHost;
            client.Port = settings.SmtpPort;
            client.EnableSsl = settings.SmtpEnableSsl;
            if (!string.IsNullOrEmpty(settings.SmtpUser))
            {
                client.Credentials = new NetworkCredential(settings.SmtpUser, settings.SmtpPassword);
            }
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}
