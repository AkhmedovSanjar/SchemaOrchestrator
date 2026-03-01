using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchemaOrchestrator.Abstractions;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Services;

public sealed class SchemaCleanupWorker : ISchemaCleanupWorker
{
    private readonly ProviderResolver _providerResolver;
    private readonly SchemaOrchestratorOptions _options;
    private readonly ILogger<SchemaCleanupWorker> _logger;

    public SchemaCleanupWorker(
        ProviderResolver providerResolver,
        IOptions<SchemaOrchestratorOptions> options,
        ILogger<SchemaCleanupWorker> logger)
    {
        _providerResolver = providerResolver;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> CleanupArchivedObjectsAsync(DbContext dbContext, CancellationToken cancellationToken = default)
    {
        var adapter = _providerResolver.Resolve(dbContext);
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        var connection = dbContext.Database.GetDbConnection();

        var owner = Environment.MachineName + ":cleanup:" + Guid.NewGuid().ToString("N");
        var removedCount = 0;

        try
        {
            await adapter.EnsureOrchestratorTablesAsync(connection, cancellationToken);

            if (_options.EnableLocking)
            {
                await adapter.AcquireLockAsync(connection, _options.LockName + "_cleanup", owner, _options.LockTimeout, cancellationToken);
            }

            var candidates = await LoadArchiveCandidatesAsync(connection, cancellationToken);
            var utcNow = DateTime.UtcNow;

            foreach (var item in candidates)
            {
                if (item.CleanupAfterUtc > utcNow)
                {
                    continue;
                }

                try
                {
                    var sql = item.ObjectType switch
                    {
                        "table" => adapter.BuildDropTableSql(item.SchemaName, item.ArchivedName),
                        "column" => adapter.BuildDropColumnSql(item.SchemaName, item.TableName, item.ArchivedName),
                        _ => string.Empty
                    };

                    if (string.IsNullOrWhiteSpace(sql))
                    {
                        continue;
                    }

                    await ExecuteNonQueryAsync(connection, sql, cancellationToken);
                    await DeleteArchiveRecordAsync(connection, item.Id, cancellationToken);
                    removedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean archived object {ObjectType} {Schema}.{Table}/{ArchivedName}", item.ObjectType, item.SchemaName, item.TableName, item.ArchivedName);
                }
            }

            return removedCount;
        }
        finally
        {
            if (_options.EnableLocking)
            {
                await adapter.ReleaseLockAsync(connection, _options.LockName + "_cleanup", owner, cancellationToken);
            }

            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task<List<ArchivedObjectRow>> LoadArchiveCandidatesAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var rows = new List<ArchivedObjectRow>();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, object_type, schema_name, table_name, archived_name, cleanup_after_utc FROM so_archived_objects ORDER BY id";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            var objectType = reader.GetString(1);
            var schemaName = reader.GetString(2);
            var tableName = reader.GetString(3);
            var archivedName = reader.GetString(4);
            var cleanupAfterUtc = ConvertToUtc(reader.GetValue(5));

            rows.Add(new ArchivedObjectRow(id, objectType, schemaName, tableName, archivedName, cleanupAfterUtc));
        }

        return rows;
    }

    private static DateTime ConvertToUtc(object value)
    {
        if (value is DateTime dt)
        {
            return dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime();
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        return DateTime.MaxValue;
    }

    private static async Task DeleteArchiveRecordAsync(DbConnection connection, long id, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM so_archived_objects WHERE id = @id";
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = id;
        cmd.Parameters.Add(parameter);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record ArchivedObjectRow(
        long Id,
        string ObjectType,
        string SchemaName,
        string TableName,
        string ArchivedName,
        DateTime CleanupAfterUtc);
}
