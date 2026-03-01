using System.Data.Common;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Abstractions;

public interface IProviderAdapter
{
    string Name { get; }
    string DefaultSchema { get; }
    ProviderCapabilities Capabilities { get; }

    bool CanHandle(string providerName);

    Task EnsureOrchestratorTablesAsync(DbConnection connection, CancellationToken cancellationToken);
    Task AcquireLockAsync(DbConnection connection, string lockName, string owner, TimeSpan timeout, CancellationToken cancellationToken);
    Task ReleaseLockAsync(DbConnection connection, string lockName, string owner, CancellationToken cancellationToken);

    Task<long> StartUpgradeHistoryAsync(DbConnection connection, bool dryRun, CancellationToken cancellationToken);
    Task CompleteUpgradeHistoryAsync(DbConnection connection, long historyId, int operationsApplied, string status, string? error, CancellationToken cancellationToken);

    Task<DatabaseSchema> ReadCurrentSchemaAsync(DbConnection connection, CancellationToken cancellationToken);

    string QuoteIdentifier(string identifier);
    bool SupportsInPlaceColumnAlter { get; }
    string BuildCreateTableSql(TableSchema table);
    string BuildAddColumnSql(TableSchema table, ColumnSchema column);
    string BuildAlterColumnSql(TableSchema table, ColumnSchema currentColumn, ColumnSchema desiredColumn);
    string BuildCreateIndexSql(IndexSchema index);
    string BuildDropIndexSql(IndexSchema index);
    string BuildRenameTableSql(string schema, string table, string newTableName);
    string BuildRenameColumnSql(string schema, string table, string column, string newColumnName);
    string BuildDropTableSql(string schema, string table);
    string BuildDropColumnSql(string schema, string table, string column);

    Task RecordArchivedObjectAsync(
        DbConnection connection,
        string objectType,
        string schema,
        string table,
        string? column,
        string archivedName,
        DateTime archivedAtUtc,
        DateTime cleanupAfterUtc,
        CancellationToken cancellationToken);
}
