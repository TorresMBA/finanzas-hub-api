using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Infrastructure.Observability;
using PersonalFinance.Infrastructure.Security;

namespace PersonalFinance.Api.Http;

/// <summary>
/// Accepts <c>X-Correlation-Id</c> from the caller or falls back to the trace id, echoes it in the response
/// and attaches it to the trace and to every log line of the request.
/// </summary>
internal sealed class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context, CorrelationContext correlation)
    {
        var supplied = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsAcceptable(supplied)
            ? supplied
            : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        correlation.CorrelationId = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { ["correlation_id"] = correlationId }))
        {
            await next(context);
        }
    }

    // The value is written to logs and response headers, so only a conservative character set is accepted.
    private static bool IsAcceptable(string value) =>
        value.Length is > 0 and <= 64 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

/// <summary>Copies the authenticated user into the scope's actor context. Runs after authentication.</summary>
internal sealed class ActorContextMiddleware(RequestDelegate next, ILogger<ActorContextMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IActorContextInitializer actor)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            actor.ActAs(userId, ActorType.User, "Web");
            Activity.Current?.SetTag("user.id", userId);
            using (logger.BeginScope(new Dictionary<string, object> { ["user_id"] = userId }))
            {
                await next(context);
            }

            return;
        }

        await next(context);
    }
}

/// <summary>
/// Second authorization level, applied to every controller. A route that carries <c>{workspaceId}</c> requires
/// an active membership; without one the workspace does not exist as far as the caller can tell. It runs before
/// the body is read, so a non-member learns nothing from validation errors either.
/// </summary>
internal sealed class WorkspaceScopeFilter(
    IWorkspaceAccess workspaceAccess, ICurrentUser currentUser, IActorContextInitializer actor, ILogger<WorkspaceScopeFilter> logger)
    : IAsyncResourceFilter
{
    /// <summary>Workspace routes must name the parameter exactly like this; an architecture test checks it.</summary>
    public const string RouteParameter = "workspaceId";

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (!context.RouteData.Values.TryGetValue(RouteParameter, out var routeValue))
        {
            await next();
            return;
        }

        var httpContext = context.HttpContext;
        if (currentUser.UserId is not { } userId)
        {
            context.Result = Error.Unauthorized("auth.unauthenticated", "Authentication is required.").ToProblem(httpContext);
            return;
        }

        var membership = Guid.TryParse(routeValue?.ToString(), out var workspaceId)
            ? await workspaceAccess.GetActiveMembershipAsync(workspaceId, userId, httpContext.RequestAborted)
            : null;
        if (membership is null)
        {
            context.Result = Error.NotFound("workspace.not_found", "The workspace was not found.").ToProblem(httpContext);
            return;
        }

        actor.EnterWorkspace(membership);
        Activity.Current?.SetTag("workspace.id", membership.WorkspaceId);
        using (logger.BeginScope(new Dictionary<string, object> { ["workspace_id"] = membership.WorkspaceId }))
        {
            await next();
        }
    }
}
