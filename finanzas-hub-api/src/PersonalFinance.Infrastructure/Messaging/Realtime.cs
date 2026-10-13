using Microsoft.Extensions.Logging;
using PersonalFinance.BuildingBlocks.Application.Abstractions;

namespace PersonalFinance.Infrastructure.Messaging;

/// <summary>How a signal actually reaches the browsers. The API sends it over its hub; a host without one sends nothing.</summary>
public interface IRealtimeTransport
{
    Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken);
}

internal sealed class NoRealtimeTransport : IRealtimeTransport
{
    public Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Holds the signals of a command until its transaction commits. A command that is rolled back announces
/// nothing; a signal that cannot be sent is dropped, because the browser also polls.
/// </summary>
internal sealed class RealtimeBuffer(IRealtimeTransport transport, ILogger<RealtimeBuffer> logger) : IRealtimeNotifier
{
    private readonly List<(Guid UserId, string EventName, object Payload)> _pending = [];

    public void ToUser(Guid userId, string eventName, object payload) => _pending.Add((userId, eventName, payload));

    public void Discard() => _pending.Clear();

    public async Task FlushAsync()
    {
        var signals = _pending.ToList();
        _pending.Clear();
        foreach (var (userId, eventName, payload) in signals)
        {
            try
            {
                await transport.SendToUserAsync(userId, eventName, payload, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Realtime signal {EventName} could not be sent", eventName);
            }
        }
    }
}
