namespace SchemaOrchestrator.Models;

public sealed record SchemaOperation(
    SchemaOperationKind Kind,
    string Schema,
    string Table,
    string? Column = null,
    string? ArchiveName = null,
    ColumnSchema? CurrentColumnDefinition = null,
    ColumnSchema? ColumnDefinition = null,
    TableSchema? TableDefinition = null);
