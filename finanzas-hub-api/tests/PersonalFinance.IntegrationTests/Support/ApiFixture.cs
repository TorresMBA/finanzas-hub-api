using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Controllers;
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

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();

        // Read by WebApplication.CreateBuilder, so they are in place before any service is registered.
        Environment.SetEnvironmentVariable("ConnectionStrings__PersonalFinance", _database.ConnectionString);
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "false");

        _factory = new ApiFactory(Workspaces);
        await _factory.Services.MigrateDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    public HttpClient AnonymousClient() => _factory.CreateClient();

    public HttpClient ClientFor(Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        return client;
    }

    /// <summary>A new user who is an active member of a new workspace.</summary>
    public (Guid UserId, Guid WorkspaceId) NewMember()
    {
        var membership = Workspaces.Add(Guid.NewGuid(), Guid.NewGuid());
        return (membership.UserId, membership.WorkspaceId);
    }

    public static string NotesUrl(Guid workspaceId) => $"/api/v1/workspaces/{workspaceId}/sample-notes";

    public static HttpRequestMessage CreateNoteRequest(Guid workspaceId, object body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, NotesUrl(workspaceId)) { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null)
        {
            request.Headers.Add(ApiControllerBase.IdempotencyKeyHeader, idempotencyKey);
        }

        return request;
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

                services.AddSingleton<IWorkspaceAccess>(workspaces);
                services.AddHandlersFromAssembly(typeof(ApiFixture).Assembly);
            });
        }
    }
}
