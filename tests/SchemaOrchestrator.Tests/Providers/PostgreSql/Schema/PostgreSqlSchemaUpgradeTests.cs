using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.PostgreSql.Schema;

public class PostgreSqlSchemaUpgradeTests
{
    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task UpgradeSchema_AddsColumnAndIndex_WhenConnectionProvided()
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

        await using (var upgradeServices = TestServiceFactory.BuildPostgreSqlServices<PostgrePeopleV2Context>(conn))
        {
            await using var scope = upgradeServices.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<PostgrePeopleV2Context>();
            var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

            Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.AddColumn && o.Column == "Email");
            Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.CreateIndex);

            await using var raw = ctx.Database.GetDbConnection().CreateCommand();
            if (raw.Connection!.State != System.Data.ConnectionState.Open)
            {
                await raw.Connection.OpenAsync();
            }

            raw.CommandText = "SELECT COUNT(1) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'people' AND column_name = 'Email'";
            var count = Convert.ToInt32(await raw.ExecuteScalarAsync());
            Assert.Equal(1, count);
        }
    }
}


