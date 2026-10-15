using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Modules.Notifications.Contracts.Events;

/// <summary>
/// A person wrote by WhatsApp from their verified number. It is raised once per message and handled as that person,
/// in their personal workspace, so whatever answers sees only their data.
/// </summary>
public sealed record InboundMessageReceived(Guid UserId, string Text) : IDomainEvent;
