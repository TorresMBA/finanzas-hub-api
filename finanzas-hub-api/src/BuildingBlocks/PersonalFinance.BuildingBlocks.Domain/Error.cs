namespace PersonalFinance.BuildingBlocks.Domain;

public enum ErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    BusinessRule
}

/// <summary>
/// An expected failure. <see cref="Code"/> is stable and part of the API contract; <see cref="Message"/> is for people.
/// </summary>
public sealed record Error
{
    private Error(string code, string message, ErrorType type, IReadOnlyDictionary<string, string[]>? fieldErrors = null)
    {
        Code = code;
        Message = message;
        Type = type;
        FieldErrors = fieldErrors;
    }

    public string Code { get; }
    public string Message { get; }
    public ErrorType Type { get; }

    /// <summary>Per-field messages; only set for <see cref="ErrorType.Validation"/>.</summary>
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; }

    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new(code, message, ErrorType.Validation, fieldErrors);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error BusinessRule(string code, string message) => new(code, message, ErrorType.BusinessRule);
}
