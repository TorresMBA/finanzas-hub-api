using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.Formatters;
using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Api.Http;

/// <summary>Unexpected failures become a 500 problem response without internal details. Business rules never reach here.</summary>
internal sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            BadHttpRequestException badRequest => (badRequest.StatusCode, "The request is not valid."),
            JsonException => (StatusCodes.Status400BadRequest, "The request body is not valid JSON."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception");
        }
        else
        {
            logger.LogWarning(exception, "Malformed request");
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title }
        });
    }
}

internal static class HttpSetup
{
    public const string CorsPolicy = "Frontend";

    /// <summary>Controllers, error responses, session cookie, authorization and CORS.</summary>
    public static IServiceCollection AddApiHttp(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddControllers(options =>
            {
                options.Filters.Add<WorkspaceScopeFilter>();

                // The API speaks JSON only. Without this the contract would list text/plain and text/json everywhere.
                options.OutputFormatters.RemoveType<StringOutputFormatter>();
                foreach (var formatter in options.InputFormatters.OfType<SystemTextJsonInputFormatter>())
                {
                    formatter.SupportedMediaTypes.Remove("text/json");
                    formatter.SupportedMediaTypes.Remove("application/*+json");
                }

                foreach (var formatter in options.OutputFormatters.OfType<SystemTextJsonOutputFormatter>())
                {
                    formatter.SupportedMediaTypes.Remove("text/json");
                }
            })
            .AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = context =>
            {
                // Shape rules (required fields, lengths, ranges) come from the request's data annotations.
                // Errors are reported under the JSON name of the field ("title"), not the C# one ("Title").
                var fieldErrors = context.ModelState
                    .Where(entry => entry.Value is { Errors.Count: > 0 })
                    .GroupBy(
                        entry => string.Join('.', entry.Key.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName)),
                        entry => entry.Value!.Errors.Select(error => string.IsNullOrEmpty(error.ErrorMessage) ? "Invalid value." : error.ErrorMessage))
                    .ToDictionary(group => group.Key, group => group.SelectMany(messages => messages).ToArray());
                return Error.Validation("validation.failed", "One or more fields are not valid.", fieldErrors).ToProblem(context.HttpContext);
            });

        // The OpenAPI document builds its schemas from these options, not from MVC's.
        services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var extensions = context.ProblemDetails.Extensions;
            extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
            if (!extensions.ContainsKey("code"))
            {
                extensions["code"] = ProblemResults.DefaultCodeFor(context.ProblemDetails.Status);
            }
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Session cookie the browser cannot read from script. An API answers 401/403; it never redirects to a login page.
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "ph.session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        // First authorization level: every endpoint requires a signed-in user unless it opts out explicitly.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        var allowedOrigins = ResolveAllowedOrigins(configuration, environment);
        services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders(CorrelationMiddleware.HeaderName, "ETag", "Location")));

        return services;
    }

    public static WebApplication UseApiHttp(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseMiddleware<CorrelationMiddleware>();
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            await next(context);
        });

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        // CORS answers the browser preflight before HTTPS redirection would turn it into a redirect.
        app.UseCors(CorsPolicy);
        
        app.UseHttpsRedirection();
        app.UseAuthentication();

        app.UseMiddleware<ActorContextMiddleware>();
        app.UseAuthorization();

        return app;
    }

    private static void ConfigureJson(JsonSerializerOptions options) => options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

    /// <summary>
    /// The allow-list is a set of exact browser origins. Production fails closed: with no origin configured the
    /// application does not start, rather than accepting any site.
    /// </summary>
    private static string[] ResolveAllowedOrigins(IConfiguration configuration, IHostEnvironment environment)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0 && environment.IsDevelopment())
        {
            origins = ["http://localhost:4200"];
        }

        if (origins.Length == 0 && environment.IsProduction())
        {
            throw new InvalidOperationException("Cors:AllowedOrigins must contain at least one HTTPS frontend origin in Production.");
        }

        if (origins.Any(origin =>
                !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || !string.IsNullOrEmpty(uri.PathAndQuery.Trim('/'))
                || !string.IsNullOrEmpty(uri.Fragment)))
        {
            throw new InvalidOperationException("Each Cors:AllowedOrigins value must be an absolute origin without a path.");
        }

        return origins;
    }
}
