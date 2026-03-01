using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Services;

internal static class EfModelSchemaBuilder
{
    public static DatabaseSchema Build(DbContext dbContext, string defaultSchema)
    {
        var tables = new Dictionary<string, TableSchema>(StringComparer.OrdinalIgnoreCase);

        foreach (var entityType in dbContext.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (string.IsNullOrWhiteSpace(tableName))
            {
                continue;
            }

            var schema = entityType.GetSchema() ?? defaultSchema;
            var storeObject = StoreObjectIdentifier.Table(tableName, schema);

            var columns = new List<ColumnSchema>();
            var pkProperties = entityType.FindPrimaryKey()?.Properties;
            var pkNames = pkProperties is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : pkProperties
                    .Select(p => p.GetColumnName(storeObject))
                    .Where(static n => !string.IsNullOrWhiteSpace(n))
                    .Cast<string>()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName(storeObject);
                if (string.IsNullOrWhiteSpace(columnName))
                {
                    continue;
                }

                var columnType = property.GetColumnType();
                if (string.IsNullOrWhiteSpace(columnType))
                {
                    columnType = ResolveFallbackStoreType(property.ClrType);
                }

                columns.Add(new ColumnSchema(
                    Name: columnName!,
                    StoreType: columnType!,
                    IsNullable: property.IsColumnNullable(storeObject),
                    IsPrimaryKey: pkNames.Contains(columnName!)));
            }

            var key = string.Create(CultureInfo.InvariantCulture, $"{schema}.{tableName}");
            tables[key] = new TableSchema
            {
                Schema = schema,
                Name = tableName,
                Columns = columns
            };
        }

        return new DatabaseSchema
        {
            Tables = tables.Values.OrderBy(static t => t.Key, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static string ResolveFallbackStoreType(Type clrType)
    {
        if (clrType == typeof(int) || clrType == typeof(int?)) return "INTEGER";
        if (clrType == typeof(long) || clrType == typeof(long?)) return "BIGINT";
        if (clrType == typeof(bool) || clrType == typeof(bool?)) return "BOOLEAN";
        if (clrType == typeof(DateTime) || clrType == typeof(DateTime?)) return "DATETIME";
        if (clrType == typeof(decimal) || clrType == typeof(decimal?)) return "DECIMAL(18,2)";

        return "TEXT";
    }
}
