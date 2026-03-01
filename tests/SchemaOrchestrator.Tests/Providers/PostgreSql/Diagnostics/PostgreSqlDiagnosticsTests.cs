using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.PostgreSql.Diagnostics;

public class PostgreSqlDiagnosticsTests
{
    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task UpgradeSchema_WritesHistoryAndOperationCheckpoints_WhenConnectionProvided()
    {
        if (!ProviderTestEnvironment.HasPostgreSql)
        {
            return;
        }

        var conn = ProviderTestEnvironment.PostgreSqlConnectionString!;

        await using (var setupServices = TestServiceFactory.BuildPostgreSqlServices<PostgrePeopleV1Context>(conn))
        {
            await using var scope = setupServices.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<PostgrePeopleV1Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();
        }

        await using var runServices = TestServiceFactory.BuildPostgreSqlServices<PostgrePeopleV2Context>(conn);
        await using var runScope = runServices.CreateAsyncScope();
        var runCtx = runScope.ServiceProvider.GetRequiredService<PostgrePeopleV2Context>();

        var report = await runCtx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });
        Assert.True(report.ExecutedOperations > 0);

        await using var cmd = runCtx.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open)
        {
            await cmd.Connection.OpenAsync();
        }

        cmd.CommandText = "SELECT status, operations_applied FROM so_upgrade_history ORDER BY id DESC LIMIT 1";
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("completed", reader.GetString(0));
        Assert.Equal(report.ExecutedOperations, reader.GetInt32(1));

        await reader.DisposeAsync();
        cmd.CommandText = "SELECT COUNT(1) FROM so_upgrade_operations WHERE status = 'completed'";
        var opCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.True(opCount >= report.ExecutedOperations);
    }
}
