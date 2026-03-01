namespace SchemaOrchestrator.Models;

public enum SchemaOperationKind
{
    DropForeignKey,
    CreateTable,
    AddColumn,
    AlterColumn,
    DropIndex,
    CreateIndex,
    CreateForeignKey,
    ArchiveTable,
    ArchiveColumn
}
