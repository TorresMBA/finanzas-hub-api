using System.Diagnostics;
using System.Text.Json;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.Infrastructure.Observability;
using PersonalFinance.Infrastructure.Persistence;
using PersonalFinance.Infrastructure.Platform;
using PersonalFinance.Infrastructure.Security;

namespace PersonalFinance.Infrastructure.Audit;

/// <summary>Adds the entry to the context; it is saved with the rest of the command's transaction.</summary>
internal sealed class AuditLogWriter(AppDbContext db, ActorContext actor, CorrelationContext correlation, IClock clock) : IAuditLog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(AuditEntry entry)
    {
        db.AuditLog.Add(AuditLogEntry.Create(
            clock.UtcNow,
            actor.UserId,
            actor.ActorType.ToString(),
            actor.Source,
            entry.WorkspaceId ?? actor.WorkspaceId,
            entry.OwnerUserId,
            entry.Module,
            entry.Action,
            entry.ResourceType,
            entry.ResourceId,
            entry.ScopeType,
            entry.ScopeId,
            entry.Before is null ? null : JsonSerializer.Serialize(entry.Before, JsonOptions),
            entry.After is null ? null : JsonSerializer.Serialize(entry.After, JsonOptions),
            correlation.CorrelationId ?? Activity.Current?.TraceId.ToString(),
            Activity.Current?.TraceId.ToString()));
    }
}
