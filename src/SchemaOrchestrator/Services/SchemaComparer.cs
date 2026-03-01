using SchemaOrchestrator.Abstractions;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Services;

public sealed class SchemaComparer : ISchemaComparer
{
    public IReadOnlyList<SchemaOperation> Compare(DatabaseSchema current, DatabaseSchema desired, DestructiveChangePolicy policy)
    {
        var operations = new List<SchemaOperation>();

        var currentTables = current.Tables.ToDictionary(static t => t.Key, StringComparer.OrdinalIgnoreCase);
        var desiredTables = desired.Tables.ToDictionary(static t => t.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var desiredTable in desired.Tables)
        {
            if (!currentTables.TryGetValue(desiredTable.Key, out var currentTable))
            {
                operations.Add(new SchemaOperation(
                    Kind: SchemaOperationKind.CreateTable,
                    Schema: desiredTable.Schema,
                    Table: desiredTable.Name,
                    TableDefinition: desiredTable));
                continue;
            }

            var currentColumns = currentTable.Columns.ToDictionary(static c => c.Name, StringComparer.OrdinalIgnoreCase);
            var desiredColumns = desiredTable.Columns.ToDictionary(static c => c.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var desiredColumn in desiredTable.Columns)
            {
                if (!currentColumns.TryGetValue(desiredColumn.Name, out var currentColumn))
                {
                    operations.Add(new SchemaOperation(
                        Kind: SchemaOperationKind.AddColumn,
                        Schema: desiredTable.Schema,
                        Table: desiredTable.Name,
                        Column: desiredColumn.Name,
                        ColumnDefinition: desiredColumn,
                        TableDefinition: desiredTable));
                    continue;
                }

                if (HasDefinitionChanged(currentColumn, desiredColumn))
                {
                    operations.Add(new SchemaOperation(
                        Kind: SchemaOperationKind.AlterColumn,
                        Schema: desiredTable.Schema,
                        Table: desiredTable.Name,
                        Column: desiredColumn.Name,
                        CurrentColumnDefinition: currentColumn,
                        ColumnDefinition: desiredColumn,
                        TableDefinition: desiredTable));
                }
            }

            if (policy == DestructiveChangePolicy.Block)
            {
                continue;
            }

            foreach (var removedColumn in currentTable.Columns.Where(c => !desiredColumns.ContainsKey(c.Name)))
            {
                if (IsOrchestratorInternalColumn(removedColumn.Name))
                {
                    continue;
                }

                operations.Add(new SchemaOperation(
                    Kind: SchemaOperationKind.ArchiveColumn,
                    Schema: currentTable.Schema,
                    Table: currentTable.Name,
                    Column: removedColumn.Name));
            }
        }

        if (policy != DestructiveChangePolicy.Block)
        {
            foreach (var removedTable in current.Tables.Where(t => !desiredTables.ContainsKey(t.Key)))
            {
                if (IsOrchestratorInternalTable(removedTable.Name))
                {
                    continue;
                }

                operations.Add(new SchemaOperation(
                    Kind: SchemaOperationKind.ArchiveTable,
                    Schema: removedTable.Schema,
                    Table: removedTable.Name));
            }
        }

        return operations;
    }

    private static bool IsOrchestratorInternalTable(string tableName) =>
        tableName.StartsWith("so_", StringComparison.OrdinalIgnoreCase);

    private static bool IsOrchestratorInternalColumn(string columnName) =>
        columnName.StartsWith("so_", StringComparison.OrdinalIgnoreCase);

    private static bool HasDefinitionChanged(ColumnSchema current, ColumnSchema desired)
    {
        return !string.Equals(NormalizeStoreType(current.StoreType), NormalizeStoreType(desired.StoreType), StringComparison.OrdinalIgnoreCase)
            || current.IsNullable != desired.IsNullable;
    }

    private static string NormalizeStoreType(string value) => value.Replace(" ", string.Empty);
}
