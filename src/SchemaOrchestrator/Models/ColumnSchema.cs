namespace SchemaOrchestrator.Models;

public sealed record ColumnSchema(
    string Name,
    string StoreType,
    bool IsNullable,
    bool IsPrimaryKey = false);
