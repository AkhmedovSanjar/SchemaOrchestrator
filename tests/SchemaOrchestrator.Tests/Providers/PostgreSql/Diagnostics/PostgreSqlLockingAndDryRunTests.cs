using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.PostgreSql.Diagnostics;

public class PostgreSqlLockingAndDryRunTests
{
    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task UpgradeSchema_WhenLockRowExists_Throws()
    {
        if (!ProviderTestEnvironment.HasPostgreSql) return;
        var conn = ProviderTestEnvironment.PostgreSqlConnectionString!;

        await using var setup = TestServiceFactory.BuildPostgreSqlServices<PostgrePeopleV1Context>(conn);
        await using (var scope = setup.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PostgrePeopleV1Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();

            await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
            if (cmd.Connection!.State != System.Data.ConnectionState.Open) await cmd.Connection.OpenAsync();
            cmd.CommandText = "INSERT INTO so_locks(lock_name, owner, acquired_at_utc) VALUES ('SchemaOrchestratorUpgrade','manual',NOW())";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var run = TestServiceFactory.BuildPostgreSqlServices<PostgrePeopleV2Context>(conn);
        await using (var scope = run.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PostgrePeopleV2Context>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false }));
        }
    }

    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task DryRun_DoesNotWriteOperationRows()
    {
        if (!ProviderTestEnvironment.HasPostgreSql) return;
        var conn = ProviderTestEnvironment.PostgreSqlConnectionString!;

        await using (var setup = TestServiceFactory.BuildPostgreSqlServices<PostgrePeopleV1Context>(conn))
        await using (var scope = setup.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PostgrePeopleV1Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();
        }

        await using var run = TestServiceFactory.BuildPostgreSqlServices<PostgrePeopleV2Context>(conn);
        await using (var scope = run.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PostgrePeopleV2Context>();
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
