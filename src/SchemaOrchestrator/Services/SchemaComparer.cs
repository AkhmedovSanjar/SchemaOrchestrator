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
                        CurrentTableDefinition: currentTable,
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

        AddIndexOperations(operations, current, desired);

        return OrderOperations(operations);
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

    private static void AddIndexOperations(List<SchemaOperation> operations, DatabaseSchema current, DatabaseSchema desired)
    {
        var currentIndexes = current.Indexes.ToDictionary(static i => i.Key, StringComparer.OrdinalIgnoreCase);
        var desiredIndexes = desired.Indexes.ToDictionary(static i => i.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var removed in current.Indexes.Where(i => !desiredIndexes.ContainsKey(i.Key)))
        {
            if (removed.Name.StartsWith("so_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            operations.Add(new SchemaOperation(
                Kind: SchemaOperationKind.DropIndex,
                Schema: removed.Schema,
                Table: removed.Table,
                IndexDefinition: removed));
        }

        foreach (var desiredIndex in desired.Indexes)
        {
            if (!currentIndexes.TryGetValue(desiredIndex.Key, out var currentIndex))
            {
                operations.Add(new SchemaOperation(
                    Kind: SchemaOperationKind.CreateIndex,
                    Schema: desiredIndex.Schema,
                    Table: desiredIndex.Table,
                    IndexDefinition: desiredIndex));
                continue;
            }

            if (HasIndexChanged(currentIndex, desiredIndex))
            {
                operations.Add(new SchemaOperation(
                    Kind: SchemaOperationKind.DropIndex,
                    Schema: currentIndex.Schema,
                    Table: currentIndex.Table,
                    IndexDefinition: currentIndex));
                operations.Add(new SchemaOperation(
                    Kind: SchemaOperationKind.CreateIndex,
                    Schema: desiredIndex.Schema,
                    Table: desiredIndex.Table,
                    IndexDefinition: desiredIndex));
            }
        }
    }

    private static bool HasIndexChanged(IndexSchema current, IndexSchema desired)
    {
        if (current.IsUnique != desired.IsUnique)
        {
            return true;
        }

        if (current.Columns.Count != desired.Columns.Count)
        {
            return true;
        }

        for (var i = 0; i < current.Columns.Count; i++)
        {
            if (!string.Equals(current.Columns[i], desired.Columns[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<SchemaOperation> OrderOperations(IEnumerable<SchemaOperation> operations)
    {
        return operations
            .OrderBy(op => GetOrder(op.Kind))
            .ThenBy(op => op.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(op => op.Table, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int GetOrder(SchemaOperationKind kind) => kind switch
    {
        SchemaOperationKind.DropIndex => 10,
        SchemaOperationKind.CreateTable => 20,
        SchemaOperationKind.AddColumn => 30,
        SchemaOperationKind.AlterColumn => 40,
        SchemaOperationKind.CreateIndex => 50,
        SchemaOperationKind.ArchiveColumn => 60,
        SchemaOperationKind.ArchiveTable => 70,
        _ => 999
    };
}
