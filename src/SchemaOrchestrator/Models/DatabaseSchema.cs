namespace SchemaOrchestrator.Models;

public sealed class DatabaseSchema
{
    public IReadOnlyList<TableSchema> Tables { get; init; } = Array.Empty<TableSchema>();
    public IReadOnlyList<IndexSchema> Indexes { get; init; } = Array.Empty<IndexSchema>();
}
