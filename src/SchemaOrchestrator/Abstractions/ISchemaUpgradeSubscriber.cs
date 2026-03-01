using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Abstractions;

public interface ISchemaUpgradeSubscriber
{
    Task OnUpgradeStartedAsync(DbContext dbContext, DbConnection connection, UpgradeSubscriberContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    Task BeforeOperationAsync(DbContext dbContext, DbConnection connection, UpgradeSubscriberContext context, SchemaOperation operation, CancellationToken cancellationToken) => Task.CompletedTask;

    Task AfterOperationAsync(DbContext dbContext, DbConnection connection, UpgradeSubscriberContext context, SchemaOperation operation, CancellationToken cancellationToken) => Task.CompletedTask;

    Task OnUpgradeCompletedAsync(DbContext dbContext, DbConnection connection, UpgradeSubscriberContext context, UpgradeReport report, CancellationToken cancellationToken) => Task.CompletedTask;

    Task OnUpgradeFailedAsync(DbContext dbContext, DbConnection connection, UpgradeSubscriberContext context, Exception exception, CancellationToken cancellationToken) => Task.CompletedTask;
}
