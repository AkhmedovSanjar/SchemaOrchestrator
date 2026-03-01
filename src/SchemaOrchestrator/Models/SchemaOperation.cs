namespace SchemaOrchestrator.Models;

public sealed record SchemaOperation(
    SchemaOperationKind Kind,
    string Schema,
    string Table,
    string? Column = null,
    string? ArchiveName = null,
    ColumnSchema? CurrentColumnDefinition = null,
    TableSchema? CurrentTableDefinition = null,
    ColumnSchema? ColumnDefinition = null,
    TableSchema? TableDefinition = null,
    IndexSchema? IndexDefinition = null);
