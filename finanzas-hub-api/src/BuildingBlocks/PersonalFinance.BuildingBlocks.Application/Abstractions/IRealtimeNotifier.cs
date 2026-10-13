namespace PersonalFinance.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Tells a person's open browsers that something changed. Only a signal travels: the browser asks again over
/// HTTP, where authorization applies. It is sent after the command commits, so nothing rolled back is announced,
/// and it may be lost: the browser also polls.
/// </summary>
public interface IRealtimeNotifier
{
    void ToUser(Guid userId, string eventName, object payload);
}
