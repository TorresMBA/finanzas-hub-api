using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.Api.Controllers;

/// <summary>
/// Base of every controller. A controller checks the shape of the request, builds a command or query, calls its
/// handler and turns the <see cref="Result"/> into a response. It holds no business rule and never touches persistence.
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    protected IActionResult ToActionResult<T>(Result<T> result, Func<T, IActionResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : result.Error.ToProblem(HttpContext);

    /// <summary>Commands with a financial effect cannot run without a key that makes retries safe.</summary>
    protected bool TryReadIdempotencyKey(string? headerValue, [NotNullWhen(true)] out string? key, [NotNullWhen(false)] out IActionResult? problem)
    {
        key = headerValue?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > 100)
        {
            key = null;
            problem = Error.Validation("idempotency.key_required", $"The {IdempotencyKeyHeader} header is required and must be at most 100 characters.").ToProblem(HttpContext);
            return false;
        }

        problem = null;
        return true;
    }
}
