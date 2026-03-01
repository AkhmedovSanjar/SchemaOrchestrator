using System.Data.Common;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Adapters;

public sealed class SqlServerProviderAdapter : ProviderAdapterBase
{
    public override string Name => "sqlserver";
    public override string DefaultSchema => "dbo";
    public override ProviderCapabilities Capabilities => new(
        SupportsInPlaceColumnAlter: true,
        SupportsIndexes: true,
        SupportsArchiveRename: true,
        SupportsForeignKeys: true,
        SupportsCheckConstraints: true);
    public override bool SupportsInPlaceColumnAlter => true;

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
END;

IF OBJECT_ID(N'so_upgrade_operations', N'U') IS NULL
BEGIN
    CREATE TABLE so_upgrade_operations (
        id BIGINT IDENTITY(1,1) PRIMARY KEY,
        history_id BIGINT NOT NULL,
        operation_kind NVARCHAR(50) NOT NULL,
        schema_name NVARCHAR(128) NOT NULL,
        table_name NVARCHAR(128) NOT NULL,
        column_name NVARCHAR(128) NULL,
        index_name NVARCHAR(128) NULL,
        started_at_utc DATETIME2 NOT NULL,
        completed_at_utc DATETIME2 NULL,
        status NVARCHAR(20) NOT NULL,
        error NVARCHAR(MAX) NULL
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

        var indexes = new List<IndexSchema>();
        await using (var indexCmd = connection.CreateCommand())
        {
            indexCmd.CommandText = @"
SELECT
    s.name AS schema_name,
    t.name AS table_name,
    i.name AS index_name,
    i.is_unique,
    c.name AS column_name,
    ic.key_ordinal
FROM sys.indexes i
JOIN sys.tables t ON i.object_id = t.object_id
JOIN sys.schemas s ON t.schema_id = s.schema_id
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.is_primary_key = 0
  AND i.is_unique_constraint = 0
  AND i.type > 0
  AND i.name IS NOT NULL
ORDER BY s.name, t.name, i.name, ic.key_ordinal;";

            await using var indexReader = await indexCmd.ExecuteReaderAsync(cancellationToken);
            var grouped = new Dictionary<string, (string Schema, string Table, string Name, bool Unique, List<(int Ordinal, string Column)> Columns)>(StringComparer.OrdinalIgnoreCase);
            while (await indexReader.ReadAsync(cancellationToken))
            {
                var schema = indexReader.GetString(0);
                var table = indexReader.GetString(1);
                var indexName = indexReader.GetString(2);
                var unique = indexReader.GetBoolean(3);
                var columnName = indexReader.GetString(4);
                var ordinal = indexReader.GetInt32(5);
                var key = $"{schema}.{table}.{indexName}";
                if (!grouped.TryGetValue(key, out var state))
                {
                    state = (schema, table, indexName, unique, new List<(int, string)>());
                }

                state.Columns.Add((ordinal, columnName));
                grouped[key] = state;
            }

            indexes.AddRange(grouped.Values.Select(g => new IndexSchema(g.Schema, g.Table, g.Name, g.Columns.OrderBy(x => x.Ordinal).Select(x => x.Column).ToArray(), g.Unique)));
        }

        var foreignKeys = new List<ForeignKeySchema>();
        await using (var fkCmd = connection.CreateCommand())
        {
            fkCmd.CommandText = @"
SELECT
    sch.name AS schema_name,
    tab.name AS table_name,
    fk.name AS fk_name,
    refsch.name AS ref_schema_name,
    reftab.name AS ref_table_name,
    col.name AS column_name,
    refcol.name AS ref_column_name,
    fkc.constraint_column_id
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
JOIN sys.tables tab ON tab.object_id = fk.parent_object_id
JOIN sys.schemas sch ON sch.schema_id = tab.schema_id
JOIN sys.columns col ON col.object_id = tab.object_id AND col.column_id = fkc.parent_column_id
JOIN sys.tables reftab ON reftab.object_id = fk.referenced_object_id
JOIN sys.schemas refsch ON refsch.schema_id = reftab.schema_id
JOIN sys.columns refcol ON refcol.object_id = reftab.object_id AND refcol.column_id = fkc.referenced_column_id
ORDER BY sch.name, tab.name, fk.name, fkc.constraint_column_id;";

            await using var fkReader = await fkCmd.ExecuteReaderAsync(cancellationToken);
            var grouped = new Dictionary<string, (string Schema, string Table, string Name, string RefSchema, string RefTable, List<(int Ord, string Col, string RefCol)> Cols)>(StringComparer.OrdinalIgnoreCase);
            while (await fkReader.ReadAsync(cancellationToken))
            {
                var schema = fkReader.GetString(0);
                var table = fkReader.GetString(1);
                var name = fkReader.GetString(2);
                var refSchema = fkReader.GetString(3);
                var refTable = fkReader.GetString(4);
                var col = fkReader.GetString(5);
                var refCol = fkReader.GetString(6);
                var ord = fkReader.GetInt32(7);
                var key = $"{schema}.{table}.{name}";
                if (!grouped.TryGetValue(key, out var state))
                {
                    state = (schema, table, name, refSchema, refTable, new List<(int, string, string)>());
                }

                state.Cols.Add((ord, col, refCol));
                grouped[key] = state;
            }

            foreignKeys.AddRange(grouped.Values.Select(g =>
                new ForeignKeySchema(
                    g.Schema,
                    g.Table,
                    g.Name,
                    g.Cols.OrderBy(x => x.Ord).Select(x => x.Col).ToArray(),
                    g.RefSchema,
                    g.RefTable,
                    g.Cols.OrderBy(x => x.Ord).Select(x => x.RefCol).ToArray())));
        }

        return new DatabaseSchema
        {
            Tables = tableMap.Select(kvp => new TableSchema
            {
                Schema = keyParts[kvp.Key].Schema,
                Name = keyParts[kvp.Key].Table,
                Columns = kvp.Value
            }).ToArray(),
            Indexes = indexes,
            ForeignKeys = foreignKeys
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

    public override string BuildAlterColumnSql(TableSchema table, ColumnSchema currentColumn, ColumnSchema desiredColumn)
    {
        var schemaPrefix = QuoteIdentifier(table.Schema) + ".";
        var nullableToken = desiredColumn.IsNullable ? "NULL" : "NOT NULL";
        return $"ALTER TABLE {schemaPrefix}{QuoteIdentifier(table.Name)} ALTER COLUMN {QuoteIdentifier(desiredColumn.Name)} {desiredColumn.StoreType} {nullableToken};";
    }

    public override string BuildCreateIndexSql(IndexSchema index)
    {
        var unique = index.IsUnique ? "UNIQUE " : string.Empty;
        var columns = string.Join(", ", index.Columns.Select(QuoteIdentifier));
        return $"CREATE {unique}INDEX {QuoteIdentifier(index.Name)} ON {QuoteIdentifier(index.Schema)}.{QuoteIdentifier(index.Table)} ({columns});";
    }

    public override string BuildDropIndexSql(IndexSchema index) =>
        $"DROP INDEX {QuoteIdentifier(index.Name)} ON {QuoteIdentifier(index.Schema)}.{QuoteIdentifier(index.Table)};";

    public override string BuildCreateForeignKeySql(ForeignKeySchema foreignKey)
    {
        var columns = string.Join(", ", foreignKey.Columns.Select(QuoteIdentifier));
        var refColumns = string.Join(", ", foreignKey.PrincipalColumns.Select(QuoteIdentifier));
        return
            $"ALTER TABLE {QuoteIdentifier(foreignKey.Schema)}.{QuoteIdentifier(foreignKey.Table)} " +
            $"ADD CONSTRAINT {QuoteIdentifier(foreignKey.Name)} FOREIGN KEY ({columns}) " +
            $"REFERENCES {QuoteIdentifier(foreignKey.PrincipalSchema)}.{QuoteIdentifier(foreignKey.PrincipalTable)} ({refColumns});";
    }

    public override string BuildDropForeignKeySql(ForeignKeySchema foreignKey) =>
        $"ALTER TABLE {QuoteIdentifier(foreignKey.Schema)}.{QuoteIdentifier(foreignKey.Table)} DROP CONSTRAINT {QuoteIdentifier(foreignKey.Name)};";

    public override string BuildRenameTableSql(string schema, string table, string newTableName) =>
        $"EXEC sp_rename '{schema}.{table}', '{newTableName}';";

    public override string BuildRenameColumnSql(string schema, string table, string column, string newColumnName) =>
        $"EXEC sp_rename '{schema}.{table}.{column}', '{newColumnName}', 'COLUMN';";

    public override string BuildDropTableSql(string schema, string table) =>
        $"DROP TABLE {QuoteIdentifier(schema)}.{QuoteIdentifier(table)};";

    public override string BuildDropColumnSql(string schema, string table, string column) =>
        $"ALTER TABLE {QuoteIdentifier(schema)}.{QuoteIdentifier(table)} DROP COLUMN {QuoteIdentifier(column)};";

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
