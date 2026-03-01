using System.Data.Common;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Adapters;

public sealed class SqliteProviderAdapter : ProviderAdapterBase
{
    public override string Name => "sqlite";
    public override string DefaultSchema => "main";
    public override ProviderCapabilities Capabilities => new(
        SupportsInPlaceColumnAlter: false,
        SupportsIndexes: true,
        SupportsArchiveRename: true,
        SupportsForeignKeys: false,
        SupportsCheckConstraints: false);
    public override bool SupportsInPlaceColumnAlter => false;

    public override bool CanHandle(string providerName) =>
        providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

    public override async Task EnsureOrchestratorTablesAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var sql = @"
CREATE TABLE IF NOT EXISTS so_archived_objects (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    object_type TEXT NOT NULL,
    schema_name TEXT NOT NULL,
    table_name TEXT NOT NULL,
    column_name TEXT NULL,
    archived_name TEXT NOT NULL,
    archived_at_utc TEXT NOT NULL,
    cleanup_after_utc TEXT NOT NULL,
    provider TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS so_upgrade_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    started_at_utc TEXT NOT NULL,
    completed_at_utc TEXT NULL,
    provider TEXT NOT NULL,
    status TEXT NOT NULL,
    operations_applied INTEGER NOT NULL DEFAULT 0,
    error TEXT NULL,
    dry_run INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS so_locks (
    lock_name TEXT PRIMARY KEY,
    owner TEXT NOT NULL,
    acquired_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS so_upgrade_operations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    history_id INTEGER NOT NULL,
    operation_kind TEXT NOT NULL,
    schema_name TEXT NOT NULL,
    table_name TEXT NOT NULL,
    column_name TEXT NULL,
    index_name TEXT NULL,
    started_at_utc TEXT NOT NULL,
    completed_at_utc TEXT NULL,
    status TEXT NOT NULL,
    error TEXT NULL
);";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public override async Task<DatabaseSchema> ReadCurrentSchemaAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var tables = new List<TableSchema>();
        await using var listCmd = connection.CreateCommand();
        listCmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";

        await using var reader = await listCmd.ExecuteReaderAsync(cancellationToken);
        var tableNames = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tableNames.Add(reader.GetString(0));
        }

        foreach (var tableName in tableNames)
        {
            var columns = new List<ColumnSchema>();
            await using var pragmaCmd = connection.CreateCommand();
            pragmaCmd.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)});";

            await using var colReader = await pragmaCmd.ExecuteReaderAsync(cancellationToken);
            while (await colReader.ReadAsync(cancellationToken))
            {
                var columnName = colReader.GetString(1);
                var storeType = colReader.IsDBNull(2) ? "TEXT" : colReader.GetString(2);
                var notNull = colReader.GetInt32(3) == 1;
                var isPk = colReader.GetInt32(5) == 1;
                columns.Add(new ColumnSchema(columnName, storeType, !notNull, isPk));
            }

            tables.Add(new TableSchema
            {
                Schema = DefaultSchema,
                Name = tableName,
                Columns = columns
            });
        }

        var indexes = new List<IndexSchema>();
        var foreignKeys = new List<ForeignKeySchema>();
        foreach (var tableName in tableNames)
        {
            await using var indexListCmd = connection.CreateCommand();
            indexListCmd.CommandText = $"PRAGMA index_list({QuoteIdentifier(tableName)});";

            await using var indexListReader = await indexListCmd.ExecuteReaderAsync(cancellationToken);
            var tableIndexes = new List<(string Name, bool IsUnique)>();
            while (await indexListReader.ReadAsync(cancellationToken))
            {
                var indexName = indexListReader.GetString(1);
                var unique = indexListReader.GetInt32(2) == 1;
                if (indexName.StartsWith("sqlite_autoindex_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                tableIndexes.Add((indexName, unique));
            }

            foreach (var (indexName, unique) in tableIndexes)
            {
                await using var infoCmd = connection.CreateCommand();
                infoCmd.CommandText = $"PRAGMA index_info({QuoteIdentifier(indexName)});";
                await using var infoReader = await infoCmd.ExecuteReaderAsync(cancellationToken);
                var cols = new List<(int Seq, string Name)>();
                while (await infoReader.ReadAsync(cancellationToken))
                {
                    cols.Add((infoReader.GetInt32(0), infoReader.GetString(2)));
                }

                indexes.Add(new IndexSchema(DefaultSchema, tableName, indexName, cols.OrderBy(c => c.Seq).Select(c => c.Name).ToArray(), unique));
            }

            await using var fkCmd = connection.CreateCommand();
            fkCmd.CommandText = $"PRAGMA foreign_key_list({QuoteIdentifier(tableName)});";
            await using var fkReader = await fkCmd.ExecuteReaderAsync(cancellationToken);
            var fkGrouped = new Dictionary<int, (string RefTable, List<(int Seq, string From, string To)> Cols)>();
            while (await fkReader.ReadAsync(cancellationToken))
            {
                var id = fkReader.GetInt32(0);
                var seq = fkReader.GetInt32(1);
                var refTable = fkReader.GetString(2);
                var fromCol = fkReader.GetString(3);
                var toCol = fkReader.GetString(4);
                if (!fkGrouped.TryGetValue(id, out var state))
                {
                    state = (refTable, new List<(int, string, string)>());
                }

                state.Cols.Add((seq, fromCol, toCol));
                fkGrouped[id] = state;
            }

            foreach (var kvp in fkGrouped)
            {
                var name = $"FK_{tableName}_{kvp.Key}";
                foreignKeys.Add(new ForeignKeySchema(
                    DefaultSchema,
                    tableName,
                    name,
                    kvp.Value.Cols.OrderBy(c => c.Seq).Select(c => c.From).ToArray(),
                    DefaultSchema,
                    kvp.Value.RefTable,
                    kvp.Value.Cols.OrderBy(c => c.Seq).Select(c => c.To).ToArray()));
            }
        }

        return new DatabaseSchema { Tables = tables, Indexes = indexes, ForeignKeys = foreignKeys };
    }

    public override string QuoteIdentifier(string identifier) => $"\"{identifier}\"";

    public override string BuildCreateTableSql(TableSchema table)
    {
        var columns = string.Join(", ", table.Columns.Select(BuildColumnDefinition));
        return $"CREATE TABLE {QuoteIdentifier(table.Name)} ({columns});";
    }

    public override string BuildAddColumnSql(TableSchema table, ColumnSchema column) =>
        $"ALTER TABLE {QuoteIdentifier(table.Name)} ADD COLUMN {BuildColumnDefinition(column)};";

    public override string BuildAlterColumnSql(TableSchema table, ColumnSchema currentColumn, ColumnSchema desiredColumn) =>
        throw new NotSupportedException("SQLite in-place alter column is not supported by this adapter.");

    public override string BuildCreateIndexSql(IndexSchema index)
    {
        var unique = index.IsUnique ? "UNIQUE " : string.Empty;
        var columns = string.Join(", ", index.Columns.Select(QuoteIdentifier));
        return $"CREATE {unique}INDEX {QuoteIdentifier(index.Name)} ON {QuoteIdentifier(index.Table)} ({columns});";
    }

    public override string BuildDropIndexSql(IndexSchema index) =>
        $"DROP INDEX {QuoteIdentifier(index.Name)};";

    public override string BuildCreateForeignKeySql(ForeignKeySchema foreignKey) =>
        throw new NotSupportedException("SQLite add foreign key requires table rebuild and is not supported as a standalone operation.");

    public override string BuildDropForeignKeySql(ForeignKeySchema foreignKey) =>
        throw new NotSupportedException("SQLite drop foreign key requires table rebuild and is not supported as a standalone operation.");

    public override string BuildRenameTableSql(string schema, string table, string newTableName) =>
        $"ALTER TABLE {QuoteIdentifier(table)} RENAME TO {QuoteIdentifier(newTableName)};";

    public override string BuildRenameColumnSql(string schema, string table, string column, string newColumnName) =>
        $"ALTER TABLE {QuoteIdentifier(table)} RENAME COLUMN {QuoteIdentifier(column)} TO {QuoteIdentifier(newColumnName)};";

    public override string BuildDropTableSql(string schema, string table) =>
        $"DROP TABLE {QuoteIdentifier(table)};";

    public override string BuildDropColumnSql(string schema, string table, string column) =>
        $"ALTER TABLE {QuoteIdentifier(table)} DROP COLUMN {QuoteIdentifier(column)};";

    protected override string BuildStartHistorySql() =>
        "INSERT INTO so_upgrade_history(started_at_utc, provider, status, dry_run) VALUES (@started_at_utc, @provider, @status, @dry_run); SELECT last_insert_rowid();";

    protected override string BuildAcquireLockSql() =>
        "INSERT OR IGNORE INTO so_locks(lock_name, owner, acquired_at_utc) VALUES (@lock_name, @owner, @acquired_at_utc);";

    private string BuildColumnDefinition(ColumnSchema column)
    {
        var nullableToken = column.IsNullable ? "NULL" : "NOT NULL";
        var pkToken = column.IsPrimaryKey ? " PRIMARY KEY" : string.Empty;
        return $"{QuoteIdentifier(column.Name)} {column.StoreType} {nullableToken}{pkToken}";
    }
}
