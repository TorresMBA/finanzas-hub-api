namespace PersonalFinance.BuildingBlocks.Domain;

/// <summary>Outcome of an operation that can fail for an expected reason. Business rules return it instead of throwing.</summary>
public class Result
{
    private readonly Error? _error;

    protected Result(Error? error)
    {
        _error = error;
    }

    public bool IsSuccess => _error is null;

    public bool IsFailure => _error is not null;

    public Error Error => _error ?? throw new InvalidOperationException("A successful result has no error.");

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    public static Result<T> Success<T>(T value) => new(value);

    public static Result<T> Failure<T>(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T value) : base(null)
    {
        _value = value;
    }

    internal Result(Error error) : base(error)
    {
    }

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"A failed result has no value: {Error.Code}.");

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));
}
