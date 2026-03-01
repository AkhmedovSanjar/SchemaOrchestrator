using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Subscribers;

public sealed class UpgradeExecutionContext
{
    public UpgradeExecutionContext(DbContext dbContext, DbConnection connection, UpgradeSubscriberContext subscriberContext)
    {
        DbContext = dbContext;
        Connection = connection;
        SubscriberContext = subscriberContext;
    }

    public DbContext DbContext { get; }
    public DbConnection Connection { get; }
    public UpgradeSubscriberContext SubscriberContext { get; }

    public async Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default)
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<T?> ExecuteScalarAsync<T>(string sql, CancellationToken cancellationToken = default)
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result is null || result is DBNull)
        {
            return default;
        }

        return (T?)Convert.ChangeType(result, typeof(T));
    }

    public async Task<List<Dictionary<string, object?>>> QueryAsync(string sql, CancellationToken cancellationToken = default)
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }
}
