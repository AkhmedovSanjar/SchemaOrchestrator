namespace SchemaOrchestrator.Models;

public sealed class DatabaseSchema
{
    public IReadOnlyList<TableSchema> Tables { get; init; } = Array.Empty<TableSchema>();
}
