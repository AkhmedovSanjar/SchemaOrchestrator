using SchemaOrchestrator.Models;
using SchemaOrchestrator.Subscribers;
using SchemaOrchestrator.Subscribers.Attributes;

namespace SchemaOrchestrator.Samples.Sqlite.Subscribers;

public sealed class DemoSqliteSubscriber
{
    [BeforeOperation]
    public Task Before(UpgradeExecutionContext context, SchemaOperation operation)
    {
        Console.WriteLine($"[subscriber] before {operation.Kind} {operation.Schema}.{operation.Table}");
        return Task.CompletedTask;
    }

    [AfterOperation]
    public Task After(UpgradeExecutionContext context, SchemaOperation operation)
    {
        Console.WriteLine($"[subscriber] after {operation.Kind} {operation.Schema}.{operation.Table}");
        return Task.CompletedTask;
    }
}
