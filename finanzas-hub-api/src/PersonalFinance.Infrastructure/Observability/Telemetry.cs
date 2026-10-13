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
}

/// <summary>
/// The correlation id of the work in progress, for code that runs below the host layer (the audit trail).
/// One per DI scope; the HTTP pipeline sets it for each request.
/// </summary>
public sealed class CorrelationContext
{
    public string? CorrelationId { get; set; }
}
