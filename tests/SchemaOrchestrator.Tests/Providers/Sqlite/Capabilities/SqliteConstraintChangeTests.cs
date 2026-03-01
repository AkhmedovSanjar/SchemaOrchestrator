using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.Sqlite.Capabilities;

public class SqliteConstraintChangeTests
{
    [Fact]
    public async Task ConstraintChanges_ExecuteWithoutEngineCrash()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setup = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setup.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqliteConstraintV1Context>(setup);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqliteConstraintV1Context>();
                await ctx.Database.EnsureCreatedAsync();
            }

            await using (var run = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await run.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqliteConstraintV2Context>(run);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqliteConstraintV2Context>();

                var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });
                Assert.NotNull(report);
            }
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }
}

