using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Identity.Contracts;
using PersonalFinance.Modules.Workspaces.Application.Abstractions;
using PersonalFinance.Modules.Workspaces.Domain;

namespace PersonalFinance.Modules.Workspaces.Application.Invitations;

/// <param name="AcceptLink">
/// The link the invited person opens to join. Only in the answer to creating the invitation, so whoever invited
/// can hand it over themselves when the email does not arrive; null everywhere else, because the token is not kept.
/// </param>
public sealed record InvitationResponse(
    Guid Id, string Email, WorkspaceRole Role, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool Expired, string? AcceptLink = null)
{
    internal static InvitationResponse From(Invitation invitation, DateTimeOffset now, string? acceptLink = null) =>
        new(invitation.Id, invitation.Email, invitation.Role, invitation.CreatedAt, invitation.ExpiresAt, now >= invitation.ExpiresAt, acceptLink);
}

public sealed record ListInvitationsQuery : IQuery<IReadOnlyList<InvitationResponse>>;

internal sealed class ListInvitationsHandler(IWorkspacesDb db, IWorkspaceContext context, IClock clock) : IQueryHandler<ListInvitationsQuery, IReadOnlyList<InvitationResponse>>
{
    public async Task<Result<IReadOnlyList<InvitationResponse>>> HandleAsync(ListInvitationsQuery query, CancellationToken cancellationToken)
    {
        var allowed = context.Require(WorkspaceRole.Admin);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var pending = await db.Invitations.AsNoTracking()
            .Where(invitation => invitation.WorkspaceId == context.WorkspaceId && invitation.Status == InvitationStatus.Pending)
            .OrderByDescending(invitation => invitation.CreatedAt)
            .ToListAsync(cancellationToken);
        var now = clock.UtcNow;
        return pending.Select(invitation => InvitationResponse.From(invitation, now)).ToList();
    }
}

public sealed record InviteMemberCommand(string Email, WorkspaceRole Role) : ICommand<InvitationResponse>;

internal sealed class InviteMemberHandler(
    IWorkspacesDb db, IWorkspaceContext context, ICurrentUser currentUser, IUserDirectory users, IEmailSender emails, IAppLinks links, IAuditLog audit, IClock clock)
    : ICommandHandler<InviteMemberCommand, InvitationResponse>
{
    public async Task<Result<InvitationResponse>> HandleAsync(InviteMemberCommand command, CancellationToken cancellationToken)
    {
        var allowed = context.Require(command.Role == WorkspaceRole.Admin ? WorkspaceRole.Owner : WorkspaceRole.Admin);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        if (currentUser.UserId is not { } actor || await db.CurrentAsync(context, cancellationToken) is not { } workspace)
        {
            return WorkspaceErrors.NotFound;
        }

        var now = clock.UtcNow;
        var created = Invitation.Create(workspace.Id, command.Email, command.Role, actor, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var (invitation, token) = created.Value;
        if (await users.FindByEmailAsync(invitation.Email, cancellationToken) is { } existing && workspace.ActiveMembershipOf(existing.Id) is not null)
        {
            return Error.Conflict("workspaces.already_member", "This person is already a member of the workspace.");
        }

        var open = await db.Invitations
            .Where(row => row.WorkspaceId == workspace.Id && row.Email == invitation.Email && row.Status == InvitationStatus.Pending)
            .ToListAsync(cancellationToken);
        if (open.Any(row => row.IsOpen(now)))
        {
            return Error.Conflict("workspaces.invitation.already_pending", "This email already has a pending invitation.");
        }

        // Expired ones still count as pending for the unique index; a new invitation replaces them.
        open.ForEach(row => row.Revoke());
        db.Invitations.Add(invitation);

        var inviter = await users.FindAsync(actor, cancellationToken);
        var acceptLink = links.To($"/invite/{token}");
        emails.Queue(new EmailMessage(
            invitation.Email,
            $"{inviter?.DisplayName ?? "Alguien"} te invitó a «{workspace.Name}» en PersonalHub",
            $"Hola:\n\n{inviter?.DisplayName ?? "Alguien"} te invitó a unirte a «{workspace.Name}» en PersonalHub.\n\n"
            + $"Acepta la invitación con este enlace, válido durante {Invitation.Lifetime.Days} días:\n{acceptLink}\n\n"
            + "Unirte no da acceso a tus finanzas personales a nadie: solo a lo que decidas compartir."));
        audit.Record(new AuditEntry("workspaces", "workspaces.invitation.created", "invitation", invitation.Id)
        {
            After = new { Role = invitation.Role.ToString() }
        });
        return InvitationResponse.From(invitation, now, acceptLink);
    }
}

public sealed record RevokeInvitationCommand(Guid InvitationId) : ICommand<Unit>;

internal sealed class RevokeInvitationHandler(IWorkspacesDb db, IWorkspaceContext context, IAuditLog audit) : ICommandHandler<RevokeInvitationCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RevokeInvitationCommand command, CancellationToken cancellationToken)
    {
        var allowed = context.Require(WorkspaceRole.Admin);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var invitation = await db.Invitations
            .SingleOrDefaultAsync(row => row.Id == command.InvitationId && row.WorkspaceId == context.WorkspaceId, cancellationToken);
        if (invitation is null)
        {
            return Error.NotFound("workspaces.invitation.not_found", "The invitation was not found.");
        }

        var revoked = invitation.Revoke();
        if (revoked.IsFailure)
        {
            return revoked.Error;
        }

        audit.Record(new AuditEntry("workspaces", "workspaces.invitation.revoked", "invitation", invitation.Id));
        return Unit.Value;
    }
}

/// <summary>What the person holding the link may see before signing in: enough to decide, nothing about the members.</summary>
public sealed record InvitationPreviewResponse(string WorkspaceName, string Email, WorkspaceRole Role, string? InvitedBy, bool Available);

public sealed record GetInvitationPreviewQuery(string Token) : IQuery<InvitationPreviewResponse>;

internal sealed class GetInvitationPreviewHandler(IWorkspacesDb db, IUserDirectory users, IClock clock) : IQueryHandler<GetInvitationPreviewQuery, InvitationPreviewResponse>
{
    public async Task<Result<InvitationPreviewResponse>> HandleAsync(GetInvitationPreviewQuery query, CancellationToken cancellationToken)
    {
        var hash = Invitation.HashToken(query.Token);
        var invitation = await db.Invitations.AsNoTracking().SingleOrDefaultAsync(row => row.TokenHash == hash, cancellationToken);
        if (invitation is null)
        {
            return InvitationErrors.NotFound;
        }

        var workspace = await db.Workspaces.AsNoTracking().SingleAsync(row => row.Id == invitation.WorkspaceId, cancellationToken);
        var inviter = await users.FindAsync(invitation.InvitedByUserId, cancellationToken);
        return new InvitationPreviewResponse(workspace.Name, invitation.Email, invitation.Role, inviter?.DisplayName, invitation.IsOpen(clock.UtcNow));
    }
}

public sealed record AcceptedInvitationResponse(Guid WorkspaceId, string WorkspaceName, WorkspaceRole Role);

public sealed record AcceptInvitationCommand(string Token) : ICommand<AcceptedInvitationResponse>;

internal sealed class AcceptInvitationHandler(IWorkspacesDb db, ICurrentUser currentUser, IUserDirectory users, InvitationAcceptance acceptance)
    : ICommandHandler<AcceptInvitationCommand, AcceptedInvitationResponse>
{
    public async Task<Result<AcceptedInvitationResponse>> HandleAsync(AcceptInvitationCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || await users.FindAsync(userId, cancellationToken) is not { } user)
        {
            return WorkspaceErrors.Unauthenticated;
        }

        var accepted = await acceptance.AcceptAsync(command.Token, user.Id, user.Email, cancellationToken);
        if (accepted.IsFailure)
        {
            return accepted.Error;
        }

        var workspace = await db.Workspaces.SingleAsync(row => row.Id == accepted.Value.WorkspaceId, cancellationToken);
        return new AcceptedInvitationResponse(workspace.Id, workspace.Name, accepted.Value.Role);
    }
}

internal static class InvitationErrors
{
    public static readonly Error NotFound = Error.NotFound("workspaces.invitation.not_found", "The invitation was not found.");
}

/// <summary>Shared by accepting while signed in and by registering from an invitation link.</summary>
internal sealed class InvitationAcceptance(IWorkspacesDb db, IAuditLog audit, IClock clock)
{
    public async Task<Result<Invitation>> AcceptAsync(string token, Guid userId, string userEmail, CancellationToken cancellationToken)
    {
        var hash = Invitation.HashToken(token);
        var invitation = await db.Invitations.SingleOrDefaultAsync(row => row.TokenHash == hash, cancellationToken);
        if (invitation is null)
        {
            return InvitationErrors.NotFound;
        }

        var now = clock.UtcNow;
        var accepted = invitation.Accept(userId, userEmail, now);
        if (accepted.IsFailure)
        {
            return accepted.Error;
        }

        var workspace = await db.Workspaces.Include(row => row.Memberships).SingleAsync(row => row.Id == invitation.WorkspaceId, cancellationToken);
        var joined = workspace.Join(userId, invitation.Role, now);
        if (joined.IsFailure)
        {
            return joined.Error;
        }

        audit.Record(new AuditEntry("workspaces", "workspaces.member.joined", "membership", userId)
        {
            WorkspaceId = workspace.Id,
            After = new { Role = invitation.Role.ToString() }
        });

        // Someone whose personal workspace is still to be set up takes the currency and the time zone of the one
        // they join, so their own books are ready to use at once. The one just created by a registration is not in
        // the database yet: it is found among the tracked ones.
        var personal = db.Workspaces.Local.FirstOrDefault(row => row.IsPersonal && row.OwnerUserId == userId)
            ?? await db.Workspaces.FirstOrDefaultAsync(row => row.IsPersonal && row.OwnerUserId == userId, cancellationToken);
        if (personal is { Status: WorkspaceStatus.PendingSetup } && workspace.BaseCurrency is not null)
        {
            personal.CompleteOnboarding(workspace.BaseCurrency, workspace.TimeZone);
        }

        return invitation;
    }
}
