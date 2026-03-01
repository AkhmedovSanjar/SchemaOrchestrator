using System.Data;
using System.Data.Common;
using System.Globalization;
using SchemaOrchestrator.Abstractions;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Adapters;

public abstract class ProviderAdapterBase : IProviderAdapter
{
    public abstract string Name { get; }
    public abstract string DefaultSchema { get; }
    public abstract ProviderCapabilities Capabilities { get; }

    public abstract bool CanHandle(string providerName);
    public abstract Task EnsureOrchestratorTablesAsync(DbConnection connection, CancellationToken cancellationToken);
    public abstract Task<DatabaseSchema> ReadCurrentSchemaAsync(DbConnection connection, CancellationToken cancellationToken);

    public abstract string QuoteIdentifier(string identifier);
    public abstract bool SupportsInPlaceColumnAlter { get; }
    public abstract string BuildCreateTableSql(TableSchema table);
    public abstract string BuildAddColumnSql(TableSchema table, ColumnSchema column);
    public abstract string BuildAlterColumnSql(TableSchema table, ColumnSchema currentColumn, ColumnSchema desiredColumn);
    public abstract string BuildCreateIndexSql(IndexSchema index);
    public abstract string BuildDropIndexSql(IndexSchema index);
    public abstract string BuildRenameTableSql(string schema, string table, string newTableName);
    public abstract string BuildRenameColumnSql(string schema, string table, string column, string newColumnName);
    public abstract string BuildDropTableSql(string schema, string table);
    public abstract string BuildDropColumnSql(string schema, string table, string column);

    protected abstract string BuildStartHistorySql();
    protected abstract string BuildAcquireLockSql();

    public async Task AcquireLockAsync(DbConnection connection, string lockName, string owner, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var staleBeforeUtc = DateTime.UtcNow.Subtract(timeout);

        await using (var cleanup = connection.CreateCommand())
        {
            cleanup.CommandText = "DELETE FROM so_locks WHERE lock_name = @lock_name AND acquired_at_utc < @stale_before";
            AddParameter(cleanup, "@lock_name", lockName);
            AddParameter(cleanup, "@stale_before", staleBeforeUtc);
            await cleanup.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = BuildAcquireLockSql();
        AddParameter(cmd, "@lock_name", lockName);
        AddParameter(cmd, "@owner", owner);
        AddParameter(cmd, "@acquired_at_utc", DateTime.UtcNow);

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (affected <= 0)
        {
            throw new InvalidOperationException($"Could not acquire upgrade lock '{lockName}'.");
        }
    }

    public async Task ReleaseLockAsync(DbConnection connection, string lockName, string owner, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM so_locks WHERE lock_name = @lock_name AND owner = @owner";
        AddParameter(cmd, "@lock_name", lockName);
        AddParameter(cmd, "@owner", owner);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> StartUpgradeHistoryAsync(DbConnection connection, bool dryRun, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = BuildStartHistorySql();
        AddParameter(cmd, "@started_at_utc", DateTime.UtcNow);
        AddParameter(cmd, "@provider", Name);
        AddParameter(cmd, "@status", "running");
        AddParameter(cmd, "@dry_run", dryRun);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    public async Task CompleteUpgradeHistoryAsync(DbConnection connection, long historyId, int operationsApplied, string status, string? error, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE so_upgrade_history SET completed_at_utc = @completed_at_utc, operations_applied = @operations_applied, status = @status, error = @error WHERE id = @id";
        AddParameter(cmd, "@completed_at_utc", DateTime.UtcNow);
        AddParameter(cmd, "@operations_applied", operationsApplied);
        AddParameter(cmd, "@status", status);
        AddParameter(cmd, "@error", (object?)error ?? DBNull.Value, DbType.String);
        AddParameter(cmd, "@id", historyId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordArchivedObjectAsync(
        DbConnection connection,
        string objectType,
        string schema,
        string table,
        string? column,
        string archivedName,
        DateTime archivedAtUtc,
        DateTime cleanupAfterUtc,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO so_archived_objects
(object_type, schema_name, table_name, column_name, archived_name, archived_at_utc, cleanup_after_utc, provider)
VALUES
(@object_type, @schema_name, @table_name, @column_name, @archived_name, @archived_at_utc, @cleanup_after_utc, @provider)";

        AddParameter(cmd, "@object_type", objectType);
        AddParameter(cmd, "@schema_name", schema);
        AddParameter(cmd, "@table_name", table);
        AddParameter(cmd, "@column_name", (object?)column ?? DBNull.Value, DbType.String);
        AddParameter(cmd, "@archived_name", archivedName);
        AddParameter(cmd, "@archived_at_utc", archivedAtUtc);
        AddParameter(cmd, "@cleanup_after_utc", cleanupAfterUtc);
        AddParameter(cmd, "@provider", Name);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    protected static void AddParameter(DbCommand command, string name, object value, DbType? dbType = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        if (dbType.HasValue)
        {
            parameter.DbType = dbType.Value;
        }

        command.Parameters.Add(parameter);
    }
}
