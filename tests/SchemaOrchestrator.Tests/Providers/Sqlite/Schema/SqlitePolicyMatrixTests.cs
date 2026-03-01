using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.Sqlite.Schema;

public class SqlitePolicyMatrixTests
{
    [Fact]
    public async Task BlockPolicy_DoesNotArchiveRemovedTable()
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

            await using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            await using var provider = TestServiceFactory.BuildSqliteServices<SqliteNoLegacyTablesContext>(
                conn,
                options => options.MigrationsPolicy = DestructiveChangePolicy.Block);
            await using var runScope = provider.CreateAsyncScope();
            var ctx2 = runScope.ServiceProvider.GetRequiredService<SqliteNoLegacyTablesContext>();
            var report = await ctx2.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

            Assert.DoesNotContain(report.PlannedOperations, o => o.Kind == SchemaOperationKind.ArchiveTable);

            var existsLegacy = await SqliteSchemaAssertions.TableExistsAsync(conn, "LegacyThings");
            Assert.True(existsLegacy);
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }

    [Theory]
    [InlineData(DestructiveChangePolicy.Archive)]
    [InlineData(DestructiveChangePolicy.Allow)]
    public async Task ArchiveOrAllowPolicy_ArchivesRemovedTable(DestructiveChangePolicy policy)
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

            await using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            await using var provider = TestServiceFactory.BuildSqliteServices<SqliteNoLegacyTablesContext>(
                conn,
                options => options.MigrationsPolicy = policy);
            await using var runScope = provider.CreateAsyncScope();
            var ctx2 = runScope.ServiceProvider.GetRequiredService<SqliteNoLegacyTablesContext>();
            var report = await ctx2.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

            Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.ArchiveTable);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name LIKE 'Deleted_LegacyThings_%'";
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.True(count >= 1);
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }
}

