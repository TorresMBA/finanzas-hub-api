using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Api.Http;

/// <summary>The only place where an <see cref="Error"/> becomes an HTTP response (RFC 9457 problem details).</summary>
internal static class ProblemResults
{
    public static ObjectResult ToProblem(this Error error, HttpContext httpContext)
    {
        var (status, title) = error.Type switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "The request is not valid."),
            ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, "Authentication is required."),
            ErrorType.Forbidden => (StatusCodes.Status403Forbidden, "This action is not allowed."),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "The resource was not found."),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "The request conflicts with the current state."),
            ErrorType.BusinessRule => (StatusCodes.Status422UnprocessableEntity, "A business rule rejected the request."),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Type, "Unknown error type.")
        };

        // The factory applies the same customization as every other problem response (traceId, default code).
        var factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        ProblemDetails problem;
        if (error.Type == ErrorType.Validation && error.FieldErrors is { Count: > 0 } fieldErrors)
        {
            var modelState = new ModelStateDictionary();
            foreach (var (field, messages) in fieldErrors)
            {
                foreach (var message in messages)
                {
                    modelState.AddModelError(field, message);
                }
            }

            problem = factory.CreateValidationProblemDetails(httpContext, modelState, status, title, detail: error.Message);
        }
        else
        {
            problem = factory.CreateProblemDetails(httpContext, status, title, detail: error.Message);
        }

        problem.Extensions["code"] = error.Code;
        return new ObjectResult(problem) { StatusCode = status };
    }

    /// <summary>Stable code for problem responses produced by the framework itself, which have no <see cref="Error"/>.</summary>
    public static string DefaultCodeFor(int? statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "request.invalid",
        StatusCodes.Status401Unauthorized => "auth.unauthenticated",
        StatusCodes.Status403Forbidden => "auth.forbidden",
        StatusCodes.Status404NotFound => "resource.not_found",
        StatusCodes.Status405MethodNotAllowed => "request.method_not_allowed",
        StatusCodes.Status409Conflict => "request.conflict",
        StatusCodes.Status413PayloadTooLarge => "request.too_large",
        StatusCodes.Status415UnsupportedMediaType => "request.unsupported_media_type",
        StatusCodes.Status422UnprocessableEntity => "request.unprocessable",
        StatusCodes.Status429TooManyRequests => "request.rate_limited",
        _ => "unexpected"
    };
}
