namespace SchemaOrchestrator.Models;

public enum SchemaOperationKind
{
    CreateTable,
    AddColumn,
    AlterColumn,
    DropIndex,
    CreateIndex,
    ArchiveTable,
    ArchiveColumn
}
