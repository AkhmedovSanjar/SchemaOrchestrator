using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.SqlServer.Cleanup;

public class SqlServerArchiveRestoreEdgeTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task RestoreArchivedColumn_WhenOriginalNameExists_Throws()
    {
        if (!ProviderTestEnvironment.HasSqlServer) return;
        var conn = ProviderTestEnvironment.SqlServerConnectionString!;

        await using (var setup = TestServiceFactory.BuildSqlServerServices<SqlServerPeopleV2Context>(conn))
        await using (var scope = setup.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerPeopleV2Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();
        }

        await using var run = TestServiceFactory.BuildSqlServerServices<SqlServerPeopleV1Context>(conn);
        await using (var scope = run.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerPeopleV1Context>();
            await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

            long id;
            await using (var get = ctx.Database.GetDbConnection().CreateCommand())
            {
                if (get.Connection!.State != System.Data.ConnectionState.Open) await get.Connection.OpenAsync();
                get.CommandText = "SELECT TOP(1) id FROM so_archived_objects ORDER BY id DESC";
                id = Convert.ToInt64(await get.ExecuteScalarAsync());
            }

            await using (var add = ctx.Database.GetDbConnection().CreateCommand())
            {
                add.CommandText = "ALTER TABLE [dbo].[People] ADD [Email] nvarchar(100) NULL";
                await add.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsAnyAsync<Exception>(() => ctx.RestoreArchivedObjectAsync(id));
        }
    }
}
