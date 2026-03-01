using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.SqlServer.Diagnostics;

public class SqlServerDiagnosticsTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task UpgradeSchema_WritesHistoryAndOperationCheckpoints_WhenConnectionProvided()
    {
        if (!ProviderTestEnvironment.HasSqlServer)
        {
            return;
        }

        var conn = ProviderTestEnvironment.SqlServerConnectionString!;

        await using (var setupServices = TestServiceFactory.BuildSqlServerServices<SqlServerPeopleV1Context>(conn))
        {
            await using var scope = setupServices.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerPeopleV1Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();
        }

        await using var runServices = TestServiceFactory.BuildSqlServerServices<SqlServerPeopleV2Context>(conn);
        await using var runScope = runServices.CreateAsyncScope();
        var runCtx = runScope.ServiceProvider.GetRequiredService<SqlServerPeopleV2Context>();

        var report = await runCtx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });
        Assert.True(report.ExecutedOperations > 0);

        await using var cmd = runCtx.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open)
        {
            await cmd.Connection.OpenAsync();
        }

        cmd.CommandText = "SELECT TOP(1) status, operations_applied FROM so_upgrade_history ORDER BY id DESC";
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


