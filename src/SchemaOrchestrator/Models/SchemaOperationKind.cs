namespace SchemaOrchestrator.Models;

public enum SchemaOperationKind
{
    CreateTable,
    AddColumn,
    AlterColumn,
    ArchiveTable,
    ArchiveColumn
}
