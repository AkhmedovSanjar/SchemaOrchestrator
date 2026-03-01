using Microsoft.EntityFrameworkCore;

namespace SchemaOrchestrator.Abstractions;

public interface ISchemaCleanupWorker
{
    Task<int> CleanupArchivedObjectsAsync(DbContext dbContext, bool dryRun = false, CancellationToken cancellationToken = default);
    Task<bool> RestoreArchivedObjectAsync(DbContext dbContext, long archiveId, CancellationToken cancellationToken = default);
}
