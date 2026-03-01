using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.Sqlite.Cleanup;

public class SqliteCleanupTests
{
    [Fact]
    public async Task CleanupDryRun_AndRestoreArchivedColumn_Works()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setupConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setupConnection.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqlitePeopleWithEmailContext>(setupConnection);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleWithEmailContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            long archiveId;
            await using (var archiveConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await archiveConnection.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqlitePeopleNameOnlyContext>(archiveConnection);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();
                await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

                await using var cmd = archiveConnection.CreateCommand();
                cmd.CommandText = "SELECT id FROM so_archived_objects ORDER BY id DESC LIMIT 1";
                var value = await cmd.ExecuteScalarAsync();
                archiveId = Convert.ToInt64(value);

                var dryRunCount = await ctx.CleanupArchivedObjectsAsync(dryRun: true);
                Assert.True(dryRunCount >= 1);
            }

            await using (var restoreConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await restoreConnection.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqlitePeopleNameOnlyContext>(restoreConnection);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();

                var restored = await ctx.RestoreArchivedObjectAsync(archiveId);
                Assert.True(restored);
            }
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }

    [Fact]
    public async Task CleanupArchivedObjects_PhysicalRun_DeletesArchivedColumnAndMetadata()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setupConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setupConnection.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqlitePeopleWithEmailContext>(setupConnection);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleWithEmailContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            await using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqlitePeopleNameOnlyContext>(conn);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();
                await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

                var removed = await ctx.CleanupArchivedObjectsAsync(dryRun: false);
                Assert.True(removed >= 1);

                var columns = await SqliteSchemaAssertions.GetColumnsAsync(conn, "People");
                Assert.DoesNotContain(columns, c => c.StartsWith("Deleted_Email_", StringComparison.OrdinalIgnoreCase));

                await using var countCmd = conn.CreateCommand();
                countCmd.CommandText = "SELECT COUNT(1) FROM so_archived_objects";
                var remaining = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                Assert.Equal(0, remaining);
            }
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }
}

