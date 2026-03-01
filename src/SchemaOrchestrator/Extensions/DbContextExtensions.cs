using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Abstractions;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Extensions;

public static class DbContextExtensions
{
    public static Task<UpgradeReport> UpgradeSchemaAsync(this DbContext dbContext, UpgradeOptions? options = null, CancellationToken cancellationToken = default)
    {
        var service = dbContext.GetService<ISchemaOrchestrator>();
        return service.UpgradeSchemaAsync(dbContext, options, cancellationToken);
    }

    public static Task<int> CleanupArchivedObjectsAsync(this DbContext dbContext, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        var service = dbContext.GetService<ISchemaCleanupWorker>();
        return service.CleanupArchivedObjectsAsync(dbContext, dryRun, cancellationToken);
    }

    public static Task<bool> RestoreArchivedObjectAsync(this DbContext dbContext, long archiveId, CancellationToken cancellationToken = default)
    {
        var service = dbContext.GetService<ISchemaCleanupWorker>();
        return service.RestoreArchivedObjectAsync(dbContext, archiveId, cancellationToken);
    }
}
