using Microsoft.EntityFrameworkCore;
using PersonalFinance.Modules.Workspaces.Domain;

namespace PersonalFinance.Modules.Workspaces.Application.Abstractions;

/// <summary>The tables this module may read and write.</summary>
public interface IWorkspacesDb
{
    DbSet<Workspace> Workspaces { get; }

    DbSet<Membership> Memberships { get; }

    DbSet<Invitation> Invitations { get; }
}
