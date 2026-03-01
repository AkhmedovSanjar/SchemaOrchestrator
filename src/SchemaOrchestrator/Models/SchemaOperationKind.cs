namespace SchemaOrchestrator.Models;

public enum SchemaOperationKind
{
    CreateTable,
    AddColumn,
    ArchiveTable,
    ArchiveColumn
}
