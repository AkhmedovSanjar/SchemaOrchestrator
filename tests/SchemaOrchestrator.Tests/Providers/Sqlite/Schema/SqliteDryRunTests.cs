using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.Sqlite.Schema;

public class SqliteDryRunTests
{
    [Fact]
    public async Task UpgradeSchema_DryRun_DoesNotApplyPhysicalChanges()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setupConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setupConnection.OpenAsync();
                await using var setupServices = TestServiceFactory.BuildSqliteServices<SqlitePeopleNameOnlyContext>(setupConnection);
                await using var scope = setupServices.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            await using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqlitePeopleWithEmailContext>(conn);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleWithEmailContext>();

                var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = true });

                Assert.True(report.PlannedOperations.Count > 0);
                Assert.Equal(0, report.ExecutedOperations);

                var columns = await SqliteSchemaAssertions.GetColumnsAsync(conn, "People");
                Assert.DoesNotContain("Email", columns, StringComparer.OrdinalIgnoreCase);
            }
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }
}
