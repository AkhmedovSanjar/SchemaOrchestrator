using System.Reflection;
using SchemaOrchestrator.Abstractions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Subscribers.Attributes;

namespace SchemaOrchestrator.Subscribers;

internal sealed class ReflectedSchemaUpgradeSubscriber : ISchemaUpgradeSubscriber
{
    private readonly object _instance;
    private readonly List<MethodInfo> _onStart;
    private readonly List<MethodInfo> _before;
    private readonly List<MethodInfo> _after;
    private readonly List<MethodInfo> _onComplete;
    private readonly List<MethodInfo> _onFailed;

    public ReflectedSchemaUpgradeSubscriber(object instance, IEnumerable<MethodInfo> methods)
    {
        _instance = instance;
        _onStart = methods.Where(m => m.GetCustomAttribute<OnUpgradeStartedAttribute>() is not null).ToList();
        _before = methods.Where(m => m.GetCustomAttribute<BeforeOperationAttribute>() is not null).ToList();
        _after = methods.Where(m => m.GetCustomAttribute<AfterOperationAttribute>() is not null).ToList();
        _onComplete = methods.Where(m => m.GetCustomAttribute<OnUpgradeCompletedAttribute>() is not null).ToList();
        _onFailed = methods.Where(m => m.GetCustomAttribute<OnUpgradeFailedAttribute>() is not null).ToList();
    }

    public async Task OnUpgradeStartedAsync(UpgradeExecutionContext context, CancellationToken cancellationToken)
    {
        foreach (var method in _onStart)
        {
            await InvokeAsync(method, context, null, null, null, cancellationToken);
        }
    }

    public async Task BeforeOperationAsync(UpgradeExecutionContext context, SchemaOperation operation, CancellationToken cancellationToken)
    {
        foreach (var method in _before)
        {
            await InvokeAsync(method, context, operation, null, null, cancellationToken);
        }
    }

    public async Task AfterOperationAsync(UpgradeExecutionContext context, SchemaOperation operation, CancellationToken cancellationToken)
    {
        foreach (var method in _after)
        {
            await InvokeAsync(method, context, operation, null, null, cancellationToken);
        }
    }

    public async Task OnUpgradeCompletedAsync(UpgradeExecutionContext context, UpgradeReport report, CancellationToken cancellationToken)
    {
        foreach (var method in _onComplete)
        {
            await InvokeAsync(method, context, null, report, null, cancellationToken);
        }
    }

    public async Task OnUpgradeFailedAsync(UpgradeExecutionContext context, Exception exception, CancellationToken cancellationToken)
    {
        foreach (var method in _onFailed)
        {
            await InvokeAsync(method, context, null, null, exception, cancellationToken);
        }
    }

    private async Task InvokeAsync(
        MethodInfo method,
        UpgradeExecutionContext context,
        SchemaOperation? operation,
        UpgradeReport? report,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        var args = BuildArguments(method, context, operation, report, exception, cancellationToken);
        var result = method.Invoke(_instance, args);
        if (result is Task task)
        {
            await task;
        }
    }

    private static object?[] BuildArguments(
        MethodInfo method,
        UpgradeExecutionContext context,
        SchemaOperation? operation,
        UpgradeReport? report,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        return method.GetParameters().Select(param =>
        {
            if (param.ParameterType == typeof(UpgradeExecutionContext)) return (object?)context;
            if (param.ParameterType == typeof(SchemaOperation)) return operation;
            if (param.ParameterType == typeof(UpgradeReport)) return report;
            if (param.ParameterType == typeof(Exception)) return exception;
            if (param.ParameterType == typeof(CancellationToken)) return (object?)cancellationToken;

            throw new InvalidOperationException($"Unsupported subscriber parameter type '{param.ParameterType.Name}' in method '{method.Name}'.");
        }).ToArray();
    }
}
