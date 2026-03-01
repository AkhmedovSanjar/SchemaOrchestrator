using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.PostgreSql.Cleanup;

public class PostgreSqlArchiveRestoreEdgeTests
{
    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task RestoreArchivedColumn_WhenOriginalNameExists_Throws()
    {
        if (!ProviderTestEnvironment.HasPostgreSql) return;
        var conn = ProviderTestEnvironment.PostgreSqlConnectionString!;

        await using (var setup = TestServiceFactory.BuildPostgreSqlServices<PostgrePeopleV2Context>(conn))
        await using (var scope = setup.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PostgrePeopleV2Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();
        }

        await using var run = TestServiceFactory.BuildPostgreSqlServices<PostgrePeopleV1Context>(conn);
        await using (var scope = run.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PostgrePeopleV1Context>();
            await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

            long id;
            await using (var get = ctx.Database.GetDbConnection().CreateCommand())
            {
                if (get.Connection!.State != System.Data.ConnectionState.Open) await get.Connection.OpenAsync();
                get.CommandText = "SELECT id FROM so_archived_objects ORDER BY id DESC LIMIT 1";
                id = Convert.ToInt64(await get.ExecuteScalarAsync());
            }

            await using (var add = ctx.Database.GetDbConnection().CreateCommand())
            {
                add.CommandText = "ALTER TABLE \"public\".\"people\" ADD COLUMN \"Email\" text NULL";
                await add.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsAnyAsync<Exception>(() => ctx.RestoreArchivedObjectAsync(id));
        }
    }
}
