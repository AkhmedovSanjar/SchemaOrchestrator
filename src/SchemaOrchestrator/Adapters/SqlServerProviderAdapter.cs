using System.Data.Common;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Adapters;

public sealed class SqlServerProviderAdapter : ProviderAdapterBase
{
    public override string Name => "sqlserver";
    public override string DefaultSchema => "dbo";

    public override bool CanHandle(string providerName) =>
        providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);

    public override async Task EnsureOrchestratorTablesAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var sql = @"
IF OBJECT_ID(N'so_archived_objects', N'U') IS NULL
BEGIN
    CREATE TABLE so_archived_objects (
        id BIGINT IDENTITY(1,1) PRIMARY KEY,
        object_type NVARCHAR(40) NOT NULL,
        schema_name NVARCHAR(128) NOT NULL,
        table_name NVARCHAR(128) NOT NULL,
        column_name NVARCHAR(128) NULL,
        archived_name NVARCHAR(256) NOT NULL,
        archived_at_utc DATETIME2 NOT NULL,
        cleanup_after_utc DATETIME2 NOT NULL,
        provider NVARCHAR(40) NOT NULL
    );
END;

IF OBJECT_ID(N'so_upgrade_history', N'U') IS NULL
BEGIN
    CREATE TABLE so_upgrade_history (
        id BIGINT IDENTITY(1,1) PRIMARY KEY,
        started_at_utc DATETIME2 NOT NULL,
        completed_at_utc DATETIME2 NULL,
        provider NVARCHAR(40) NOT NULL,
        status NVARCHAR(20) NOT NULL,
        operations_applied INT NOT NULL DEFAULT 0,
        error NVARCHAR(MAX) NULL,
        dry_run BIT NOT NULL
    );
END;

IF OBJECT_ID(N'so_locks', N'U') IS NULL
BEGIN
    CREATE TABLE so_locks (
        lock_name NVARCHAR(100) NOT NULL PRIMARY KEY,
        owner NVARCHAR(100) NOT NULL,
        acquired_at_utc DATETIME2 NOT NULL
    );
END;";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public override async Task<DatabaseSchema> ReadCurrentSchemaAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT
    c.TABLE_SCHEMA,
    c.TABLE_NAME,
    c.COLUMN_NAME,
    c.DATA_TYPE,
    c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
JOIN INFORMATION_SCHEMA.TABLES t
    ON t.TABLE_SCHEMA = c.TABLE_SCHEMA
   AND t.TABLE_NAME = c.TABLE_NAME
WHERE t.TABLE_TYPE = 'BASE TABLE';";

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

    public override string QuoteIdentifier(string identifier) => $"[{identifier}]";

    public override string BuildCreateTableSql(TableSchema table)
    {
        var schemaPrefix = QuoteIdentifier(table.Schema) + ".";
        var columns = string.Join(", ", table.Columns.Select(BuildColumnDefinition));
        return $"CREATE TABLE {schemaPrefix}{QuoteIdentifier(table.Name)} ({columns});";
    }

    public override string BuildAddColumnSql(TableSchema table, ColumnSchema column)
    {
        var schemaPrefix = QuoteIdentifier(table.Schema) + ".";
        return $"ALTER TABLE {schemaPrefix}{QuoteIdentifier(table.Name)} ADD {BuildColumnDefinition(column)};";
    }

    public override string BuildRenameTableSql(string schema, string table, string newTableName) =>
        $"EXEC sp_rename '{schema}.{table}', '{newTableName}';";

    public override string BuildRenameColumnSql(string schema, string table, string column, string newColumnName) =>
        $"EXEC sp_rename '{schema}.{table}.{column}', '{newColumnName}', 'COLUMN';";

    protected override string BuildStartHistorySql() =>
        "INSERT INTO so_upgrade_history(started_at_utc, provider, status, dry_run) VALUES (@started_at_utc, @provider, @status, @dry_run); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

    protected override string BuildAcquireLockSql() =>
        "INSERT INTO so_locks(lock_name, owner, acquired_at_utc) SELECT @lock_name, @owner, @acquired_at_utc WHERE NOT EXISTS (SELECT 1 FROM so_locks WHERE lock_name = @lock_name);";

    private string BuildColumnDefinition(ColumnSchema column)
    {
        var nullableToken = column.IsNullable ? "NULL" : "NOT NULL";
        var pkToken = column.IsPrimaryKey ? " PRIMARY KEY" : string.Empty;
        return $"{QuoteIdentifier(column.Name)} {column.StoreType} {nullableToken}{pkToken}";
    }
}
