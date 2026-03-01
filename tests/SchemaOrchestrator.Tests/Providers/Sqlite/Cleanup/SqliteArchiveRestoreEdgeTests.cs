using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.Sqlite.Cleanup;

public class SqliteArchiveRestoreEdgeTests
{
    [Fact]
    public async Task RestoreArchivedColumn_WhenOriginalNameExists_Throws()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setup = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setup.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqlitePeopleWithEmailContext>(setup);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleWithEmailContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            long archiveId;
            await using (var changeConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await changeConn.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqlitePeopleNameOnlyContext>(changeConn);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();
                await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

                await using var query = changeConn.CreateCommand();
                query.CommandText = "SELECT id FROM so_archived_objects ORDER BY id DESC LIMIT 1";
                archiveId = Convert.ToInt64(await query.ExecuteScalarAsync());

                // recreate original column name manually to force restore conflict
                await using var add = changeConn.CreateCommand();
                add.CommandText = "ALTER TABLE \"People\" ADD COLUMN \"Email\" TEXT NULL";
                await add.ExecuteNonQueryAsync();

                await Assert.ThrowsAnyAsync<Exception>(() => ctx.RestoreArchivedObjectAsync(archiveId));
            }
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }

    [Fact]
    public async Task RestoreArchivedTable_Works_WhenTableWasArchived()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setup = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setup.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqliteLegacyTablesContext>(setup);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqliteLegacyTablesContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            long archiveId;
            await using (var runConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await runConn.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqliteNoLegacyTablesContext>(runConn);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqliteNoLegacyTablesContext>();
                await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

                await using var query = runConn.CreateCommand();
                query.CommandText = "SELECT id FROM so_archived_objects WHERE object_type='table' ORDER BY id DESC LIMIT 1";
                archiveId = Convert.ToInt64(await query.ExecuteScalarAsync());

                var restored = await ctx.RestoreArchivedObjectAsync(archiveId);
                Assert.True(restored);

                var exists = await SqliteSchemaAssertions.TableExistsAsync(runConn, "LegacyThings");
                Assert.True(exists);
            }
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }
}

