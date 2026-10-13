using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PersonalFinance.Infrastructure.Messaging;

namespace PersonalFinance.Api.Realtime;

/// <summary>
/// The one hub. It only pushes signals to the browser and accepts nothing from it. A connection belongs to the
/// person of its session cookie, and a signal is addressed to a person by id: SignalR matches the two with the
/// name identifier claim, so nobody receives the signals of someone else.
/// </summary>
[Authorize]
public sealed class RealtimeHub : Hub
{
    public const string Path = "/hubs/realtime";
}

internal sealed class SignalRRealtimeTransport(IHubContext<RealtimeHub> hub) : IRealtimeTransport
{
    public Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken) =>
        hub.Clients.User(userId.ToString()).SendAsync(eventName, payload, cancellationToken);
}
