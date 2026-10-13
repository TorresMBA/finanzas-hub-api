using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PersonalFinance.Api.Http;
using PersonalFinance.Api.Realtime;
using PersonalFinance.Infrastructure;
using PersonalFinance.Infrastructure.Messaging;
using PersonalFinance.Infrastructure.Observability;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsProduction() && string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(DependencyInjection.ConnectionStringName)))
{
    throw new InvalidOperationException($"ConnectionStrings:{DependencyInjection.ConnectionStringName} must be configured in Production.");
}

builder.AddObservability("personal-finance-api");
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiHttp(builder.Configuration, builder.Environment);
builder.Services.AddOpenApi("v1");

// The hub lives in this host, so it is this host that knows how to reach the browsers.
builder.Services.AddSignalR();
builder.Services.AddSingleton<IRealtimeTransport, SignalRRealtimeTransport>();

var app = builder.Build();

// "dotnet run -- migrate" applies every pending migration and exits. Used by tools/migrate.ps1 and by deployments.
if (args.Contains("migrate"))
{
    await app.Services.MigrateDatabaseAsync();
    return;
}

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await app.Services.MigrateDatabaseAsync();
}

app.UseApiHttp();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ObservabilityExtensions.ReadyTag) }).AllowAnonymous();

if (!app.Environment.IsProduction())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.MapControllers();
app.MapHub<RealtimeHub>(RealtimeHub.Path);

await app.RunAsync();

public partial class Program;
