using Microsoft.EntityFrameworkCore;

namespace SchemaOrchestrator.Abstractions;

public interface ISchemaCleanupWorker
{
    Task<int> CleanupArchivedObjectsAsync(DbContext dbContext, CancellationToken cancellationToken = default);
}
