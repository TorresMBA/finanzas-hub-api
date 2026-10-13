using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Infrastructure.Messaging;
using PersonalFinance.Infrastructure.Persistence;
using PersonalFinance.Infrastructure.Platform;
using PersonalFinance.IntegrationTests.Support;
using PersonalFinance.Modules.Workspaces.Domain;

namespace PersonalFinance.IntegrationTests;

/// <summary>One transaction per command, reactions to domain events, idempotency and the audit trail, through real use cases.</summary>
[Collection(ApiCollection.Name)]
public class CommandPipelineTests(ApiFixture api)
{
    [Fact]
    public async Task A_command_stores_its_change_its_audit_entry_and_the_reactions_to_its_event_together()
    {
        var brian = await api.RegisterAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;

        var response = await brian.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/onboarding/complete", new { baseCurrency = "PEN", timeZone = "America/Lima" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var audit = await api.ExecuteAsync(services => Db(services).AuditLog.AsNoTracking()
            .SingleAsync(entry => entry.Action == "workspaces.workspace.onboarding_completed" && entry.ResourceId == workspaceId));
        Assert.Equal(brian.UserId, audit.ActorUserId);
        Assert.Equal("User", audit.ActorType);
        Assert.Equal("Web", audit.Source);
        Assert.Equal(workspaceId, audit.WorkspaceId);
        Assert.Equal("PEN", JsonDocument.Parse(audit.After!).RootElement.GetProperty("baseCurrency").GetString());
        Assert.False(string.IsNullOrWhiteSpace(audit.CorrelationId));

        // Two reactions ran inside the same transaction: the real one seeded the categories, and the probe
        // found in the database what the command had just written.
        var seeded = await brian.Client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{workspaceId}/categories");
        Assert.NotEmpty(seeded.EnumerateArray());
        Assert.Equal("PEN", OnboardingProbe.CurrencySeen[workspaceId]);
        var reactionAudit = await api.ExecuteAsync(services => Db(services).AuditLog.AsNoTracking()
            .SingleAsync(entry => entry.Action == "categories.defaults.seeded" && entry.WorkspaceId == workspaceId));
        Assert.Equal(audit.CorrelationId, reactionAudit.CorrelationId);
    }

    [Fact]
    public async Task A_reaction_that_fails_rolls_back_the_command_that_caused_it_and_the_other_reactions()
    {
        var brian = await api.RegisterAsync("Brian");
        var workspaceId = brian.PersonalWorkspaceId;

        var response = await brian.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/onboarding/complete", new { baseCurrency = "PEN", timeZone = OnboardingProbe.ExplodingTimeZone });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("unexpected", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var workspace = await brian.Client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{workspaceId}");
        Assert.Equal("pendingSetup", workspace.GetProperty("status").GetString());
        Assert.Empty((await brian.Client.GetFromJsonAsync<JsonElement>($"/api/v1/workspaces/{workspaceId}/categories")).EnumerateArray());
        var auditEntries = await api.ExecuteAsync(services => Db(services).AuditLog.AsNoTracking()
            .CountAsync(entry => entry.WorkspaceId == workspaceId && entry.Action != "workspaces.workspace.created"));
        Assert.Equal(0, auditEntries);

        // Nothing is left half done: the same request with a healthy reaction succeeds.
        var retry = await brian.Client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/onboarding/complete", new { baseCurrency = "PEN", timeZone = "America/Lima" });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task A_failed_command_does_not_keep_its_idempotency_key()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var url = Books.AccountsUrl(brian.PersonalWorkspaceId);
        var key = Guid.NewGuid().ToString();

        var rejected = await brian.Client.PostWithKeyAsync(url, new { name = "Yenes", type = "bank", currency = "JPY" }, key);
        var corrected = await brian.Client.PostWithKeyAsync(url, new { name = "Soles", type = "bank", currency = "PEN" }, key);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);
    }

    [Fact]
    public async Task Replaying_an_idempotency_key_returns_the_first_response_and_applies_the_effect_once()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var url = Books.AccountsUrl(brian.PersonalWorkspaceId);
        var key = Guid.NewGuid().ToString();
        var body = new { name = "Una sola vez", type = "bank", currency = "PEN", openingBalance = 100m };

        var first = await brian.Client.PostWithKeyAsync(url, body, key);
        var second = await brian.Client.PostWithKeyAsync(url, body, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal((await first.Content.ReadFromJsonAsync<JsonElement>()).Id(), (await second.Content.ReadFromJsonAsync<JsonElement>()).Id());
        var accounts = await brian.Client.GetFromJsonAsync<JsonElement>(url);
        Assert.Equal(100m, Assert.Single(accounts.EnumerateArray()).GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task Reusing_an_idempotency_key_for_a_different_request_is_rejected()
    {
        var brian = await api.RegisterWithActiveWorkspaceAsync("Brian");
        var url = Books.AccountsUrl(brian.PersonalWorkspaceId);
        var key = Guid.NewGuid().ToString();
        await brian.Client.PostWithKeyAsync(url, new { name = "Original", type = "bank", currency = "PEN" }, key);

        var response = await brian.Client.PostWithKeyAsync(url, new { name = "Otra cosa", type = "bank", currency = "PEN" }, key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("idempotency.key_reused", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Single((await brian.Client.GetFromJsonAsync<JsonElement>(url)).EnumerateArray());
    }

    [Fact]
    public async Task Two_users_can_use_the_same_idempotency_key_without_seeing_each_others_result()
    {
        var (brian, workspaceId) = api.NewMember();
        var juan = api.Workspaces.Add(workspaceId, Guid.NewGuid()).UserId;
        var key = $"same-key-for-both-{workspaceId}";
        var body = new { name = "Efectivo", type = "cash", currency = "PEN" };

        var fromBrian = await api.ClientFor(brian).PostWithKeyAsync(Books.AccountsUrl(workspaceId), body, key);
        var fromJuan = await api.ClientFor(juan).PostWithKeyAsync(Books.AccountsUrl(workspaceId), body, key);

        Assert.Equal(HttpStatusCode.Created, fromBrian.StatusCode);
        Assert.Equal(HttpStatusCode.Created, fromJuan.StatusCode);
        Assert.NotEqual((await fromBrian.Content.ReadFromJsonAsync<JsonElement>()).Id(), (await fromJuan.Content.ReadFromJsonAsync<JsonElement>()).Id());
    }

    [Fact]
    public async Task A_rejected_command_leaves_nothing_behind_in_any_schema()
    {
        var (userId, workspaceId) = api.NewMember();
        var marker = Guid.NewGuid();

        var result = await api.ExecuteAsync(
            services => services.GetRequiredService<ICommandHandler<WriteThenFailCommand, Unit>>().HandleAsync(new WriteThenFailCommand(marker), CancellationToken.None),
            userId, workspaceId);

        Assert.True(result.IsFailure);
        Assert.Equal("test.forced_failure", result.Error.Code);
        var markerName = marker.ToString("N");
        var (accounts, auditEntries) = await api.ExecuteAsync(async services => (
            await Db(services).Accounts.IgnoreQueryFilters().AsNoTracking().CountAsync(account => account.Name == markerName),
            await Db(services).AuditLog.AsNoTracking().CountAsync(entry => entry.ResourceId == marker)));
        Assert.Equal(0, accounts);
        Assert.Equal(0, auditEntries);
    }

    [Fact]
    public async Task Saving_outside_the_command_pipeline_with_events_pending_is_refused()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => api.ExecuteAsync(async services =>
        {
            var db = Db(services);
            db.Workspaces.Add(Workspace.Create("Fuera del pipeline", Guid.NewGuid(), DateTimeOffset.UtcNow).Value);
            return await db.SaveChangesAsync();
        }));

        Assert.Contains("outside the command pipeline", exception.Message);
    }

    [Fact]
    public async Task Expired_idempotency_keys_are_purged_and_live_ones_are_kept()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await api.ExecuteAsync(async services =>
        {
            var db = Db(services);
            db.IdempotencyKeys.Add(IdempotencyRecord.Create(userId, "expired", "test", "hash", "{}", now.AddDays(-30), TimeSpan.FromDays(7)));
            db.IdempotencyKeys.Add(IdempotencyRecord.Create(userId, "live", "test", "hash", "{}", now, TimeSpan.FromDays(7)));
            return await db.SaveChangesAsync();
        });

        var removed = await api.ExecuteAsync(services => services.GetRequiredService<IIdempotencyKeyCleanup>().PurgeExpiredAsync(CancellationToken.None));

        var remaining = await api.ExecuteAsync(services => Db(services).IdempotencyKeys.AsNoTracking()
            .Where(record => record.UserId == userId).Select(record => record.Key).ToListAsync());
        Assert.True(removed >= 1);
        Assert.Equal(["live"], remaining);
    }

    [Fact]
    public async Task Every_private_entity_is_filtered_by_owner()
    {
        var unfiltered = await api.ExecuteAsync(services => Task.FromResult(Db(services).Model.GetEntityTypes()
            .Where(entityType => typeof(IOwned).IsAssignableFrom(entityType.ClrType) && entityType.GetDeclaredQueryFilters().Count == 0)
            .Select(entityType => entityType.ClrType.FullName)
            .ToList()));

        Assert.Empty(unfiltered);
    }

    [Fact]
    public async Task Every_table_lives_in_the_schema_of_its_module()
    {
        var misplaced = await api.ExecuteAsync(services => Task.FromResult(Db(services).Model.GetEntityTypes()
            .Select(entityType => (Type: entityType.ClrType.FullName!, Actual: entityType.GetSchema(), Expected: ExpectedSchema(entityType.ClrType)))
            .Where(table => table.Actual != table.Expected)
            .Select(table => $"{table.Type} is in schema '{table.Actual}', expected '{table.Expected}'.")
            .ToList()));

        Assert.Empty(misplaced);
    }

    private static AppDbContext Db(IServiceProvider services) => services.GetRequiredService<AppDbContext>();

    /// <summary>"PersonalFinance.Modules.SharedExpenses.Domain.X" belongs in "shared_expenses"; anything else in "platform".</summary>
    private static string ExpectedSchema(Type entity)
    {
        const string modulePrefix = "PersonalFinance.Modules.";
        if (entity.Namespace == "Microsoft.AspNetCore.Identity")
        {
            // The tables ASP.NET Core Identity brings belong to the Identity module.
            return "identity";
        }

        if (entity.Namespace?.StartsWith(modulePrefix, StringComparison.Ordinal) != true)
        {
            return "platform";
        }

        var module = entity.Namespace[modulePrefix.Length..].Split('.')[0];
        return string.Concat(module.Select((character, index) =>
            char.IsUpper(character) && index > 0 ? $"_{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));
    }
}
