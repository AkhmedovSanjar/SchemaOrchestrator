namespace SchemaOrchestrator.Models;

public sealed class TableSchema
{
    public required string Schema { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<ColumnSchema> Columns { get; init; } = Array.Empty<ColumnSchema>();

    public string Key => $"{Schema}.{Name}";
}
