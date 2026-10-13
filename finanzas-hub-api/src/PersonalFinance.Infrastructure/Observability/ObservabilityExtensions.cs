using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PersonalFinance.Infrastructure.Persistence;

namespace PersonalFinance.Infrastructure.Observability;

public static class ObservabilityExtensions
{
    public const string ReadyTag = "ready";

    /// <summary>
    /// Traces, metrics and logs through OpenTelemetry. They are exported over OTLP only when
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, so a machine without a collector runs without errors.
    /// </summary>
    public static IHostApplicationBuilder AddObservability(this IHostApplicationBuilder builder, string serviceName)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
        });

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddSource(Telemetry.SourceName)
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(metrics => metrics
                .AddMeter(Telemetry.SourceName)
                .AddMeter("Npgsql")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetry.UseOtlpExporter();
        }

        builder.Services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag])
            .AddCheck<MigrationsHealthCheck>("migrations", tags: [ReadyTag]);

        return builder;
    }
}

internal sealed class DatabaseHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("The database is not reachable.", exception);
        }
    }
}

/// <summary>Not ready while the database is behind the migrations this build ships.</summary>
internal sealed class MigrationsHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            return pending.Count == 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"{pending.Count} pending migration(s), starting at {pending[0]}.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Migrations could not be checked.", exception);
        }
    }
}
