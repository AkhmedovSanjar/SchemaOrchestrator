using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.SqlServer.Schema;

public class SqlServerSchemaUpgradeTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task UpgradeSchema_AddsColumnAndIndex_WhenConnectionProvided()
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

        await using (var upgradeServices = TestServiceFactory.BuildSqlServerServices<SqlServerPeopleV2Context>(conn))
        {
            await using var scope = upgradeServices.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerPeopleV2Context>();
            var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

            Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.AddColumn && o.Column == "Email");
            Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.CreateIndex);

            await using var raw = ctx.Database.GetDbConnection().CreateCommand();
            if (raw.Connection!.State != System.Data.ConnectionState.Open)
            {
                await raw.Connection.OpenAsync();
            }

            raw.CommandText = "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'People' AND COLUMN_NAME = 'Email'";
            var count = Convert.ToInt32(await raw.ExecuteScalarAsync());
            Assert.Equal(1, count);
        }
    }
}

