using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Notifications.Application;

namespace PersonalFinance.Api.Controllers.Notifications;

public sealed class StartWhatsAppVerificationRequest
{
    /// <summary>With its country code, as +51 987 654 321.</summary>
    [Required]
    [StringLength(24)]
    public string? Phone { get; init; }
}

public sealed class ConfirmWhatsAppVerificationRequest
{
    [Required]
    [StringLength(12)]
    public string? Code { get; init; }
}

/// <summary>The WhatsApp number of the signed-in person. It receives notices and can ask for balances once its code is confirmed.</summary>
[Tags("Notifications")]
[Route("api/v1/me/channels/whatsapp")]
[Problem(StatusCodes.Status401Unauthorized)]
public sealed class WhatsAppController : ApiControllerBase
{
    [HttpGet(Name = "GetWhatsAppChannel")]
    [ProducesResponseType<WhatsAppChannelResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromServices] IQueryHandler<GetWhatsAppChannelQuery, WhatsAppChannelResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetWhatsAppChannelQuery(), cancellationToken), Ok);

    /// <summary>Sends a code by WhatsApp to the number. A new code can be asked for after a minute; each lasts ten minutes.</summary>
    [HttpPost("verify-start", Name = "StartWhatsAppVerification")]
    [ProducesResponseType<WhatsAppVerificationResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(
        StartWhatsAppVerificationRequest request, [FromServices] ICommandHandler<StartWhatsAppVerificationCommand, WhatsAppVerificationResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new StartWhatsAppVerificationCommand(request.Phone!), cancellationToken), Ok);

    /// <summary>Confirms the number with the code it received. Five wrong codes and a new one must be asked for.</summary>
    [HttpPost("verify-confirm", Name = "ConfirmWhatsAppVerification")]
    [ProducesResponseType<WhatsAppChannelResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(
        ConfirmWhatsAppVerificationRequest request, [FromServices] ICommandHandler<ConfirmWhatsAppVerificationCommand, WhatsAppConfirmation> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(
            await handler.HandleAsync(new ConfirmWhatsAppVerificationCommand(request.Code!), cancellationToken),
            confirmation => confirmation.Rejection is { } rejection ? rejection.ToProblem(HttpContext) : Ok(confirmation.Channel));

    /// <summary>Forgets the number and turns off every notice by WhatsApp.</summary>
    [HttpDelete(Name = "RemoveWhatsAppChannel")]
    [ProducesResponseType<WhatsAppChannelResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Remove([FromServices] ICommandHandler<RemoveWhatsAppCommand, WhatsAppChannelResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new RemoveWhatsAppCommand(), cancellationToken), Ok);
}

/// <summary>
/// Where Meta delivers WhatsApp events: messages people write and reports of the messages sent. Anonymous, but an
/// event counts only if it carries the signature of the App Secret; it is handled in the request, and a failure
/// answers 500 so that Meta sends it again.
/// </summary>
[Route("webhooks/v1/whatsapp")]
[AllowAnonymous]
[EnableRateLimiting(HttpSetup.WebhookRateLimit)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class WhatsAppWebhookController(WhatsAppInbox inbox) : ApiControllerBase
{
    private const int MaxBodyBytes = 1024 * 1024;

    /// <summary>The check Meta makes when the address is registered: it expects the challenge back.</summary>
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode, [FromQuery(Name = "hub.verify_token")] string? verifyToken, [FromQuery(Name = "hub.challenge")] string? challenge) =>
        inbox.IsSubscription(mode, verifyToken) ? Content(challenge ?? string.Empty, "text/plain") : StatusCode(StatusCodes.Status403Forbidden);

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(
        [FromServices] ICommandHandler<ReceiveWhatsAppWebhookCommand, IReadOnlyList<WhatsAppInboundMessage>> handler,
        [FromHeader(Name = "X-Hub-Signature-256")] string? signature, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        var body = buffer.ToArray();

        // The signature covers the exact bytes sent; nothing is read or stored before it is checked.
        if (!inbox.IsSigned(signature, body))
        {
            return Error.Unauthorized("webhook.signature_invalid", "The signature does not match.").ToProblem(HttpContext);
        }

        var result = await inbox.ReceiveAsync(System.Text.Encoding.UTF8.GetString(body), handler, cancellationToken);
        return result.IsSuccess ? Ok() : result.Error.ToProblem(HttpContext);
    }
}
