namespace PersonalFinance.Infrastructure.Platform;

/// <summary>The stored outcome of an idempotent command, returned again when the same key is replayed.</summary>
internal sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
    }

    public Guid UserId { get; private set; }

    public string Key { get; private set; } = null!;

    public string Operation { get; private set; } = null!;

    public string RequestHash { get; private set; } = null!;

    public string ResponseBody { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public static IdempotencyRecord Create(Guid userId, string key, string operation, string requestHash, string responseBody, DateTimeOffset now, TimeSpan retention) => new()
    {
        UserId = userId,
        Key = key,
        Operation = operation,
        RequestHash = requestHash,
        ResponseBody = responseBody,
        CreatedAt = now,
        ExpiresAt = now.Add(retention)
    };
}

/// <summary>Functional audit trail entry. Append-only: the application's database role may only insert and read it.</summary>
internal sealed class AuditLogEntry
{
    private AuditLogEntry()
    {
    }

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string ActorType { get; private set; } = null!;

    public string Source { get; private set; } = null!;

    public Guid? WorkspaceId { get; private set; }

    public Guid? OwnerUserId { get; private set; }

    public string Module { get; private set; } = null!;

    public string Action { get; private set; } = null!;

    public string ResourceType { get; private set; } = null!;

    public Guid ResourceId { get; private set; }

    public string? ScopeType { get; private set; }

    public Guid? ScopeId { get; private set; }

    public string? Before { get; private set; }

    public string? After { get; private set; }

    public string? CorrelationId { get; private set; }

    public string? TraceId { get; private set; }

    public static AuditLogEntry Create(
        DateTimeOffset occurredAt, Guid? actorUserId, string actorType, string source, Guid? workspaceId, Guid? ownerUserId,
        string module, string action, string resourceType, Guid resourceId, string? scopeType, Guid? scopeId,
        string? before, string? after, string? correlationId, string? traceId) => new()
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = occurredAt,
            ActorUserId = actorUserId,
            ActorType = actorType,
            Source = source,
            WorkspaceId = workspaceId,
            OwnerUserId = ownerUserId,
            Module = module,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ScopeType = scopeType,
            ScopeId = scopeId,
            Before = before,
            After = after,
            CorrelationId = correlationId,
            TraceId = traceId
        };
}

/// <summary>
/// An email waiting to be sent. Written in the transaction of the command that needs it and sent by a Worker
/// job, so a mail outage never fails a command. The body is erased once sent: it may carry a one-time link.
/// </summary>
internal sealed class OutgoingEmail
{
    private OutgoingEmail()
    {
    }

    public Guid Id { get; private set; }

    public string To { get; private set; } = null!;

    public string Subject { get; private set; } = null!;

    public string? Body { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public static OutgoingEmail Create(string to, string subject, string body, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        To = to,
        Subject = subject,
        Body = body,
        CreatedAt = now,
        NextAttemptAt = now
    };

    public void MarkSent(DateTimeOffset now)
    {
        SentAt = now;
        Body = null;
        LastError = null;
    }

    /// <summary>Waits longer after each failure: 1, 2, 4... minutes, up to an hour.</summary>
    public void MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        NextAttemptAt = now.AddMinutes(Math.Min(60, Math.Pow(2, Attempts - 1)));
    }
}
