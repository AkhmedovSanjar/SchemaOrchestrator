using System.Data.Common;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Adapters;

public sealed class PostgreSqlProviderAdapter : ProviderAdapterBase
{
    public override string Name => "postgresql";
    public override string DefaultSchema => "public";

    public override bool CanHandle(string providerName) =>
        providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
        providerName.Contains("Postgre", StringComparison.OrdinalIgnoreCase);

    public override async Task EnsureOrchestratorTablesAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var sql = @"
CREATE TABLE IF NOT EXISTS so_archived_objects (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    object_type VARCHAR(40) NOT NULL,
    schema_name VARCHAR(128) NOT NULL,
    table_name VARCHAR(128) NOT NULL,
    column_name VARCHAR(128) NULL,
    archived_name VARCHAR(256) NOT NULL,
    archived_at_utc TIMESTAMP NOT NULL,
    cleanup_after_utc TIMESTAMP NOT NULL,
    provider VARCHAR(40) NOT NULL
);

CREATE TABLE IF NOT EXISTS so_upgrade_history (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    started_at_utc TIMESTAMP NOT NULL,
    completed_at_utc TIMESTAMP NULL,
    provider VARCHAR(40) NOT NULL,
    status VARCHAR(20) NOT NULL,
    operations_applied INT NOT NULL DEFAULT 0,
    error TEXT NULL,
    dry_run BOOLEAN NOT NULL
);

CREATE TABLE IF NOT EXISTS so_locks (
    lock_name VARCHAR(100) PRIMARY KEY,
    owner VARCHAR(100) NOT NULL,
    acquired_at_utc TIMESTAMP NOT NULL
);";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public override async Task<DatabaseSchema> ReadCurrentSchemaAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT
    c.table_schema,
    c.table_name,
    c.column_name,
    c.data_type,
    c.is_nullable
FROM information_schema.columns c
JOIN information_schema.tables t
    ON t.table_schema = c.table_schema
   AND t.table_name = c.table_name
WHERE t.table_type = 'BASE TABLE'
  AND c.table_schema NOT IN ('information_schema', 'pg_catalog');";

        var tableMap = new Dictionary<string, List<ColumnSchema>>(StringComparer.OrdinalIgnoreCase);
        var keyParts = new Dictionary<string, (string Schema, string Table)>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            var column = reader.GetString(2);
            var type = reader.GetString(3);
            var nullable = string.Equals(reader.GetString(4), "YES", StringComparison.OrdinalIgnoreCase);

            var key = $"{schema}.{table}";
            if (!tableMap.TryGetValue(key, out var cols))
            {
                cols = new List<ColumnSchema>();
                tableMap[key] = cols;
                keyParts[key] = (schema, table);
            }

            cols.Add(new ColumnSchema(column, type, nullable));
        }

        return new DatabaseSchema
        {
            Tables = tableMap.Select(kvp => new TableSchema
            {
                Schema = keyParts[kvp.Key].Schema,
                Name = keyParts[kvp.Key].Table,
                Columns = kvp.Value
            }).ToArray()
        };
    }

    public override string QuoteIdentifier(string identifier) => $"\"{identifier}\"";

    public override string BuildCreateTableSql(TableSchema table)
    {
        var prefix = QuoteIdentifier(table.Schema) + ".";
        var columns = string.Join(", ", table.Columns.Select(BuildColumnDefinition));
        return $"CREATE TABLE {prefix}{QuoteIdentifier(table.Name)} ({columns});";
    }

    public override string BuildAddColumnSql(TableSchema table, ColumnSchema column)
    {
        var prefix = QuoteIdentifier(table.Schema) + ".";
        return $"ALTER TABLE {prefix}{QuoteIdentifier(table.Name)} ADD COLUMN {BuildColumnDefinition(column)};";
    }

    public override string BuildRenameTableSql(string schema, string table, string newTableName) =>
        $"ALTER TABLE {QuoteIdentifier(schema)}.{QuoteIdentifier(table)} RENAME TO {QuoteIdentifier(newTableName)};";

    public override string BuildRenameColumnSql(string schema, string table, string column, string newColumnName) =>
        $"ALTER TABLE {QuoteIdentifier(schema)}.{QuoteIdentifier(table)} RENAME COLUMN {QuoteIdentifier(column)} TO {QuoteIdentifier(newColumnName)};";

    protected override string BuildStartHistorySql() =>
        "INSERT INTO so_upgrade_history(started_at_utc, provider, status, dry_run) VALUES (@started_at_utc, @provider, @status, @dry_run) RETURNING id;";

    protected override string BuildAcquireLockSql() =>
        "INSERT INTO so_locks(lock_name, owner, acquired_at_utc) VALUES (@lock_name, @owner, @acquired_at_utc) ON CONFLICT(lock_name) DO NOTHING;";

    private string BuildColumnDefinition(ColumnSchema column)
    {
        var nullableToken = column.IsNullable ? "NULL" : "NOT NULL";
        var pkToken = column.IsPrimaryKey ? " PRIMARY KEY" : string.Empty;
        return $"{QuoteIdentifier(column.Name)} {column.StoreType} {nullableToken}{pkToken}";
    }
}
