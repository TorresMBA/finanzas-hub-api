using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PersonalFinance.BuildingBlocks.Application.Abstractions;

namespace PersonalFinance.Infrastructure.Persistence;

/// <summary>Takes the row lock on the table the model maps the aggregate to, by its single key column.</summary>
internal sealed class RowLock(AppDbContext db) : IRowLock
{
    public async Task LockAsync<TAggregate>(Guid id, CancellationToken cancellationToken) where TAggregate : class
    {
        var entity = db.Model.FindEntityType(typeof(TAggregate)) ?? throw new InvalidOperationException($"{typeof(TAggregate).Name} is not mapped.");
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var key = entity.FindPrimaryKey()!.Properties.Single().GetColumnName(table);

        // Names come from the model, never from the request; only the id is a parameter.
        var sql = string.Concat("SELECT 1 FROM \"", table.Schema, "\".\"", table.Name, "\" WHERE \"", key, "\" = {0} FOR UPDATE");
        await db.Database.ExecuteSqlRawAsync(sql, [id], cancellationToken);
    }
}
