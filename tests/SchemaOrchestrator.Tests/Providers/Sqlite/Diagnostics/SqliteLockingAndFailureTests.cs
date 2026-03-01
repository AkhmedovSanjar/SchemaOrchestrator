using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.Sqlite.Diagnostics;

public class SqliteLockingAndFailureTests
{
    [Fact]
    public async Task UpgradeSchema_WhenLockAlreadyHeld_Throws()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setupConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setupConnection.OpenAsync();
                await using var provider = TestServiceFactory.BuildSqliteServices<SqlitePeopleNameOnlyContext>(setupConnection);
                await using var scope = provider.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();
                await ctx.Database.EnsureCreatedAsync();
                await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = true });

                await using var lockCmd = setupConnection.CreateCommand();
                lockCmd.CommandText = "INSERT INTO so_locks(lock_name, owner, acquired_at_utc) VALUES ('SchemaOrchestratorUpgrade', 'manual-owner', CURRENT_TIMESTAMP)";
                await lockCmd.ExecuteNonQueryAsync();
            }

            await using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            await using var services = TestServiceFactory.BuildSqliteServices<SqlitePeopleWithEmailContext>(conn);
            await using var runScope = services.CreateAsyncScope();
            var runCtx = runScope.ServiceProvider.GetRequiredService<SqlitePeopleWithEmailContext>();

            await Assert.ThrowsAsync<InvalidOperationException>(() => runCtx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false }));
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }

    [Fact]
    public async Task UpgradeSchema_WhenStaleLockExists_CleansAndContinues()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setupConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setupConnection.OpenAsync();
                await using var provider = TestServiceFactory.BuildSqliteServices<SqlitePeopleNameOnlyContext>(
                    setupConnection,
                    options => options.LockTimeout = TimeSpan.FromSeconds(1));
                await using var scope = provider.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();
                await ctx.Database.EnsureCreatedAsync();
                await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = true });

                await using var lockCmd = setupConnection.CreateCommand();
                lockCmd.CommandText = "INSERT INTO so_locks(lock_name, owner, acquired_at_utc) VALUES ('SchemaOrchestratorUpgrade', 'stale-owner', datetime('now', '-10 minutes'))";
                await lockCmd.ExecuteNonQueryAsync();
            }

            await using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            await using var services = TestServiceFactory.BuildSqliteServices<SqlitePeopleWithEmailContext>(
                conn,
                options => options.LockTimeout = TimeSpan.FromSeconds(1));
            await using var runScope = services.CreateAsyncScope();
            var runCtx = runScope.ServiceProvider.GetRequiredService<SqlitePeopleWithEmailContext>();

            var report = await runCtx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });
            Assert.True(report.ExecutedOperations > 0);
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }

    [Fact]
    public async Task UpgradeSchema_WhenFirstRunFails_SecondRunCanSucceed()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setupConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setupConnection.OpenAsync();
                await using var provider = TestServiceFactory.BuildSqliteServices<SqlitePeopleNameOnlyContext>(setupConnection);
                await using var scope = provider.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            await using (var failConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await failConn.OpenAsync();
                await using var failServices = TestServiceFactory.BuildSqliteServices<SqliteFkV2Context>(failConn);
                await using var failScope = failServices.CreateAsyncScope();
                var failCtx = failScope.ServiceProvider.GetRequiredService<SqliteFkV2Context>();
                await Assert.ThrowsAsync<NotSupportedException>(() => failCtx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false }));

                await using var historyCmd = failConn.CreateCommand();
                historyCmd.CommandText = "SELECT status FROM so_upgrade_history ORDER BY id DESC LIMIT 1";
                var status = Convert.ToString(await historyCmd.ExecuteScalarAsync());
                Assert.Equal("failed", status);
            }

            await using (var succeedConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await succeedConn.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqlitePeopleNameOnlyContext>(succeedConn);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();

                var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });
                Assert.True(report.ExecutedOperations >= 0);

                await using var cmd = succeedConn.CreateCommand();
                cmd.CommandText = "SELECT status FROM so_upgrade_history ORDER BY id DESC LIMIT 1";
                var status = Convert.ToString(await cmd.ExecuteScalarAsync());
                Assert.Equal("completed", status);
            }
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }
}

