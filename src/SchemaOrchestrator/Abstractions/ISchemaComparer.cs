using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Abstractions;

public interface ISchemaComparer
{
    IReadOnlyList<SchemaOperation> Compare(DatabaseSchema current, DatabaseSchema desired, DestructiveChangePolicy policy);
}
