using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Workspaces.Domain;

public enum InvitationStatus
{
    Pending,
    Accepted,
    Revoked
}

/// <summary>
/// An offer to join a workspace, sent to an email address that may not have an account yet. Only the hash of
/// its token is stored: the token itself exists in the link that was emailed and nowhere else.
/// </summary>
public sealed class Invitation : AggregateRoot
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private Invitation()
    {
    }

    private Invitation(Guid id) : base(id)
    {
    }

    public Guid WorkspaceId { get; private set; }

    /// <summary>Lower case, so the same address cannot be invited twice with different casing.</summary>
    public string Email { get; private set; } = null!;

    public WorkspaceRole Role { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public InvitationStatus Status { get; private set; }

    public Guid InvitedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public Guid? AcceptedByUserId { get; private set; }

    /// <returns>The invitation and the token to put in the link. The token cannot be recovered later.</returns>
    public static Result<(Invitation Invitation, string Token)> Create(Guid workspaceId, string? email, WorkspaceRole role, Guid invitedByUserId, DateTimeOffset now)
    {
        var normalized = NormalizeEmail(email);
        if (normalized.Length is < 3 or > 254 || normalized.Count(character => character == '@') != 1)
        {
            return Error.Validation("workspaces.invitation.email_invalid", "The email address is not valid.");
        }

        if (role == WorkspaceRole.Owner)
        {
            return Error.BusinessRule("workspaces.owner_by_transfer_only", "Nobody can be invited as owner.");
        }

        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var invitation = new Invitation(NewId())
        {
            WorkspaceId = workspaceId,
            Email = normalized,
            Role = role,
            TokenHash = HashToken(token),
            Status = InvitationStatus.Pending,
            InvitedByUserId = invitedByUserId,
            CreatedAt = now,
            ExpiresAt = now.Add(Lifetime)
        };
        return (invitation, token);
    }

    public static string NormalizeEmail(string? email) => email?.Trim().ToLowerInvariant() ?? string.Empty;

    public static string HashToken(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public bool IsOpen(DateTimeOffset now) => Status == InvitationStatus.Pending && now < ExpiresAt;

    public Result Accept(Guid userId, string userEmail, DateTimeOffset now)
    {
        if (!IsOpen(now))
        {
            return Error.BusinessRule("workspaces.invitation.not_available", "The invitation has expired, was revoked or was already used.");
        }

        if (NormalizeEmail(userEmail) != Email)
        {
            return Error.Forbidden("workspaces.invitation.other_recipient", "This invitation was sent to a different email address.");
        }

        Status = InvitationStatus.Accepted;
        AcceptedByUserId = userId;
        return Result.Success();
    }

    public Result Revoke()
    {
        if (Status != InvitationStatus.Pending)
        {
            return Error.BusinessRule("workspaces.invitation.not_pending", "Only a pending invitation can be revoked.");
        }

        Status = InvitationStatus.Revoked;
        return Result.Success();
    }
}
