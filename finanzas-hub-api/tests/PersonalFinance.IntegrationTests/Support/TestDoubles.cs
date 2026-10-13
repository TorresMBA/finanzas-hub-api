using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Events;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Infrastructure.Persistence;
using PersonalFinance.Modules.Sample.Application.Abstractions;
using PersonalFinance.Modules.Sample.Domain;

namespace PersonalFinance.IntegrationTests.Support;

/// <summary>
/// Signs the caller in as the user named in a header, for tests that are not about signing in. Without the
/// header it falls back to the real session cookie, so the sign-in flows are tested as they run in production.
/// </summary>
internal sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var value) || !Guid.TryParse(value, out var userId))
        {
            var cookie = await Context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return cookie.Succeeded ? AuthenticateResult.Success(new AuthenticationTicket(cookie.Principal, SchemeName)) : AuthenticateResult.NoResult();
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

/// <summary>
/// Memberships declared by a test without creating users and workspaces, for tests of the platform itself.
/// Anything not declared here is answered by the real Workspaces module.
/// </summary>
internal sealed class FakeWorkspaceAccess
{
    private readonly ConcurrentDictionary<(Guid WorkspaceId, Guid UserId), WorkspaceMembershipInfo> _memberships = new();

    public WorkspaceMembershipInfo Add(Guid workspaceId, Guid userId, WorkspaceRole role = WorkspaceRole.Member)
    {
        var membership = new WorkspaceMembershipInfo(workspaceId, userId, role, "PEN", "America/Lima");
        _memberships[(workspaceId, userId)] = membership;
        return membership;
    }

    public WorkspaceMembershipInfo? Find(Guid workspaceId, Guid userId) => _memberships.GetValueOrDefault((workspaceId, userId));
}

internal sealed class FallbackWorkspaceAccess(FakeWorkspaceAccess declared, IWorkspaceAccess real) : IWorkspaceAccess
{
    public async Task<WorkspaceMembershipInfo?> GetActiveMembershipAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
        declared.Find(workspaceId, userId) ?? await real.GetActiveMembershipAsync(workspaceId, userId, cancellationToken);
}

/// <summary>
/// Writes to two schemas, flushes them to the database inside the transaction and then rejects the command.
/// Nothing it wrote may survive.
/// </summary>
internal sealed record WriteThenFailCommand(Guid MarkerId) : ICommand<Unit>;

internal sealed class WriteThenFailHandler(ISampleDb db, AppDbContext context, IAuditLog audit, IClock clock)
    : ICommandHandler<WriteThenFailCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(WriteThenFailCommand command, CancellationToken cancellationToken)
    {
        db.NoteReceipts.Add(new NoteReceipt(command.MarkerId, clock.UtcNow));
        audit.Record(new AuditEntry("sample", "sample.test.written", "marker", command.MarkerId));
        await context.SaveChangesAsync(cancellationToken);
        return Error.BusinessRule("test.forced_failure", "Rejected after writing.");
    }
}

/// <summary>
/// A reaction with a bug: it throws for notes whose title carries the marker. It finds the title by querying,
/// which also proves that a reaction sees what its command just wrote.
/// </summary>
internal sealed class ExplodingNoteReaction(ISampleDb db) : IDomainEventHandler<NoteCreated>
{
    public const string TitleMarker = "explota";

    public async Task HandleAsync(NoteCreated domainEvent, CancellationToken cancellationToken)
    {
        var title = await db.Notes.AsNoTracking()
            .Where(note => note.Id == domainEvent.NoteId)
            .Select(note => note.Title)
            .SingleAsync(cancellationToken);
        if (title.StartsWith(TitleMarker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Simulated failure in a reaction.");
        }
    }
}
