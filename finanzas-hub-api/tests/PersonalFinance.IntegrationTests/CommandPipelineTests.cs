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
using PersonalFinance.Modules.Sample.Domain;

namespace PersonalFinance.IntegrationTests;

/// <summary>Ownership, one transaction per command, reactions to domain events, idempotency and the audit trail, through a real use case.</summary>
[Collection(ApiCollection.Name)]
public class CommandPipelineTests(ApiFixture api)
{
    private sealed record NoteDto(Guid Id, string Title, DateTimeOffset CreatedAt);

    [Fact]
    public async Task Creating_a_note_stores_the_note_its_audit_entry_and_the_reaction_to_its_event_together()
    {
        var (userId, workspaceId) = api.NewMember();

        var response = await api.ClientFor(userId).SendAsync(ApiFixture.CreateNoteRequest(workspaceId, new { title = "Pagar la luz" }, Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var note = (await response.Content.ReadFromJsonAsync<NoteDto>())!;
        Assert.Equal("Pagar la luz", note.Title);
        Assert.EndsWith($"/sample-notes/{note.Id}", response.Headers.Location!.ToString());

        var audit = await api.ExecuteAsync(services => Db(services).AuditLog.AsNoTracking().SingleAsync(entry => entry.ResourceId == note.Id));
        Assert.Equal("sample.note.created", audit.Action);
        Assert.Equal(userId, audit.ActorUserId);
        Assert.Equal("User", audit.ActorType);
        Assert.Equal("Web", audit.Source);
        Assert.Equal(workspaceId, audit.WorkspaceId);
        Assert.Equal(userId, audit.OwnerUserId);
        Assert.Equal("Pagar la luz", JsonDocument.Parse(audit.After!).RootElement.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(audit.CorrelationId));

        var receipts = await api.ExecuteAsync(services => Db(services).NoteReceipts.AsNoTracking().CountAsync(receipt => receipt.NoteId == note.Id));
        Assert.Equal(1, receipts);
    }

    [Fact]
    public async Task A_reaction_that_fails_rolls_back_the_command_that_caused_it()
    {
        var (userId, workspaceId) = api.NewMember();
        var client = api.ClientFor(userId);
        var key = Guid.NewGuid().ToString();
        var title = $"{ExplodingNoteReaction.TitleMarker}-{Guid.NewGuid():N}";

        var response = await client.SendAsync(ApiFixture.CreateNoteRequest(workspaceId, new { title }, key));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("unexpected", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Empty((await client.GetFromJsonAsync<List<NoteDto>>(ApiFixture.NotesUrl(workspaceId)))!);
        var (auditEntries, storedKeys) = await api.ExecuteAsync(async services => (
            await Db(services).AuditLog.AsNoTracking().CountAsync(entry => entry.WorkspaceId == workspaceId),
            await Db(services).IdempotencyKeys.AsNoTracking().CountAsync(record => record.UserId == userId)));
        Assert.Equal(0, auditEntries);
        Assert.Equal(0, storedKeys);
    }

    [Fact]
    public async Task A_note_is_visible_only_to_its_owner_even_inside_the_same_workspace()
    {
        var (brian, workspaceId) = api.NewMember();
        var juan = api.Workspaces.Add(workspaceId, Guid.NewGuid()).UserId;
        await api.ClientFor(brian).SendAsync(ApiFixture.CreateNoteRequest(workspaceId, new { title = "Privada de Brian" }, Guid.NewGuid().ToString()));

        var brianSees = await api.ClientFor(brian).GetFromJsonAsync<List<NoteDto>>(ApiFixture.NotesUrl(workspaceId));
        var juanSees = await api.ClientFor(juan).GetFromJsonAsync<List<NoteDto>>(ApiFixture.NotesUrl(workspaceId));

        Assert.Single(brianSees!);
        Assert.Empty(juanSees!);
    }

    [Fact]
    public async Task The_same_user_keeps_separate_notes_in_each_workspace()
    {
        var (userId, firstWorkspace) = api.NewMember();
        var secondWorkspace = api.Workspaces.Add(Guid.NewGuid(), userId).WorkspaceId;
        await api.ClientFor(userId).SendAsync(ApiFixture.CreateNoteRequest(firstWorkspace, new { title = "Solo en el primero" }, Guid.NewGuid().ToString()));

        var inSecond = await api.ClientFor(userId).GetFromJsonAsync<List<NoteDto>>(ApiFixture.NotesUrl(secondWorkspace));

        Assert.Empty(inSecond!);
    }

    [Fact]
    public async Task Replaying_an_idempotency_key_returns_the_first_response_and_applies_the_effect_once()
    {
        var (userId, workspaceId) = api.NewMember();
        var client = api.ClientFor(userId);
        var key = Guid.NewGuid().ToString();

        var first = await client.SendAsync(ApiFixture.CreateNoteRequest(workspaceId, new { title = "Una sola vez" }, key));
        var second = await client.SendAsync(ApiFixture.CreateNoteRequest(workspaceId, new { title = "Una sola vez" }, key));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal((await first.Content.ReadFromJsonAsync<NoteDto>())!.Id, (await second.Content.ReadFromJsonAsync<NoteDto>())!.Id);
        Assert.Single((await client.GetFromJsonAsync<List<NoteDto>>(ApiFixture.NotesUrl(workspaceId)))!);
    }

    [Fact]
    public async Task Reusing_an_idempotency_key_for_a_different_request_is_rejected()
    {
        var (userId, workspaceId) = api.NewMember();
        var client = api.ClientFor(userId);
        var key = Guid.NewGuid().ToString();
        await client.SendAsync(ApiFixture.CreateNoteRequest(workspaceId, new { title = "Original" }, key));

        var response = await client.SendAsync(ApiFixture.CreateNoteRequest(workspaceId, new { title = "Otra cosa" }, key));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("idempotency.key_reused", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Single((await client.GetFromJsonAsync<List<NoteDto>>(ApiFixture.NotesUrl(workspaceId)))!);
    }

    [Fact]
    public async Task Two_users_can_use_the_same_idempotency_key_without_seeing_each_others_result()
    {
        var (brian, workspaceId) = api.NewMember();
        var juan = api.Workspaces.Add(workspaceId, Guid.NewGuid()).UserId;
        const string key = "same-key-for-both";

        var fromBrian = await api.ClientFor(brian).SendAsync(ApiFixture.CreateNoteRequest(workspaceId, new { title = "De Brian" }, key + workspaceId));
        var fromJuan = await api.ClientFor(juan).SendAsync(ApiFixture.CreateNoteRequest(workspaceId, new { title = "De Brian" }, key + workspaceId));

        Assert.NotEqual((await fromBrian.Content.ReadFromJsonAsync<NoteDto>())!.Id, (await fromJuan.Content.ReadFromJsonAsync<NoteDto>())!.Id);
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
        var (receipts, auditEntries) = await api.ExecuteAsync(async services => (
            await Db(services).NoteReceipts.AsNoTracking().CountAsync(receipt => receipt.NoteId == marker),
            await Db(services).AuditLog.AsNoTracking().CountAsync(entry => entry.ResourceId == marker)));
        Assert.Equal(0, receipts);
        Assert.Equal(0, auditEntries);
    }

    [Fact]
    public async Task Saving_outside_the_command_pipeline_with_events_pending_is_refused()
    {
        var (userId, workspaceId) = api.NewMember();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => api.ExecuteAsync(async services =>
        {
            var db = Db(services);
            db.Notes.Add(Note.Create(workspaceId, userId, "Fuera del pipeline", DateTimeOffset.UtcNow).Value);
            return await db.SaveChangesAsync();
        }, userId, workspaceId));

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
        if (entity.Namespace?.StartsWith(modulePrefix, StringComparison.Ordinal) != true)
        {
            return "platform";
        }

        var module = entity.Namespace[modulePrefix.Length..].Split('.')[0];
        return string.Concat(module.Select((character, index) =>
            char.IsUpper(character) && index > 0 ? $"_{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));
    }
}
