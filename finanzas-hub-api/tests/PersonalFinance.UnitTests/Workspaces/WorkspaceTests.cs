using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Workspaces.Contracts.Events;
using PersonalFinance.Modules.Workspaces.Domain;

namespace PersonalFinance.UnitTests.Workspaces;

public class WorkspaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Brian = Guid.NewGuid();
    private static readonly Guid Juan = Guid.NewGuid();
    private static readonly Guid Pedro = Guid.NewGuid();

    [Fact]
    public void A_new_workspace_waits_for_setup_and_its_creator_is_the_only_owner()
    {
        var workspace = Workspace.Create("  Hogar Quispe ", Brian, Now).Value;

        Assert.Equal("Hogar Quispe", workspace.Name);
        Assert.Equal(WorkspaceStatus.PendingSetup, workspace.Status);
        Assert.Null(workspace.BaseCurrency);
        Assert.Equal(WorkspaceRole.Owner, Assert.Single(workspace.Memberships).Role);
        Assert.Equal(new WorkspaceCreated(workspace.Id, Brian), Assert.Single(workspace.DomainEvents));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void A_workspace_needs_a_name(string? name)
    {
        Assert.Equal("workspaces.name_invalid", Workspace.Create(name, Brian, Now).Error.Code);
    }

    [Fact]
    public void Onboarding_sets_currency_and_time_zone_once()
    {
        var workspace = Workspace.Create("Hogar", Brian, Now).Value;

        Assert.True(workspace.CompleteOnboarding("pen", "America/Lima").IsSuccess);
        Assert.Equal("PEN", workspace.BaseCurrency);
        Assert.Equal(WorkspaceStatus.Active, workspace.Status);
        Assert.Contains(workspace.DomainEvents, domainEvent => domainEvent is OnboardingCompleted { BaseCurrency: "PEN" });
        Assert.Equal("workspaces.onboarding_already_completed", workspace.CompleteOnboarding("USD", "America/Lima").Error.Code);
    }

    [Theory]
    [InlineData("XXX", "America/Lima", "currency.unsupported")]
    [InlineData("PEN", "Mars/Olympus", "workspaces.time_zone_invalid")]
    public void Onboarding_rejects_an_unknown_currency_or_time_zone(string currency, string timeZone, string expectedCode)
    {
        var workspace = Workspace.Create("Hogar", Brian, Now).Value;

        Assert.Equal(expectedCode, workspace.CompleteOnboarding(currency, timeZone).Error.Code);
        Assert.Equal(WorkspaceStatus.PendingSetup, workspace.Status);
    }

    [Fact]
    public void A_person_joins_once_and_never_as_owner()
    {
        var workspace = Workspace.Create("Hogar", Brian, Now).Value;

        Assert.True(workspace.Join(Juan, WorkspaceRole.Member, Now).IsSuccess);
        Assert.Equal("workspaces.already_member", workspace.Join(Juan, WorkspaceRole.Member, Now).Error.Code);
        Assert.Equal("workspaces.owner_by_transfer_only", workspace.Join(Pedro, WorkspaceRole.Owner, Now).Error.Code);
        Assert.Contains(workspace.DomainEvents, domainEvent => domainEvent is MemberJoined joined && joined.UserId == Juan);
    }

    [Fact]
    public void Someone_who_was_removed_can_come_back_with_the_same_membership()
    {
        var workspace = WorkspaceWith((Juan, WorkspaceRole.Member));
        workspace.Remove(Brian, Juan, Now);

        var rejoined = workspace.Join(Juan, WorkspaceRole.Admin, Now.AddDays(1));

        Assert.True(rejoined.IsSuccess);
        Assert.Equal(2, workspace.Memberships.Count);
        Assert.Equal(WorkspaceRole.Admin, workspace.ActiveMembershipOf(Juan)!.Role);
    }

    [Fact]
    public void Nobody_changes_their_own_role_and_the_owner_role_is_not_assignable()
    {
        var workspace = WorkspaceWith((Juan, WorkspaceRole.Admin));

        Assert.Equal("workspaces.cannot_change_own_role", workspace.ChangeRole(Brian, Brian, WorkspaceRole.Admin).Error.Code);
        Assert.Equal("workspaces.owner_by_transfer_only", workspace.ChangeRole(Brian, Juan, WorkspaceRole.Owner).Error.Code);
        Assert.Equal("workspaces.owner_by_transfer_only", workspace.ChangeRole(Juan, Brian, WorkspaceRole.Member).Error.Code);
    }

    [Fact]
    public void Only_the_owner_grants_or_removes_the_admin_role()
    {
        var workspace = WorkspaceWith((Juan, WorkspaceRole.Admin), (Pedro, WorkspaceRole.Member));

        Assert.Equal(ErrorType.Forbidden, workspace.ChangeRole(Juan, Pedro, WorkspaceRole.Admin).Error.Type);
        Assert.True(workspace.ChangeRole(Brian, Pedro, WorkspaceRole.Admin).IsSuccess);
        Assert.Equal(ErrorType.Forbidden, workspace.ChangeRole(Juan, Pedro, WorkspaceRole.Member).Error.Type);
        Assert.True(workspace.ChangeRole(Brian, Pedro, WorkspaceRole.Member).IsSuccess);
    }

    [Fact]
    public void A_member_cannot_remove_others_but_can_leave()
    {
        var workspace = WorkspaceWith((Juan, WorkspaceRole.Member), (Pedro, WorkspaceRole.Member));

        Assert.Equal(ErrorType.Forbidden, workspace.Remove(Juan, Pedro, Now).Error.Type);
        Assert.True(workspace.Remove(Juan, Juan, Now).IsSuccess);
        Assert.Null(workspace.ActiveMembershipOf(Juan));
        Assert.Contains(workspace.DomainEvents, domainEvent => domainEvent is MemberRemoved removed && removed.UserId == Juan);
    }

    [Fact]
    public void The_owner_cannot_leave_or_be_removed_without_transferring()
    {
        var workspace = WorkspaceWith((Juan, WorkspaceRole.Admin));

        Assert.Equal("workspaces.owner_cannot_leave", workspace.Remove(Brian, Brian, Now).Error.Code);
        Assert.Equal("workspaces.owner_cannot_leave", workspace.Remove(Juan, Brian, Now).Error.Code);
    }

    [Fact]
    public void An_admin_removes_members_but_not_another_admin()
    {
        var workspace = WorkspaceWith((Juan, WorkspaceRole.Admin), (Pedro, WorkspaceRole.Admin));
        var ana = Guid.NewGuid();
        workspace.Join(ana, WorkspaceRole.Member, Now);

        Assert.True(workspace.Remove(Juan, ana, Now).IsSuccess);
        Assert.Equal(ErrorType.Forbidden, workspace.Remove(Juan, Pedro, Now).Error.Type);
        Assert.True(workspace.Remove(Brian, Pedro, Now).IsSuccess);
    }

    [Fact]
    public void Transferring_ownership_leaves_exactly_one_owner()
    {
        var workspace = WorkspaceWith((Juan, WorkspaceRole.Member));

        Assert.Equal(ErrorType.Forbidden, workspace.TransferOwnership(Juan, Juan).Error.Type);
        Assert.True(workspace.TransferOwnership(Brian, Juan).IsSuccess);

        Assert.Equal(Juan, workspace.OwnerUserId);
        Assert.Equal(WorkspaceRole.Owner, workspace.ActiveMembershipOf(Juan)!.Role);
        Assert.Equal(WorkspaceRole.Admin, workspace.ActiveMembershipOf(Brian)!.Role);
        Assert.Single(workspace.Memberships, membership => membership.Role == WorkspaceRole.Owner);
    }

    [Fact]
    public void Acting_on_someone_who_is_not_a_member_is_not_found()
    {
        var workspace = Workspace.Create("Hogar", Brian, Now).Value;

        Assert.Equal("workspaces.member_not_found", workspace.ChangeRole(Brian, Juan, WorkspaceRole.Admin).Error.Code);
        Assert.Equal("workspace.not_found", workspace.Remove(Juan, Brian, Now).Error.Code);
    }

    private static Workspace WorkspaceWith(params (Guid UserId, WorkspaceRole Role)[] members)
    {
        var workspace = Workspace.Create("Hogar", Brian, Now).Value;
        foreach (var (userId, role) in members)
        {
            workspace.Join(userId, role, Now);
        }

        return workspace;
    }
}

public class InvitationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Workspace = Guid.NewGuid();
    private static readonly Guid Inviter = Guid.NewGuid();

    [Fact]
    public void An_invitation_stores_only_the_hash_of_its_token_and_lasts_seven_days()
    {
        var (invitation, token) = Invitation.Create(Workspace, " Juan@Example.com ", WorkspaceRole.Member, Inviter, Now).Value;

        Assert.Equal("juan@example.com", invitation.Email);
        Assert.NotEqual(token, invitation.TokenHash);
        Assert.Equal(Invitation.HashToken(token), invitation.TokenHash);
        Assert.Equal(Now.AddDays(7), invitation.ExpiresAt);
        Assert.True(invitation.IsOpen(Now.AddDays(6)));
        Assert.False(invitation.IsOpen(Now.AddDays(7)));
    }

    [Fact]
    public void Two_invitations_never_share_a_token()
    {
        var first = Invitation.Create(Workspace, "a@example.com", WorkspaceRole.Member, Inviter, Now).Value.Token;
        var second = Invitation.Create(Workspace, "a@example.com", WorkspaceRole.Member, Inviter, Now).Value.Token;

        Assert.NotEqual(first, second);
        Assert.True(first.Length >= 43);
    }

    [Fact]
    public void Nobody_is_invited_as_owner_or_with_a_broken_email()
    {
        Assert.Equal("workspaces.owner_by_transfer_only", Invitation.Create(Workspace, "a@example.com", WorkspaceRole.Owner, Inviter, Now).Error.Code);
        Assert.Equal("workspaces.invitation.email_invalid", Invitation.Create(Workspace, "not-an-email", WorkspaceRole.Member, Inviter, Now).Error.Code);
    }

    [Fact]
    public void Only_the_recipient_accepts_and_only_once()
    {
        var invitation = Invitation.Create(Workspace, "juan@example.com", WorkspaceRole.Member, Inviter, Now).Value.Invitation;
        var juan = Guid.NewGuid();

        Assert.Equal(ErrorType.Forbidden, invitation.Accept(Guid.NewGuid(), "pedro@example.com", Now).Error.Type);
        Assert.True(invitation.Accept(juan, "JUAN@example.com", Now).IsSuccess);
        Assert.Equal(juan, invitation.AcceptedByUserId);
        Assert.Equal("workspaces.invitation.not_available", invitation.Accept(juan, "juan@example.com", Now).Error.Code);
    }

    [Fact]
    public void An_expired_or_revoked_invitation_cannot_be_accepted()
    {
        var expired = Invitation.Create(Workspace, "juan@example.com", WorkspaceRole.Member, Inviter, Now).Value.Invitation;
        var revoked = Invitation.Create(Workspace, "juan@example.com", WorkspaceRole.Member, Inviter, Now).Value.Invitation;
        revoked.Revoke();

        Assert.Equal("workspaces.invitation.not_available", expired.Accept(Guid.NewGuid(), "juan@example.com", Now.AddDays(8)).Error.Code);
        Assert.Equal("workspaces.invitation.not_available", revoked.Accept(Guid.NewGuid(), "juan@example.com", Now).Error.Code);
        Assert.Equal("workspaces.invitation.not_pending", revoked.Revoke().Error.Code);
    }
}
