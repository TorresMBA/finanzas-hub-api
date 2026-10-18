using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PersonalFinance.Infrastructure.Observability;

/// <summary>Single source for traces and metrics. Spans and measurements carry a <c>module</c> tag.</summary>
public static class Telemetry
{
    public const string SourceName = "PersonalFinance";

    public static readonly ActivitySource ActivitySource = new(SourceName);

    public static readonly Meter Meter = new(SourceName);

    public static readonly Histogram<double> UseCaseDuration =
        Meter.CreateHistogram<double>("usecase.duration", "ms", "Duration of a command or query by module, use case and outcome.");

    public static readonly Counter<long> Forbidden =
        Meter.CreateCounter<long>("security.forbidden", description: "Requests answered 403, by endpoint.");

    public static readonly Histogram<double> ScheduleCycleDuration =
        Meter.CreateHistogram<double>("scheduler.cycle.duration", "ms", "Time the Worker took to run the schedule of every person.");

    // What each sending job saw still waiting after its last run. Read by the gauge when the metrics are collected.
    private static long _pendingEmails;
    private static long _pendingWhatsApp;

    public static readonly ObservableGauge<long> PendingDeliveries = Meter.CreateObservableGauge(
        "notifications.deliveries.pending",
        () => new[]
        {
            new Measurement<long>(Interlocked.Read(ref _pendingEmails), new KeyValuePair<string, object?>("channel", "email")),
            new Measurement<long>(Interlocked.Read(ref _pendingWhatsApp), new KeyValuePair<string, object?>("channel", "whatsapp"))
        },
        description: "Emails and WhatsApp messages still waiting to be sent, as the last run of their job saw them.");

    public static void RecordPendingEmails(long count) => Interlocked.Exchange(ref _pendingEmails, count);

    public static void RecordPendingWhatsApp(long count) => Interlocked.Exchange(ref _pendingWhatsApp, count);
}

/// <summary>
/// The correlation id of the work in progress, for code that runs below the host layer (the audit trail).
/// One per DI scope; the HTTP pipeline sets it for each request.
/// </summary>
public sealed class CorrelationContext
{
    public string? CorrelationId { get; set; }
}
