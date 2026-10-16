using Microsoft.Extensions.Options;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Infrastructure.Security;
using PersonalFinance.Infrastructure.WhatsApp;
using PersonalFinance.Modules.Notifications.Application;

namespace PersonalFinance.Api.Http;

/// <summary>
/// What the WhatsApp webhook needs from the host: the secrets to check that a request comes from Meta, and acting
/// as the person who wrote each message. A message is handled in a scope of its own, as that person in their
/// personal workspace, so whatever answers it sees only their data, exactly as if they had asked in the app.
/// </summary>
public sealed class WhatsAppInbox(IOptions<WhatsAppOptions> options, IServiceScopeFactory scopeFactory, ILogger<WhatsAppInbox> logger)
{
    public bool IsSubscription(string? mode, string? verifyToken) =>
        mode == "subscribe" && !string.IsNullOrWhiteSpace(options.Value.VerifyToken) && verifyToken is not null
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(verifyToken), System.Text.Encoding.UTF8.GetBytes(options.Value.VerifyToken));

    public bool IsSigned(string? signature, byte[] body) => WhatsAppSignature.IsValid(signature, options.Value.AppSecret, body);

    /// <summary>Records the delivery reports and answers each new message from a verified number.</summary>
    public async Task<Result> ReceiveAsync(string body, ICommandHandler<ReceiveWhatsAppWebhookCommand, IReadOnlyList<WhatsAppInboundMessage>> receive, CancellationToken cancellationToken)
    {
        var received = await receive.HandleAsync(new ReceiveWhatsAppWebhookCommand(body), cancellationToken);
        if (received.IsFailure)
        {
            return received.Error;
        }

        foreach (var message in received.Value)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var membership = await services.GetRequiredService<IWorkspaceAccess>().GetPersonalMembershipAsync(message.UserId, cancellationToken);
            if (membership is null)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Ignored WhatsApp message {MessageId}: its sender has no personal workspace", message.ExternalId);
                }

                continue;
            }

            var actor = services.GetRequiredService<IActorContextInitializer>();
            actor.ActAs(message.UserId, ActorType.User, "WhatsApp");
            actor.EnterWorkspace(membership);

            // A failure throws and the request answers 500, so WhatsApp sends the event again; what was handled is not repeated.
            var handled = await services.GetRequiredService<ICommandHandler<HandleWhatsAppMessageCommand, bool>>()
                .HandleAsync(new HandleWhatsAppMessageCommand(message.ExternalId, message.Text), cancellationToken);
            if (handled.IsFailure)
            {
                return handled.Error;
            }
        }

        return Result.Success();
    }
}
