namespace PersonalFinance.BuildingBlocks.Application.Abstractions;

public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>
/// Queues an email in the current transaction; a Worker job sends it with retries. A use case never talks to
/// the mail server, so a mail outage cannot fail a command and a rolled-back command sends nothing.
/// </summary>
public interface IEmailSender
{
    void Queue(EmailMessage message);
}

/// <summary>Builds absolute links to the web application, for emails.</summary>
public interface IAppLinks
{
    /// <param name="path">Path inside the web application, starting with a slash.</param>
    string To(string path);
}
