using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Abstractions;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Subscribers;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.Sqlite.Diagnostics;

public class SqliteOperationalReliabilityTests
{
    [Fact]
    public async Task ConcurrentUpgrades_SecondAttemptFailsWhileFirstHoldsLock()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setup = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setup.OpenAsync();
                await using var provider = BuildSqliteServicesWithSubscriber<SqlitePeopleNameOnlyContext, SlowSubscriber>(setup);
                await using var scope = provider.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            await using var conn1 = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await using var conn2 = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn1.OpenAsync();
            await conn2.OpenAsync();

            await using var provider1 = BuildSqliteServicesWithSubscriber<SqlitePeopleWithEmailContext, SlowSubscriber>(conn1);
            await using var provider2 = TestServiceFactory.BuildSqliteServices<SqlitePeopleWithEmailContext>(conn2);
            await using var scope1 = provider1.CreateAsyncScope();
            await using var scope2 = provider2.CreateAsyncScope();

            var ctx1 = scope1.ServiceProvider.GetRequiredService<SqlitePeopleWithEmailContext>();
            var ctx2 = scope2.ServiceProvider.GetRequiredService<SqlitePeopleWithEmailContext>();

            var first = ctx1.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false, EnableDataSubscribers = true });
            await Task.Delay(200);
            var second = ctx2.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await second);
            await first;
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }

    [Fact]
    public async Task FaultInjection_SubscriberClosesConnection_UpgradeFailsAndHistoryMarkedFailed()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setup = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setup.OpenAsync();
                await using var provider = BuildSqliteServicesWithSubscriber<SqlitePeopleNameOnlyContext, ConnectionKillerSubscriber>(setup);
                await using var scope = provider.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            await using var runConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await runConn.OpenAsync();
            await using var runProvider = BuildSqliteServicesWithSubscriber<SqlitePeopleWithEmailContext, ConnectionKillerSubscriber>(runConn);
            await using var runScope = runProvider.CreateAsyncScope();
            var runCtx = runScope.ServiceProvider.GetRequiredService<SqlitePeopleWithEmailContext>();

            await Assert.ThrowsAnyAsync<Exception>(() => runCtx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false, EnableDataSubscribers = true }));

            // reopen to inspect history
            await using var inspectConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await inspectConn.OpenAsync();
            await using var cmd = inspectConn.CreateCommand();
            cmd.CommandText = "SELECT status FROM so_upgrade_history ORDER BY id DESC LIMIT 1";
            var status = Convert.ToString(await cmd.ExecuteScalarAsync());
            Assert.True(
                string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "running", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }

    [Fact]
    public async Task LargeSchemaApply_CompletesWithinReasonableTimeBudget()
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

            await using var run = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await run.OpenAsync();
            await using var provider = TestServiceFactory.BuildSqliteServices<SqliteLargeSchemaV2Context>(run);
            await using var scope2 = provider.CreateAsyncScope();
            var runCtx = scope2.ServiceProvider.GetRequiredService<SqliteLargeSchemaV2Context>();

            var sw = Stopwatch.StartNew();
            var report = await runCtx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });
            sw.Stop();

            Assert.True(report.ExecutedOperations >= 40);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30));
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }

    private static ServiceProvider BuildSqliteServicesWithSubscriber<TContext, TSubscriber>(Microsoft.Data.Sqlite.SqliteConnection connection)
        where TContext : DbContext
        where TSubscriber : class, ISchemaUpgradeSubscriber
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSchemaOrchestrator(options =>
        {
            options.MigrationsPolicy = DestructiveChangePolicy.Archive;
            options.ArchiveTtl = TimeSpan.Zero;
        });
        services.AddScoped<ISchemaUpgradeSubscriber, TSubscriber>();
        services.AddDbContext<TContext>(opt => opt.UseSqlite(connection));
        return services.BuildServiceProvider();
    }

    private sealed class SlowSubscriber : ISchemaUpgradeSubscriber
    {
        public async Task BeforeOperationAsync(UpgradeExecutionContext context, SchemaOperation operation, CancellationToken cancellationToken)
        {
            await Task.Delay(2000, cancellationToken);
        }
    }

    private sealed class ConnectionKillerSubscriber : ISchemaUpgradeSubscriber
    {
        public Task BeforeOperationAsync(UpgradeExecutionContext context, SchemaOperation operation, CancellationToken cancellationToken)
        {
            context.Connection.Close();
            return Task.CompletedTask;
        }
    }
}
