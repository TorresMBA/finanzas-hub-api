using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using PersonalFinance.Infrastructure.Audit;
using PersonalFinance.Infrastructure.Email;
using PersonalFinance.Infrastructure.Messaging;
using PersonalFinance.Modules.Categories.Application.Abstractions;
using PersonalFinance.Modules.Identity.Application.Abstractions;
using PersonalFinance.Modules.Identity.Domain;
using PersonalFinance.Modules.Workspaces.Application.Abstractions;
using PersonalFinance.Infrastructure.Observability;
using PersonalFinance.Infrastructure.Persistence;
using PersonalFinance.Infrastructure.Security;
using PersonalFinance.Modules.Sample.Application.Abstractions;

namespace PersonalFinance.Infrastructure;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public static class DependencyInjection
{
    public const string ConnectionStringName = "PersonalFinance";
    private const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>
    /// Everything below the hosts: the database, the command pipeline, the audit trail, the actor context and
    /// the modules. The API and the Worker both call it, so they always run the same application.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(provider =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{ConnectionStringName} is not configured. Set it with 'dotnet user-secrets' or the ConnectionStrings__{ConnectionStringName} environment variable.");
            }

            return new NpgsqlDataSourceBuilder(connectionString)
                .UseLoggerFactory(provider.GetRequiredService<ILoggerFactory>())
                .Build();
        });

        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>(), npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schemas.Platform))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<ActorContext>();
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<ActorContext>());
        services.AddScoped<IWorkspaceContext>(provider => provider.GetRequiredService<ActorContext>());
        services.AddScoped<IActorContextInitializer>(provider => provider.GetRequiredService<ActorContext>());
        services.AddScoped<CorrelationContext>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IAuditLog, AuditLogWriter>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddScoped<IIdempotencyKeyCleanup, IdempotencyKeyCleanup>();

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<FrontendOptions>(configuration.GetSection(FrontendOptions.SectionName));
        services.AddSingleton<IAppLinks, AppLinks>();
        services.AddScoped<IEmailSender, EmailQueue>();
        services.AddScoped<IEmailDispatcher, EmailDispatcher>();

        AddIdentity(services);

        // Modules. Adding one takes two lines: its services and handlers, and the tables it may see.
        services.AddModule(typeof(IIdentityDb).Assembly);
        services.AddScoped<IIdentityDb>(provider => provider.GetRequiredService<AppDbContext>());

        services.AddModule(typeof(IWorkspacesDb).Assembly);
        services.AddScoped<IWorkspacesDb>(provider => provider.GetRequiredService<AppDbContext>());

        services.AddModule(typeof(ICategoriesDb).Assembly);
        services.AddScoped<ICategoriesDb>(provider => provider.GetRequiredService<AppDbContext>());

        services.AddModule(typeof(ISampleDb).Assembly);
        services.AddScoped<ISampleDb>(provider => provider.GetRequiredService<AppDbContext>());

        return services;
    }

    /// <summary>
    /// ASP.NET Core Identity with its standard store on the application's context: password hashing, lockout
    /// and the tokens of the confirmation and recovery emails. The session cookie itself belongs to the API.
    /// </summary>
    private static void AddIdentity(IServiceCollection services)
    {
        services.AddIdentityCore<User>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        // In the database so that a redeploy does not sign everyone out or invalidate the links already emailed.
        services.AddDataProtection().SetApplicationName("PersonalHub").PersistKeysToDbContext<AppDbContext>();
    }

    /// <summary>Applies the pending migrations. Run before the application starts serving, never by two processes at once.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PersonalFinance.Migrations").LogInformation("Applying {Count} migration(s)", pending.Count);
        await db.Database.MigrateAsync(cancellationToken);
    }
}
