using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.SqlServer.Diagnostics;

public class SqlServerLockingAndDryRunTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task UpgradeSchema_WhenLockRowExists_Throws()
    {
        if (!ProviderTestEnvironment.HasSqlServer) return;
        var conn = ProviderTestEnvironment.SqlServerConnectionString!;

        await using var setup = TestServiceFactory.BuildSqlServerServices<SqlServerPeopleV1Context>(conn);
        await using (var scope = setup.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerPeopleV1Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();

            await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
            if (cmd.Connection!.State != System.Data.ConnectionState.Open) await cmd.Connection.OpenAsync();
            cmd.CommandText = "INSERT INTO so_locks(lock_name, owner, acquired_at_utc) VALUES ('SchemaOrchestratorUpgrade','manual',SYSUTCDATETIME())";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var run = TestServiceFactory.BuildSqlServerServices<SqlServerPeopleV2Context>(conn);
        await using (var scope = run.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerPeopleV2Context>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false }));
        }
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task DryRun_DoesNotWriteOperationRows()
    {
        if (!ProviderTestEnvironment.HasSqlServer) return;
        var conn = ProviderTestEnvironment.SqlServerConnectionString!;

        await using (var setup = TestServiceFactory.BuildSqlServerServices<SqlServerPeopleV1Context>(conn))
        await using (var scope = setup.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerPeopleV1Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();
        }

        await using var run = TestServiceFactory.BuildSqlServerServices<SqlServerPeopleV2Context>(conn);
        await using (var scope = run.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerPeopleV2Context>();
            var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = true });
            Assert.True(report.PlannedOperations.Count > 0);
            Assert.Equal(0, report.ExecutedOperations);

            await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
            if (cmd.Connection!.State != System.Data.ConnectionState.Open) await cmd.Connection.OpenAsync();
            cmd.CommandText = "SELECT COUNT(1) FROM so_upgrade_operations";
            var ops = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.Equal(0, ops);
        }
    }
}
