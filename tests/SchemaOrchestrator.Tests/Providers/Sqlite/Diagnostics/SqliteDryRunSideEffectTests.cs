using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.Sqlite.Diagnostics;

public class SqliteDryRunSideEffectTests
{
    [Fact]
    public async Task DryRun_DoesNotWriteArchivedObjects_OrOperationRows()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setup = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setup.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqlitePeopleNameOnlyContext>(setup);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            await using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            await using var provider = TestServiceFactory.BuildSqliteServices<SqlitePeopleWithEmailContext>(conn);
            await using var runScope = provider.CreateAsyncScope();
            var runCtx = runScope.ServiceProvider.GetRequiredService<SqlitePeopleWithEmailContext>();

            var report = await runCtx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = true });
            Assert.True(report.PlannedOperations.Count > 0);
            Assert.Equal(0, report.ExecutedOperations);

            await using var archivedCmd = conn.CreateCommand();
            archivedCmd.CommandText = "SELECT COUNT(1) FROM so_archived_objects";
            var archived = Convert.ToInt32(await archivedCmd.ExecuteScalarAsync());
            Assert.Equal(0, archived);

            await using var opCmd = conn.CreateCommand();
            opCmd.CommandText = "SELECT COUNT(1) FROM so_upgrade_operations";
            var ops = Convert.ToInt32(await opCmd.ExecuteScalarAsync());
            Assert.Equal(0, ops);
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }
}

