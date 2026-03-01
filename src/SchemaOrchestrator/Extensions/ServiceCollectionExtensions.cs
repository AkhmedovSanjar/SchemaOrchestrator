using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchemaOrchestrator.Abstractions;
using SchemaOrchestrator.Adapters;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Services;

namespace SchemaOrchestrator.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchemaOrchestrator(this IServiceCollection services, Action<SchemaOrchestratorOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddOptions<SchemaOrchestratorOptions>();

        services.AddSingleton<IProviderAdapter, SqlServerProviderAdapter>();
        services.AddSingleton<IProviderAdapter, PostgreSqlProviderAdapter>();
        services.AddSingleton<IProviderAdapter, SqliteProviderAdapter>();

        services.AddSingleton<ProviderResolver>();
        services.AddSingleton<ISchemaComparer, SchemaComparer>();
        services.AddScoped<ISchemaOrchestrator, SchemaOrchestratorService>();
        services.AddScoped<ISchemaCleanupWorker, SchemaCleanupWorker>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISchemaUpgradeSubscriber, NoOpSchemaUpgradeSubscriber>());

        return services;
    }
}
