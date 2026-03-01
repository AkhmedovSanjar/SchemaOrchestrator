using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.Sqlite.Schema;

public class SqliteArchiveTests
{
    [Fact]
    public async Task UpgradeSchema_RemovedColumn_IsArchivedWithDeletedPrefix()
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

                var columns = await SqliteSchemaAssertions.GetColumnsAsync(conn, "People");
                Assert.Contains(columns, c => c.StartsWith("Deleted_Email_", StringComparison.OrdinalIgnoreCase));

                var archivedCount = await SqliteSchemaAssertions.CountByPrefixAsync(conn, "so_archived_objects", "archived_name", "Deleted_Email_");
                Assert.True(archivedCount >= 1);
            }
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }

    [Fact]
    public async Task UpgradeSchema_RemovedTable_IsArchivedWithDeletedPrefix()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setupConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setupConnection.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqliteLegacyTablesContext>(setupConnection);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqliteLegacyTablesContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            await using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqliteNoLegacyTablesContext>(conn);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqliteNoLegacyTablesContext>();
                await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'Deleted_LegacyThings_%' LIMIT 1";
                var value = await cmd.ExecuteScalarAsync();
                Assert.NotNull(value);

                var archivedCount = await SqliteSchemaAssertions.CountByPrefixAsync(conn, "so_archived_objects", "archived_name", "Deleted_LegacyThings_");
                Assert.True(archivedCount >= 1);
            }
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }
}
