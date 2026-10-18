using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Realtime;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.Infrastructure;
using PersonalFinance.Infrastructure.Messaging;
using PersonalFinance.Infrastructure.Security;

namespace PersonalFinance.IntegrationTests.Support;

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}

/// <summary>The real API on a real PostgreSQL, started once for all tests.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private ApiFactory _factory = null!;

    internal FakeWorkspaceAccess Workspaces { get; } = new();

    public IServiceProvider Services => _factory.Services;

    /// <summary>Where the email dispatcher writes messages instead of sending them.</summary>
    public string MailDirectory { get; } = Path.Combine(Path.GetTempPath(), $"pf-test-mail-{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();

        // Read by WebApplication.CreateBuilder, so they are in place before any service is registered.
        Environment.SetEnvironmentVariable("ConnectionStrings__PersonalFinance", _database.ConnectionString);
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "false");
        Environment.SetEnvironmentVariable("RateLimiting__AuthPermitsPerMinute", "100000");
        Environment.SetEnvironmentVariable("Email__PickupDirectory", MailDirectory);

        _factory = new ApiFactory(Workspaces);
        await _factory.Services.MigrateDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
        if (Directory.Exists(MailDirectory))
        {
            Directory.Delete(MailDirectory, recursive: true);
        }
    }

    public const string WhatsAppSecret = "test-app-secret";
    public const string WhatsAppVerifyToken = "test-verify-token";

    public HttpClient AnonymousClient() => _factory.CreateClient();

    /// <summary>A browser-like client: it keeps the session cookie, which is only sent over HTTPS.</summary>
    public HttpClient BrowserClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    public HttpClient ClientFor(Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        return client;
    }

    /// <summary>A connection to the realtime hub as a user, by long polling through the test server.</summary>
    public HubConnection HubFor(Guid userId) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, RealtimeHub.Path.TrimStart('/')), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers[TestAuthHandler.UserHeader] = userId.ToString();
            })
            .Build();

    /// <summary>A new user who is an active member of a new workspace.</summary>
    public (Guid UserId, Guid WorkspaceId) NewMember()
    {
        var membership = Workspaces.Add(Guid.NewGuid(), Guid.NewGuid());
        return (membership.UserId, membership.WorkspaceId);
    }

    /// <summary>Runs code in its own DI scope, optionally as a user inside a workspace, the way a Worker job would.</summary>
    public async Task<T> ExecuteAsync<T>(Func<IServiceProvider, Task<T>> action, Guid? userId = null, Guid? workspaceId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        if (userId is { } user)
        {
            var actor = scope.ServiceProvider.GetRequiredService<IActorContextInitializer>();
            actor.ActAs(user, ActorType.User, "Test");
            if (workspaceId is { } workspace)
            {
                actor.EnterWorkspace(Workspaces.Add(workspace, user));
            }
        }

        return await action(scope.ServiceProvider);
    }

    private sealed class ApiFactory(FakeWorkspaceAccess workspaces) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("WhatsApp:AppSecret", WhatsAppSecret);
            builder.UseSetting("WhatsApp:VerifyToken", WhatsAppVerifyToken);
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultScheme = TestAuthHandler.SchemeName;
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                });

                // The real module keeps answering; a membership a test declared by hand wins over it.
                var real = services.Last(descriptor => descriptor.ServiceType == typeof(IWorkspaceAccess));
                services.AddScoped<IWorkspaceAccess>(provider => new FallbackWorkspaceAccess(workspaces, (IWorkspaceAccess)real.ImplementationFactory!(provider)));
                services.AddHandlersFromAssembly(typeof(ApiFixture).Assembly);
            });
        }
    }
}
