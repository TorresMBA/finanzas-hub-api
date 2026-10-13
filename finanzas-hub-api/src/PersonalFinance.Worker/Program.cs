using PersonalFinance.Infrastructure;
using PersonalFinance.Infrastructure.Observability;
using PersonalFinance.Worker.Jobs.Platform;

// Everything that runs without a request: scheduled and queued work. Same application as the API, no HTTP.
// Jobs live in Jobs/<Module> and are listed here.
var builder = Host.CreateApplicationBuilder(args);

builder.AddObservability("personal-finance-worker");
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHostedService<PurgeExpiredIdempotencyKeysJob>();
builder.Services.AddHostedService<SendEmailsJob>();

await builder.Build().RunAsync();
