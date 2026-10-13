using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Identity.Application.Abstractions;
using PersonalFinance.Modules.Identity.Domain;

namespace PersonalFinance.Modules.Identity.Application.Profile;

public sealed record PreferencesResponse(
    string Locale, string DateFormat, WeekStart WeekStart, ThemePreference Theme, bool HideBalances, Guid? DefaultWorkspaceId, int? SavingsRateTarget)
{
    internal static PreferencesResponse From(UserPreferences preferences) => new(
        preferences.Locale, preferences.DateFormat, preferences.WeekStart, preferences.Theme,
        preferences.HideBalances, preferences.DefaultWorkspaceId, preferences.SavingsRateTarget);
}

public sealed record MeResponse(Guid Id, string Email, bool EmailConfirmed, string DisplayName, string? Phone, PreferencesResponse Preferences);

public sealed record GetMeQuery : IQuery<MeResponse>;

internal sealed class GetMeHandler(IIdentityDb db, ICurrentUser currentUser) : IQueryHandler<GetMeQuery, MeResponse>
{
    public async Task<Result<MeResponse>> HandleAsync(GetMeQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId
            || await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId, cancellationToken) is not { } user)
        {
            // A session whose user no longer exists is not a session.
            return IdentityErrors.Unauthenticated;
        }

        var preferences = await db.UserPreferences.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == userId, cancellationToken)
            ?? UserPreferences.CreateDefault(userId);
        return new MeResponse(user.Id, user.Email!, user.EmailConfirmed, user.DisplayName, user.PhoneNumber, PreferencesResponse.From(preferences));
    }
}

public sealed record UpdateProfileCommand(string DisplayName, string? Phone) : ICommand<Unit>;

internal sealed class UpdateProfileHandler(IIdentityDb db, ICurrentUser currentUser) : ICommandHandler<UpdateProfileCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId
            || await db.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken) is not { } user)
        {
            return IdentityErrors.Unauthenticated;
        }

        var updated = user.UpdateProfile(command.DisplayName, command.Phone);
        return updated.IsFailure ? updated.Error : Unit.Value;
    }
}

public sealed record UpdatePreferencesCommand(
    string Locale, string DateFormat, WeekStart WeekStart, ThemePreference Theme, bool HideBalances, Guid? DefaultWorkspaceId, int? SavingsRateTarget)
    : ICommand<PreferencesResponse>;

internal sealed class UpdatePreferencesHandler(IIdentityDb db, ICurrentUser currentUser) : ICommandHandler<UpdatePreferencesCommand, PreferencesResponse>
{
    public async Task<Result<PreferencesResponse>> HandleAsync(UpdatePreferencesCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return IdentityErrors.Unauthenticated;
        }

        var preferences = await db.UserPreferences.SingleOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        if (preferences is null)
        {
            preferences = UserPreferences.CreateDefault(userId);
            db.UserPreferences.Add(preferences);
        }

        var updated = preferences.Update(
            command.Locale, command.DateFormat, command.WeekStart, command.Theme, command.HideBalances, command.DefaultWorkspaceId, command.SavingsRateTarget);
        return updated.IsFailure ? updated.Error : PreferencesResponse.From(preferences);
    }
}
