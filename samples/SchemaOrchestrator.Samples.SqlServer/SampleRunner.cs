using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Samples.SqlServer.Contexts;

namespace SchemaOrchestrator.Samples.SqlServer;

public static class SampleRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var scenario = args[0].Trim().ToLowerInvariant();
        var connectionString = args.Length > 1
            ? args[1]
            : Environment.GetEnvironmentVariable("SO_SAMPLE_SQLSERVER_CONN")
              ?? "Server=localhost,1433;Database=SchemaOrchestratorSample;User Id=sa;Password=YourStrong!Passw0rd;Encrypt=False;TrustServerCertificate=True";

        switch (scenario)
        {
            case "init-v1":
                await using (var services = BuildServices<SampleSqlServerV1Context>(connectionString))
                await using (var scope = services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<SampleSqlServerV1Context>();
                    await db.Database.EnsureDeletedAsync();
                    await db.Database.EnsureCreatedAsync();
                    Console.WriteLine("Initialized V1 schema.");
                }
                break;

            case "upgrade-v2":
                await using (var services = BuildServices<SampleSqlServerV2Context>(connectionString))
                await using (var scope = services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<SampleSqlServerV2Context>();
                    var report = await db.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false, EnableDataSubscribers = true });
                    PrintReport(report);
                }
                break;

            case "dry-run":
                await using (var services = BuildServices<SampleSqlServerV2Context>(connectionString))
                await using (var scope = services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<SampleSqlServerV2Context>();
                    var report = await db.UpgradeSchemaAsync(new UpgradeOptions { DryRun = true, EnableDataSubscribers = true });
                    PrintReport(report);
                }
                break;

            case "cleanup":
                await using (var services = BuildServices<SampleSqlServerV2Context>(connectionString))
                await using (var scope = services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<SampleSqlServerV2Context>();
                    var removed = await db.CleanupArchivedObjectsAsync(dryRun: false);
                    Console.WriteLine($"Cleanup removed archived objects: {removed}");
                }
                break;

            case "restore-latest-archive":
                await using (var services = BuildServices<SampleSqlServerV2Context>(connectionString))
                await using (var scope = services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<SampleSqlServerV2Context>();
                    await db.Database.OpenConnectionAsync();
                    try
                    {
                        await using var cmd = db.Database.GetDbConnection().CreateCommand();
                        cmd.CommandText = "SELECT TOP(1) id FROM so_archived_objects ORDER BY id DESC";
                        var value = await cmd.ExecuteScalarAsync();
                        if (value is null || value is DBNull)
                        {
                            Console.WriteLine("No archived objects found.");
                            break;
                        }

                        var id = Convert.ToInt64(value);
                        var restored = await db.RestoreArchivedObjectAsync(id);
                        Console.WriteLine($"Restore latest archive id={id}: {restored}");
                    }
                    finally
                    {
                        await db.Database.CloseConnectionAsync();
                    }
                }
                break;

            default:
                PrintUsage();
                return 1;
        }

        return 0;
    }

    private static ServiceProvider BuildServices<TContext>(string connectionString)
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSchemaOrchestrator(options =>
        {
            options.MigrationsPolicy = DestructiveChangePolicy.Archive;
            options.ArchiveTtl = TimeSpan.FromDays(7);
        });
        services.AddSchemaOrchestratorSubscribersFromAssemblies(Assembly.GetExecutingAssembly());
        services.AddDbContext<TContext>(opt => opt.UseSqlServer(connectionString));
        return services.BuildServiceProvider();
    }

    private static void PrintReport(UpgradeReport report)
    {
        Console.WriteLine($"Provider: {report.Provider}");
        Console.WriteLine($"DryRun: {report.DryRun}");
        Console.WriteLine($"Planned: {report.PlannedOperations.Count}");
        Console.WriteLine($"Executed: {report.ExecutedOperations}");
        foreach (var op in report.PlannedOperations.Take(15))
        {
            Console.WriteLine($" - {op.Kind} {op.Schema}.{op.Table} {op.Column}");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project samples/SchemaOrchestrator.Samples.SqlServer -- <scenario> [connectionString]");
        Console.WriteLine("Scenarios: init-v1 | upgrade-v2 | dry-run | cleanup | restore-latest-archive");
    }
}
