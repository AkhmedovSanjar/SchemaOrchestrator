using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Tests.Common;

internal static class TestServiceFactory
{
    public static ServiceProvider BuildSqliteServices<TContext>(Microsoft.Data.Sqlite.SqliteConnection connection)
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSchemaOrchestrator(options =>
        {
            options.MigrationsPolicy = DestructiveChangePolicy.Archive;
            options.ArchiveTtl = TimeSpan.Zero;
        });

        services.AddDbContext<TContext>(opt => opt.UseSqlite(connection));
        return services.BuildServiceProvider();
    }

    public static ServiceProvider BuildSqlServerServices<TContext>(string connectionString)
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSchemaOrchestrator(options =>
        {
            options.MigrationsPolicy = DestructiveChangePolicy.Archive;
            options.ArchiveTtl = TimeSpan.Zero;
        });

        services.AddDbContext<TContext>(opt => opt.UseSqlServer(connectionString));
        return services.BuildServiceProvider();
    }

    public static ServiceProvider BuildPostgreSqlServices<TContext>(string connectionString)
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSchemaOrchestrator(options =>
        {
            options.MigrationsPolicy = DestructiveChangePolicy.Archive;
            options.ArchiveTtl = TimeSpan.Zero;
        });

        services.AddDbContext<TContext>(opt => opt.UseNpgsql(connectionString));
        return services.BuildServiceProvider();
    }
}
