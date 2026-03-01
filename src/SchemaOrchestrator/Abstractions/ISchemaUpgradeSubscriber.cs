using SchemaOrchestrator.Models;
using SchemaOrchestrator.Subscribers;

namespace SchemaOrchestrator.Abstractions;

public interface ISchemaUpgradeSubscriber
{
    Task OnUpgradeStartedAsync(UpgradeExecutionContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    Task BeforeOperationAsync(UpgradeExecutionContext context, SchemaOperation operation, CancellationToken cancellationToken) => Task.CompletedTask;

    Task AfterOperationAsync(UpgradeExecutionContext context, SchemaOperation operation, CancellationToken cancellationToken) => Task.CompletedTask;

    Task OnUpgradeCompletedAsync(UpgradeExecutionContext context, UpgradeReport report, CancellationToken cancellationToken) => Task.CompletedTask;

    Task OnUpgradeFailedAsync(UpgradeExecutionContext context, Exception exception, CancellationToken cancellationToken) => Task.CompletedTask;
}
