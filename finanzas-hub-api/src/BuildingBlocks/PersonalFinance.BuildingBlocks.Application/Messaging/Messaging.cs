using PersonalFinance.BuildingBlocks.Domain;

namespace PersonalFinance.BuildingBlocks.Application.Messaging;

/// <summary>A use case that changes state. It runs inside one database transaction.</summary>
public interface ICommand<TResponse>;

/// <summary>A use case that only reads.</summary>
public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// A command with a financial effect. Repeating it with the same key returns the first response
/// instead of applying the effect twice.
/// </summary>
public interface IIdempotentCommand
{
    string IdempotencyKey { get; }
}

/// <summary>Response of a command that returns nothing.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value;
}
