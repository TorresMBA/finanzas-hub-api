using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.SharedExpenses.Application.Abstractions;
using PersonalFinance.Modules.SharedExpenses.Contracts;
using PersonalFinance.Modules.SharedExpenses.Domain;
using PersonalFinance.Modules.Workspaces.Contracts;

namespace PersonalFinance.Modules.SharedExpenses.Application;

internal static class SharedErrors
{
    public static readonly Error NotFound = Error.NotFound("shared.group.not_found", "The group was not found.");
}

/// <param name="Balance">
/// For the person asking, what the group owes them (negative: what they owe). For another member, only what is
/// between the two: positive when that member owes the person asking, negative the other way round.
/// </param>
public sealed record SharedMemberResponse(Guid UserId, string DisplayName, GroupRole Role, decimal Balance, bool IsMe);

/// <summary>What one member still owes another, as it stands: the unpaid parts of expenses the second one paid.</summary>
public sealed record SharedDebtResponse(Guid FromUserId, string FromName, Guid ToUserId, string ToName, decimal Amount);

public sealed record SharedShareResponse(Guid UserId, string DisplayName, decimal Amount, decimal PaidAmount);

/// <param name="MyShare">The part of the person asking; zero when the expense was not split with them.</param>
/// <param name="MyPending">What the person asking still owes of it to who paid.</param>
/// <param name="CanCancel">Whether the person asking registered it or paid it.</param>
public sealed record SharedExpenseResponse(
    Guid Id, string Description, decimal TotalAmount, Guid PaidByUserId, string PaidByName, DateOnly OccurredOn, SplitType SplitType, SharedExpenseStatus Status,
    decimal MyShare, decimal MyPending, IReadOnlyList<SharedShareResponse> Shares, bool CanCancel);

/// <summary>A payment between two members: who paid whom, how much and when.</summary>
/// <param name="CanVoid">Whether the person asking registered it and it is still recent enough to be voided.</param>
/// <param name="CanLinkAccount">Whether the person asking is one of the two and has not put it in one of their accounts yet.</param>
public sealed record SharedSettlementResponse(
    Guid Id, Guid FromUserId, string FromName, Guid ToUserId, string ToName, decimal Amount, DateOnly OccurredOn, bool IsVoided, bool CanVoid, bool CanLinkAccount);

/// <summary>A group in the list, with what the person asking needs to know at a glance. Everything here is about them.</summary>
/// <param name="MyBalance">Positive: the group owes the person asking. Negative: they owe.</param>
/// <param name="OwedToMe">What the others still owe the person asking in the group.</param>
/// <param name="IOwe">What the person asking still owes the others.</param>
/// <param name="PendingExpenses">Expenses of the person asking that are not fully repaid yet: the ones they paid and the ones they owe a part of.</param>
/// <param name="MemberNames">Who is in the group, the person asking last.</param>
/// <param name="LastActivityOn">The day of the latest expense or payment the person asking takes part in.</param>
public sealed record SharedGroupSummaryResponse(
    Guid Id, string Name, string Icon, string Currency, GroupStatus Status, int MemberCount, decimal MyBalance, decimal OwedToMe, decimal IOwe, int PendingExpenses,
    IReadOnlyList<string> MemberNames, DateOnly? LastActivityOn);

/// <summary>What the person asking is owed and owes in one currency, over all their groups.</summary>
public sealed record SharedTotalsResponse(string Currency, decimal OwedToMe, decimal IOwe);

public sealed record SharedWorkspaceMemberResponse(Guid UserId, string DisplayName);

/// <param name="Members">The members of the workspace, to choose who is in a new group.</param>
public sealed record SharedGroupsResponse(
    IReadOnlyList<SharedGroupSummaryResponse> Groups, IReadOnlyList<SharedTotalsResponse> Totals, IReadOnlyList<SharedWorkspaceMemberResponse> Members,
    string? BaseCurrency);

/// <summary>
/// A group as one of its members sees it: only what involves them. An expense split among others, a payment
/// between two others and what those two owe each other are theirs alone. It never says from which account anyone paid.
/// </summary>
/// <param name="Debts">What the person asking owes and is owed, as it stands.</param>
/// <param name="Expenses">The latest hundred the person asking paid, registered or has a part in, newest first.</param>
/// <param name="Settlements">The latest fifty payments the person asking made or received, newest first.</param>
/// <param name="VoidsLeftToday">How many more payments the person asking can void in this group today.</param>
public sealed record SharedGroupDetailResponse(
    SharedGroupSummaryResponse Group, bool IsAdmin, IReadOnlyList<SharedMemberResponse> Members, IReadOnlyList<SharedDebtResponse> Debts,
    IReadOnlyList<SharedExpenseResponse> Expenses, IReadOnlyList<SharedSettlementResponse> Settlements, int VoidsLeftToday);

/// <summary>
/// Reads groups for the current person. Every query goes through the context's global filter, so a group the
/// person is not a member of is never loaded: to them it does not exist.
/// </summary>
internal sealed class SharedGroupReader(ISharedExpensesDb db, IWorkspaceDirectory directory, ICurrentUser currentUser, IWorkspaceContext workspace, IClock clock)
{
    private const int LatestExpenses = 100;
    private const int LatestSettlements = 50;

    public Guid? UserId => currentUser.UserId;

    public Guid? WorkspaceId => workspace.WorkspaceId;

    public string? BaseCurrency => workspace.BaseCurrency;

    /// <summary>Today in the workspace's time zone.</summary>
    public DateOnly Today()
    {
        var zone = workspace.TimeZone is { } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found) ? found : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
    }

    /// <summary>The group, tracked, or null when the current person is not one of its active members.</summary>
    public Task<SharedGroup?> FindAsync(Guid groupId, CancellationToken cancellationToken) =>
        db.SharedGroups.SingleOrDefaultAsync(group => group.Id == groupId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> MemberNamesAsync(CancellationToken cancellationToken) =>
        workspace.WorkspaceId is { } workspaceId
            ? (await directory.ListMembersAsync(workspaceId, cancellationToken)).ToDictionary(member => member.UserId, member => member.DisplayName)
            : new Dictionary<Guid, string>();

    /// <summary>Someone who left the workspace keeps their part in the history, without a name.</summary>
    public static string NameOf(IReadOnlyDictionary<Guid, string> names, Guid userId) =>
        names.GetValueOrDefault(userId) is { Length: > 0 } name ? name : "Alguien que ya no está";

    public SharedGroupSummaryResponse Summarize(SharedGroup group, IReadOnlyDictionary<Guid, string> names)
    {
        var me = currentUser.UserId ?? Guid.Empty;
        var active = group.Members.Where(member => member.IsActive).ToList();
        var mine = group.Expenses.Where(expense => !expense.IsCancelled && (expense.PaidByUserId == me || expense.ShareOf(me) is not null)).ToList();
        var days = mine.Select(expense => expense.OccurredOn)
            .Concat(group.Settlements.Where(settlement => settlement.FromUserId == me || settlement.ToUserId == me).Select(settlement => settlement.OccurredOn))
            .ToList();
        return new SharedGroupSummaryResponse(
            group.Id, group.Name, group.Icon, group.Currency, group.Status, active.Count, group.NetOf(me),
            group.Members.Sum(member => group.Owed(member.UserId, me)), group.Members.Sum(member => group.Owed(me, member.UserId)),
            mine.Count(expense => expense.PaidByUserId == me ? expense.Status != SharedExpenseStatus.Settled : expense.ShareOf(me)!.Pending > 0),
            active.OrderBy(member => member.UserId == me).ThenBy(member => member.JoinedAt).Select(member => NameOf(names, member.UserId)).ToList(),
            days.Count == 0 ? null : days.Max());
    }

    public async Task<SharedGroupDetailResponse> DescribeAsync(SharedGroup group, CancellationToken cancellationToken)
    {
        var names = await MemberNamesAsync(cancellationToken);
        var me = currentUser.UserId ?? Guid.Empty;
        var active = group.Members.Where(member => member.IsActive).OrderBy(member => member.Role).ThenBy(member => member.JoinedAt).ToList();
        var now = clock.UtcNow;
        var everyone = group.Members.Select(member => member.UserId).ToList();

        // Only the debts of the person asking: what two others owe each other is between them.
        var debts = everyone.Where(other => other != me)
            .SelectMany(other => new[] { (debtor: me, creditor: other, Amount: group.Owed(me, other)), (debtor: other, creditor: me, Amount: group.Owed(other, me)) })
            .Where(debt => debt.Amount > 0)
            .Select(debt => new SharedDebtResponse(debt.debtor, NameOf(names, debt.debtor), debt.creditor, NameOf(names, debt.creditor), debt.Amount));

        return new SharedGroupDetailResponse(
            Summarize(group, names), group.IsAdmin(me),
            active.Select(member => new SharedMemberResponse(
                member.UserId, NameOf(names, member.UserId), member.Role,
                member.UserId == me ? group.NetOf(me) : group.Owed(member.UserId, me) - group.Owed(me, member.UserId), member.UserId == me)).ToList(),
            debts.ToList(),
            group.Expenses.Where(expense => expense.PaidByUserId == me || expense.CreatedByUserId == me || expense.ShareOf(me) is not null)
                .OrderByDescending(expense => expense.OccurredOn).ThenByDescending(expense => expense.CreatedAt).Take(LatestExpenses)
                .Select(expense => new SharedExpenseResponse(
                    expense.Id, expense.Description, expense.TotalAmount, expense.PaidByUserId, NameOf(names, expense.PaidByUserId), expense.OccurredOn,
                    expense.SplitType, expense.Status, expense.ShareOf(me)?.Amount ?? 0m,
                    expense.IsCancelled || expense.PaidByUserId == me ? 0m : expense.ShareOf(me)?.Pending ?? 0m,
                    expense.Shares.Select(share => new SharedShareResponse(share.UserId, NameOf(names, share.UserId), share.Amount, share.PaidAmount)).ToList(),
                    !expense.IsCancelled && (expense.CreatedByUserId == me || expense.PaidByUserId == me)))
                .ToList(),
            group.Settlements.Where(settlement => settlement.FromUserId == me || settlement.ToUserId == me)
                .OrderByDescending(settlement => settlement.OccurredOn).ThenByDescending(settlement => settlement.CreatedAt).Take(LatestSettlements)
                .Select(settlement => new SharedSettlementResponse(
                    settlement.Id, settlement.FromUserId, NameOf(names, settlement.FromUserId), settlement.ToUserId, NameOf(names, settlement.ToUserId),
                    settlement.Amount, settlement.OccurredOn, settlement.IsVoided,
                    settlement.RecordedByUserId == me && SharedGroup.CanStillVoid(settlement, now) && group.VoidsLeft(me, now) > 0,
                    !settlement.IsVoided
                    && ((settlement.FromUserId == me && settlement.FromOperationId is null) || (settlement.ToUserId == me && settlement.ToOperationId is null))))
                .ToList(),
            group.VoidsLeft(me, now));
    }
}

public sealed record ListSharedGroupsQuery : IQuery<SharedGroupsResponse>;

internal sealed class ListSharedGroupsHandler(ISharedExpensesDb db, SharedGroupReader reader) : IQueryHandler<ListSharedGroupsQuery, SharedGroupsResponse>
{
    public async Task<Result<SharedGroupsResponse>> HandleAsync(ListSharedGroupsQuery query, CancellationToken cancellationToken)
    {
        var me = reader.UserId ?? Guid.Empty;
        var groups = await db.SharedGroups.AsNoTracking().OrderBy(group => group.CreatedAt).ToListAsync(cancellationToken);
        var names = await reader.MemberNamesAsync(cancellationToken);

        // Per currency: what is owed in soles is not added to what is owed in dollars.
        var totals = groups.GroupBy(group => group.Currency)
            .Select(byCurrency => new SharedTotalsResponse(
                byCurrency.Key,
                byCurrency.Sum(group => group.Members.Sum(member => group.Owed(member.UserId, me))),
                byCurrency.Sum(group => group.Members.Sum(member => group.Owed(me, member.UserId)))))
            .OrderBy(total => total.Currency, StringComparer.Ordinal)
            .ToList();
        return new SharedGroupsResponse(
            groups.Select(group => reader.Summarize(group, names)).ToList(), totals,
            names.Select(member => new SharedWorkspaceMemberResponse(member.Key, member.Value))
                .OrderBy(member => member.DisplayName, StringComparer.InvariantCultureIgnoreCase).ToList(),
            reader.BaseCurrency);
    }
}

public sealed record GetSharedGroupQuery(Guid GroupId) : IQuery<SharedGroupDetailResponse>;

internal sealed class GetSharedGroupHandler(ISharedExpensesDb db, SharedGroupReader reader) : IQueryHandler<GetSharedGroupQuery, SharedGroupDetailResponse>
{
    public async Task<Result<SharedGroupDetailResponse>> HandleAsync(GetSharedGroupQuery query, CancellationToken cancellationToken) =>
        await db.SharedGroups.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == query.GroupId, cancellationToken) is { } group
            ? await reader.DescribeAsync(group, cancellationToken)
            : SharedErrors.NotFound;
}

internal sealed class SharedGroupQueries(ISharedExpensesDb db) : ISharedGroupQueries
{
    // The global filter leaves out the groups the current person is not in.
    public async Task<IReadOnlyList<SharedGroupMatch>> SearchAsync(string text, int count, CancellationToken cancellationToken) =>
        (await db.SharedGroups.AsNoTracking().ToListAsync(cancellationToken))
        .Where(group => group.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
        .OrderBy(group => group.Status)
        .ThenBy(group => group.Name, StringComparer.InvariantCultureIgnoreCase)
        .Take(count)
        .Select(group => new SharedGroupMatch(group.Id, group.Name, group.Icon, group.Status == GroupStatus.Archived))
        .ToList();
}
