using System.Reflection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Infrastructure.Platform;
using PersonalFinance.Modules.Budgets.Application.Abstractions;
using PersonalFinance.Modules.Categories.Application.Abstractions;
using PersonalFinance.Modules.Goals.Application.Abstractions;
using PersonalFinance.Modules.Budgets.Domain;
using PersonalFinance.Modules.Categories.Domain;
using PersonalFinance.Modules.Goals.Domain;
using PersonalFinance.Modules.Identity.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Notifications.Application.Abstractions;
using PersonalFinance.Modules.Scheduling.Application.Abstractions;
using PersonalFinance.Modules.SharedExpenses.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Domain;
using PersonalFinance.Modules.Notifications.Domain;
using PersonalFinance.Modules.Scheduling.Domain;
using PersonalFinance.Modules.SharedExpenses.Domain;
using PersonalFinance.Modules.Identity.Domain;
using PersonalFinance.Modules.Workspaces.Application.Abstractions;
using PersonalFinance.Modules.Workspaces.Domain;

namespace PersonalFinance.Infrastructure.Persistence;

/// <summary>One PostgreSQL schema per module, plus <see cref="Platform"/> for the tables that belong to none.</summary>
internal static class Schemas
{
    public const string Platform = "platform";
    public const string Identity = "identity";
    public const string Workspaces = "workspaces";
    public const string Categories = "categories";
    public const string Ledger = "ledger";
    public const string Budgets = "budgets";
    public const string Notifications = "notifications";
    public const string Scheduling = "scheduling";
    public const string Goals = "goals";
    public const string SharedExpenses = "shared_expenses";
}

/// <summary>
/// The one database context. A module reaches only its own tables, through the interface it declares
/// (<see cref="ILedgerDb"/> and the like); every <see cref="IOwned"/> entity is filtered by the current user
/// and their personal workspace. It derives from ASP.NET Core Identity's context so the framework's own user store works on it.
/// </summary>
internal sealed class AppDbContext(
    DbContextOptions<AppDbContext> options, ICurrentUser currentUser, IWorkspaceContext workspaceContext, IPersonalSpace personalSpace)
    : IdentityUserContext<User, Guid>(options), IDataProtectionKeyContext, IIdentityDb, IWorkspacesDb, ICategoriesDb, ILedgerDb, IBudgetsDb, INotificationsDb, ISchedulingDb, IGoalsDb, ISharedExpensesDb
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

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Operation> Operations => Set<Operation>();

    public DbSet<Entry> Entries => Set<Entry>();

    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    public DbSet<Budget> Budgets => Set<Budget>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<NotificationSettings> NotificationSettings => Set<NotificationSettings>();

    public DbSet<RecurringRule> RecurringRules => Set<RecurringRule>();

    public DbSet<Goal> Goals => Set<Goal>();

    public DbSet<SharedGroup> SharedGroups => Set<SharedGroup>();

    // Read by the global query filters. With no user or workspace they match nothing.
    private Guid CurrentUserId => currentUser.UserId ?? Guid.Empty;

    private Guid CurrentWorkspaceId => workspaceContext.WorkspaceId ?? Guid.Empty;

    // Private finances belong to the person: they live in their personal workspace, whichever one is open.
    private Guid PersonalWorkspaceId => personalSpace.WorkspaceId ?? Guid.Empty;

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

        // A goal is shared by participation, not by membership: it exists only for its active participants. Being
        // a member of the workspace, whatever the role, is not enough to load it.
        modelBuilder.Entity<Goal>().HasQueryFilter(goal =>
            goal.WorkspaceId == CurrentWorkspaceId
            && goal.Participants.Any(participant => participant.UserId == CurrentUserId && participant.Status == ParticipantStatus.Active));

        // The same for a group that splits expenses: only its active members ever load it.
        modelBuilder.Entity<SharedGroup>().HasQueryFilter(group =>
            group.WorkspaceId == CurrentWorkspaceId
            && group.Members.Any(member => member.UserId == CurrentUserId && member.Status == GroupMemberStatus.Active));
    }

    private void ApplyOwnerFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IOwned =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity =>
            entity.OwnerUserId == CurrentUserId && entity.WorkspaceId == PersonalWorkspaceId);

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
