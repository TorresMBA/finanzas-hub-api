using System.Reflection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Infrastructure.Platform;
using PersonalFinance.Modules.Identity.Application.Abstractions;
using PersonalFinance.Modules.Identity.Domain;
using PersonalFinance.Modules.Sample.Application.Abstractions;
using PersonalFinance.Modules.Sample.Domain;
using PersonalFinance.Modules.Workspaces.Application.Abstractions;
using PersonalFinance.Modules.Workspaces.Domain;

namespace PersonalFinance.Infrastructure.Persistence;

/// <summary>One PostgreSQL schema per module, plus <see cref="Platform"/> for the tables that belong to none.</summary>
internal static class Schemas
{
    public const string Platform = "platform";
    public const string Identity = "identity";
    public const string Workspaces = "workspaces";
    public const string Sample = "sample";
}

/// <summary>
/// The one database context. A module reaches only its own tables, through the interface it declares
/// (<see cref="ISampleDb"/> and the like); every <see cref="IOwned"/> entity is filtered by the current user
/// and workspace. It derives from ASP.NET Core Identity's context so the framework's own user store works on it.
/// </summary>
internal sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser, IWorkspaceContext workspaceContext)
    : IdentityUserContext<User, Guid>(options), IDataProtectionKeyContext, IIdentityDb, IWorkspacesDb, ISampleDb
{
    private static readonly MethodInfo ApplyOwnerFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyOwnerFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public DbSet<OutgoingEmail> OutgoingEmails => Set<OutgoingEmail>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<Note> Notes => Set<Note>();

    public DbSet<NoteReceipt> NoteReceipts => Set<NoteReceipt>();

    // Read by the global query filters. With no user or workspace they match nothing.
    private Guid CurrentUserId => currentUser.UserId ?? Guid.Empty;

    private Guid CurrentWorkspaceId => workspaceContext.WorkspaceId ?? Guid.Empty;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureDomainEventsWereDispatched();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureDomainEventsWereDispatched();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity maps its own tables first; the configurations below move them to the identity schema.
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (typeof(IHasDomainEvents).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).Ignore(nameof(IHasDomainEvents.DomainEvents));
            }

            if (typeof(IOwned).IsAssignableFrom(entityType.ClrType) && entityType.BaseType is null)
            {
                ApplyOwnerFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            }
        }
    }

    private void ApplyOwnerFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IOwned =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity =>
            entity.OwnerUserId == CurrentUserId && entity.WorkspaceId == CurrentWorkspaceId);

    /// <summary>
    /// The command pipeline takes the events out of the aggregates before it saves. Events still present here
    /// mean something saved on its own, and their handlers would never run.
    /// </summary>
    private void EnsureDomainEventsWereDispatched()
    {
        var aggregate = ChangeTracker.Entries<IHasDomainEvents>().FirstOrDefault(entry => entry.Entity.DomainEvents.Count > 0);
        if (aggregate is not null)
        {
            throw new InvalidOperationException(
                $"{aggregate.Entity.GetType().Name} was saved outside the command pipeline with domain events pending. Handlers do not call SaveChanges; the transaction decorator does.");
        }
    }
}
