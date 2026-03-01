using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.Sqlite.Diagnostics;

public class SqliteDiagnosticsTests
{
    [Fact]
    public async Task UpgradeSchema_WritesHistoryAndOperationCheckpoints()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setupConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setupConnection.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqlitePeopleNameOnlyContext>(setupConnection);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            await using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqlitePeopleWithEmailContext>(conn);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleWithEmailContext>();

                var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });
                Assert.True(report.ExecutedOperations > 0);

                await using var historyCmd = conn.CreateCommand();
                historyCmd.CommandText = "SELECT status, operations_applied FROM so_upgrade_history ORDER BY id DESC LIMIT 1";
                await using var historyReader = await historyCmd.ExecuteReaderAsync();
                Assert.True(await historyReader.ReadAsync());
                Assert.Equal("completed", historyReader.GetString(0));
                var applied = historyReader.GetInt32(1);
                Assert.Equal(report.ExecutedOperations, applied);

                await using var opCountCmd = conn.CreateCommand();
                opCountCmd.CommandText = "SELECT COUNT(1) FROM so_upgrade_operations WHERE status = 'completed'";
                var completedOps = Convert.ToInt32(await opCountCmd.ExecuteScalarAsync());
                Assert.True(completedOps >= report.ExecutedOperations);
            }
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }
}
