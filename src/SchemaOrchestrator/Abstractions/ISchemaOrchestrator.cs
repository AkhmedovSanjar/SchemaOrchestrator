using Microsoft.EntityFrameworkCore;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Abstractions;

public interface ISchemaOrchestrator
{
    Task<UpgradeReport> UpgradeSchemaAsync(DbContext dbContext, UpgradeOptions? options = null, CancellationToken cancellationToken = default);
}
