using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.Sqlite.Schema;

public class SqliteLargeSchemaRegressionTests
{
    [Fact]
    public async Task DryRun_LargeSchema_ProducesDeterministicOperationOrder()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setup = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setup.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqliteLargeSchemaV1Context>(setup);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqliteLargeSchemaV1Context>();
                await ctx.Database.EnsureCreatedAsync();
            }

            List<string> first;
            List<string> second;

            await using (var run1 = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await run1.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqliteLargeSchemaV2Context>(run1);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqliteLargeSchemaV2Context>();
                var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = true });
                first = report.PlannedOperations.Select(o => $"{o.Kind}:{o.Schema}.{o.Table}.{o.Column}:{o.IndexDefinition?.Name}").ToList();
            }

            await using (var run2 = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await run2.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqliteLargeSchemaV2Context>(run2);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqliteLargeSchemaV2Context>();
                var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = true });
                second = report.PlannedOperations.Select(o => $"{o.Kind}:{o.Schema}.{o.Table}.{o.Column}:{o.IndexDefinition?.Name}").ToList();
            }

            Assert.Equal(first, second);
            Assert.True(first.Count >= 40);
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }

    [Fact]
    public async Task Apply_LargeSchema_CompletesWithExpectedOperationVolume()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setup = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setup.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqliteLargeSchemaV1Context>(setup);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqliteLargeSchemaV1Context>();
                await ctx.Database.EnsureCreatedAsync();
            }

            await using (var run = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await run.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqliteLargeSchemaV2Context>(run);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqliteLargeSchemaV2Context>();
                var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

                Assert.True(report.ExecutedOperations >= 40);
                Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.AddColumn);
                Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.CreateIndex);
            }
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }
}

