using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Infrastructure.Observability;
using PersonalFinance.Infrastructure.Persistence;
using PersonalFinance.Infrastructure.Platform;

namespace PersonalFinance.Infrastructure.Messaging;

internal static class UseCase
{
    /// <summary>"PersonalFinance.Modules.Ledger" becomes "Ledger".</summary>
    public static string ModuleOf(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name ?? string.Empty;
        return assemblyName[(assemblyName.LastIndexOf('.') + 1)..];
    }

    public static async Task<Result<TResponse>> TraceAsync<TResponse>(ILogger logger, Type messageType, Func<Task<Result<TResponse>>> next)
    {
        var module = ModuleOf(messageType);
        var useCase = messageType.Name;
        using var activity = Telemetry.ActivitySource.StartActivity($"{module}.{useCase}");
        activity?.SetTag("module", module);
        activity?.SetTag("use_case", useCase);
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["module"] = module, ["use_case"] = useCase });
        var startedAt = Stopwatch.GetTimestamp();
        var outcome = "error";
        try
        {
            var result = await next();
            outcome = result.IsSuccess ? "success" : result.Error.Code;
            if (result.IsFailure)
            {
                activity?.SetTag("error.code", result.Error.Code);
                logger.LogWarning("{UseCase} was rejected with {ErrorCode}", useCase, result.Error.Code);
            }

            return result;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            Telemetry.UseCaseDuration.Record(elapsed,
                new KeyValuePair<string, object?>("module", module),
                new KeyValuePair<string, object?>("use_case", useCase),
                new KeyValuePair<string, object?>("outcome", outcome == "success" || outcome == "error" ? outcome : "rejected"));
            logger.LogInformation("{UseCase} finished with {Outcome} in {ElapsedMs:F1} ms", useCase, outcome, elapsed);
        }
    }
}

internal sealed class LoggingCommandHandlerDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner, ILogger<LoggingCommandHandlerDecorator<TCommand, TResponse>> logger)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken) =>
        UseCase.TraceAsync(logger, typeof(TCommand), () => inner.HandleAsync(command, cancellationToken));
}

internal sealed class LoggingQueryHandlerDecorator<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner, ILogger<LoggingQueryHandlerDecorator<TQuery, TResponse>> logger)
    : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken) =>
        UseCase.TraceAsync(logger, typeof(TQuery), () => inner.HandleAsync(query, cancellationToken));
}

/// <summary>
/// Runs the command, the reactions to its domain events and the save in one database transaction. A failed
/// result or a failing reaction rolls everything back. A command invoked from inside another joins its transaction.
/// </summary>
internal sealed class TransactionCommandHandlerDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner, AppDbContext db, DomainEventDispatcher domainEvents)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await inner.HandleAsync(command, cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await inner.HandleAsync(command, cancellationToken);
            if (result.IsFailure)
            {
                await RollbackAsync(transaction);
                return result;
            }

            await domainEvents.SaveAndDispatchAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollbackAsync(transaction);
            return Error.Conflict("concurrency_conflict", "The resource was changed by someone else. Reload it and try again.");
        }
        catch (DbUpdateException exception) when (IdempotencyRace.IsDuplicateKey(exception))
        {
            // Two requests with the same key ran at once. This one lost: nothing it did was saved.
            await RollbackAsync(transaction);
            return Error.Conflict("idempotency.in_progress", "A request with the same Idempotency-Key is being processed. Retry to get its result.");
        }
        catch
        {
            await RollbackAsync(transaction);
            throw;
        }
    }

    private async Task RollbackAsync(IDbContextTransaction transaction)
    {
        await transaction.RollbackAsync(CancellationToken.None);

        // Tracked entities no longer match the database; nothing in this scope may reuse them.
        db.ChangeTracker.Clear();
    }
}

internal static class IdempotencyRace
{
    public const string PrimaryKeyName = "pk_idempotency_keys";

    public static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: PrimaryKeyName };
}

/// <summary>
/// Stores the response of a successful command under its idempotency key, inside the command's own transaction.
/// A replay with the same key and the same request returns that response without running the command again.
/// </summary>
internal sealed class IdempotencyCommandHandlerDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner, AppDbContext db, ICurrentUser currentUser, IClock clock) : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>, IIdempotentCommand
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? Guid.Empty;
        var operation = typeof(TCommand).FullName!;
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command, JsonOptions))));

        var existing = await db.IdempotencyKeys.AsNoTracking()
            .SingleOrDefaultAsync(record => record.UserId == userId && record.Key == command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.Operation != operation || existing.RequestHash != requestHash)
            {
                return Error.BusinessRule("idempotency.key_reused", "This Idempotency-Key was already used with a different request.");
            }

            return JsonSerializer.Deserialize<TResponse>(existing.ResponseBody, JsonOptions)!;
        }

        var result = await inner.HandleAsync(command, cancellationToken);
        if (result.IsSuccess)
        {
            db.IdempotencyKeys.Add(IdempotencyRecord.Create(
                userId, command.IdempotencyKey, operation, requestHash, JsonSerializer.Serialize(result.Value, JsonOptions), clock.UtcNow, Retention));
        }

        return result;
    }
}
