using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Goals.Application.Abstractions;
using PersonalFinance.Modules.Goals.Contracts;
using PersonalFinance.Modules.Goals.Domain;
using PersonalFinance.Modules.Workspaces.Contracts;

namespace PersonalFinance.Modules.Goals.Application;

internal static class GoalErrors
{
    public static readonly Error Unauthenticated = Error.Unauthorized("auth.unauthenticated", "Authentication is required.");

    public static readonly Error NotFound = Error.NotFound("goals.not_found", "The goal was not found.");
}

/// <param name="Saved">What this person put in minus what they took out.</param>
public sealed record GoalParticipantResponse(Guid UserId, string DisplayName, ParticipantRole Role, decimal Saved, bool IsMe);

/// <summary>A goal as one of its participants sees it. It says how much each one put in, never from which account.</summary>
/// <param name="Saved">By all its participants.</param>
/// <param name="MySaved">By the person asking: the most they can withdraw.</param>
/// <param name="RequiredMonthly">What has to go in each month to reach the target on its date. Null without a date or once reached.</param>
/// <param name="MyFundAccountId">The account where the person asking keeps their part. Null until their first contribution.</param>
public sealed record GoalResponse(
    Guid Id, string Name, decimal TargetAmount, string Currency, DateOnly? TargetDate, GoalStatus Status, bool IsShared, bool IsOwner, decimal Saved, decimal MySaved,
    decimal Remaining, decimal Percent, decimal? RequiredMonthly, decimal AverageMonthly, GoalHealth Health, IReadOnlyList<GoalParticipantResponse> Participants,
    Guid? MyFundAccountId);

public sealed record GoalMemberResponse(Guid UserId, string DisplayName);

/// <param name="Members">The members of the workspace, to choose participants from.</param>
/// <param name="BaseCurrency">The currency a new goal is offered in.</param>
public sealed record GoalsResponse(IReadOnlyList<GoalResponse> Goals, IReadOnlyList<GoalMemberResponse> Members, string? BaseCurrency);

public sealed record GoalContributionResponse(Guid Id, Guid UserId, string DisplayName, ContributionType Type, decimal Amount, DateOnly OccurredOn, bool IsMine);

/// <param name="Month">As <c>2026-10</c>.</param>
/// <param name="Amount">What went in minus what came out that month.</param>
public sealed record GoalMonthResponse(string Month, decimal Amount);

/// <param name="Contributions">The latest fifty, newest first, of every participant.</param>
/// <param name="Monthly">The six months that end in the current one, oldest first.</param>
public sealed record GoalDetailResponse(GoalResponse Goal, IReadOnlyList<GoalContributionResponse> Contributions, IReadOnlyList<GoalMonthResponse> Monthly);

/// <summary>
/// Reads goals for the current person. Every query goes through the context's global filter, so a goal the
/// person does not take part in is never loaded: to them it does not exist.
/// </summary>
internal sealed class GoalReader(IGoalsDb db, IWorkspaceDirectory directory, ICurrentUser currentUser, IWorkspaceContext workspace, IClock clock)
{
    public (Guid WorkspaceId, Guid UserId)? Owner =>
        currentUser.UserId is { } userId && workspace.WorkspaceId is { } workspaceId ? (workspaceId, userId) : null;

    public string? BaseCurrency => workspace.BaseCurrency;

    /// <summary>Today in the workspace's time zone.</summary>
    public DateOnly Today()
    {
        var zone = workspace.TimeZone is { } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found) ? found : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
    }

    /// <summary>The goal, tracked, or null when the current person is not one of its active participants.</summary>
    public Task<Goal?> FindAsync(Guid goalId, CancellationToken cancellationToken) =>
        db.Goals.SingleOrDefaultAsync(goal => goal.Id == goalId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> MemberNamesAsync(CancellationToken cancellationToken) =>
        workspace.WorkspaceId is { } workspaceId
            ? (await directory.ListMembersAsync(workspaceId, cancellationToken)).ToDictionary(member => member.UserId, member => member.DisplayName)
            : new Dictionary<Guid, string>();

    public async Task<GoalResponse> DescribeAsync(Goal goal, CancellationToken cancellationToken) =>
        Describe(goal, await MemberNamesAsync(cancellationToken));

    public GoalResponse Describe(Goal goal, IReadOnlyDictionary<Guid, string> names)
    {
        var me = currentUser.UserId ?? Guid.Empty;
        var projection = GoalProjection.Of(goal, Today());
        return new GoalResponse(
            goal.Id, goal.Name, goal.TargetAmount, goal.Currency, goal.TargetDate, goal.Status, goal.IsShared, goal.IsOwner(me), projection.Saved, goal.SavedBy(me),
            projection.Remaining, projection.Percent, projection.RequiredMonthly, projection.AverageMonthly, projection.Health,
            goal.Participants.Where(participant => participant.IsActive)
                .OrderBy(participant => participant.Role)
                .ThenBy(participant => participant.JoinedAt)
                .Select(participant => new GoalParticipantResponse(
                    participant.UserId, NameOf(names, participant.UserId), participant.Role, goal.SavedBy(participant.UserId), participant.UserId == me))
                .ToList(),
            goal.ActiveParticipant(me)?.FundAccountId);
    }

    /// <summary>Someone who left the workspace keeps their contributions in the history, without a name.</summary>
    public static string NameOf(IReadOnlyDictionary<Guid, string> names, Guid userId) =>
        names.GetValueOrDefault(userId) is { Length: > 0 } name ? name : "Alguien que ya no está";
}

public sealed record ListGoalsQuery : IQuery<GoalsResponse>;

internal sealed class ListGoalsHandler(IGoalsDb db, GoalReader reader) : IQueryHandler<ListGoalsQuery, GoalsResponse>
{
    public async Task<Result<GoalsResponse>> HandleAsync(ListGoalsQuery query, CancellationToken cancellationToken)
    {
        var names = await reader.MemberNamesAsync(cancellationToken);
        var goals = await db.Goals.AsNoTracking().OrderBy(goal => goal.CreatedAt).ToListAsync(cancellationToken);
        return new GoalsResponse(
            goals.Select(goal => reader.Describe(goal, names)).ToList(),
            names.Select(member => new GoalMemberResponse(member.Key, member.Value)).OrderBy(member => member.DisplayName, StringComparer.InvariantCultureIgnoreCase).ToList(),
            reader.BaseCurrency);
    }
}

public sealed record GetGoalDetailQuery(Guid GoalId) : IQuery<GoalDetailResponse>;

internal sealed class GetGoalDetailHandler(IGoalsDb db, GoalReader reader, ICurrentUser currentUser) : IQueryHandler<GetGoalDetailQuery, GoalDetailResponse>
{
    private const int LatestContributions = 50;
    private const int Months = 6;

    public async Task<Result<GoalDetailResponse>> HandleAsync(GetGoalDetailQuery query, CancellationToken cancellationToken)
    {
        if (await db.Goals.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == query.GoalId, cancellationToken) is not { } goal)
        {
            return GoalErrors.NotFound;
        }

        var names = await reader.MemberNamesAsync(cancellationToken);
        var today = reader.Today();
        var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1 - Months);
        return new GoalDetailResponse(
            reader.Describe(goal, names),
            goal.Contributions.OrderByDescending(contribution => contribution.OccurredOn).ThenByDescending(contribution => contribution.CreatedAt)
                .Take(LatestContributions)
                .Select(contribution => new GoalContributionResponse(
                    contribution.Id, contribution.UserId, GoalReader.NameOf(names, contribution.UserId), contribution.Type, contribution.Amount, contribution.OccurredOn,
                    contribution.UserId == currentUser.UserId))
                .ToList(),
            Enumerable.Range(0, Months).Select(offset => firstMonth.AddMonths(offset))
                .Select(month => new GoalMonthResponse(
                    $"{month.Year:D4}-{month.Month:D2}",
                    goal.Contributions.Where(contribution => contribution.OccurredOn.Year == month.Year && contribution.OccurredOn.Month == month.Month).Sum(contribution => contribution.Signed)))
                .ToList());
    }
}

internal sealed class GoalQueries(IGoalsDb db) : IGoalQueries
{
    public async Task<IReadOnlyList<GoalSummary>> GetActiveAsync(int count, CancellationToken cancellationToken) =>
        (await db.Goals.AsNoTracking().Where(goal => goal.Status == GoalStatus.Active).ToListAsync(cancellationToken))
        .Select(goal => new GoalSummary(goal.Id, goal.Name, goal.Saved, goal.TargetAmount, goal.Currency, decimal.Round(goal.Saved * 100m / goal.TargetAmount, 1), goal.IsShared))
        .OrderByDescending(goal => goal.Percent)
        .Take(count)
        .ToList();
}
