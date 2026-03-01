using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.PostgreSql.Cleanup;

public class PostgreSqlCleanupTests
{
    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task CleanupDryRun_AndRestoreArchivedColumn_Works_WhenConnectionProvided()
    {
        if (!ProviderTestEnvironment.HasPostgreSql)
        {
            return;
        }

        var conn = ProviderTestEnvironment.PostgreSqlConnectionString!;

        await using (var setupServices = TestServiceFactory.BuildPostgreSqlServices<PostgrePeopleV2Context>(conn))
        {
            await using var scope = setupServices.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<PostgrePeopleV2Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();
        }

        long archiveId;
        await using (var runServices = TestServiceFactory.BuildPostgreSqlServices<PostgrePeopleV1Context>(conn))
        {
            await using var scope = runServices.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<PostgrePeopleV1Context>();
            await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

            await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
            if (cmd.Connection!.State != System.Data.ConnectionState.Open)
            {
                await cmd.Connection.OpenAsync();
            }

            cmd.CommandText = "SELECT id FROM so_archived_objects ORDER BY id DESC LIMIT 1";
            archiveId = Convert.ToInt64(await cmd.ExecuteScalarAsync());

            var dryRunCount = await ctx.CleanupArchivedObjectsAsync(dryRun: true);
            Assert.True(dryRunCount >= 1);

            var restored = await ctx.RestoreArchivedObjectAsync(archiveId);
            Assert.True(restored);
        }
    }

    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task CleanupPhysicalRun_RemovesArchivedMetadata_WhenConnectionProvided()
    {
        if (!ProviderTestEnvironment.HasPostgreSql)
        {
            return;
        }

        var conn = ProviderTestEnvironment.PostgreSqlConnectionString!;

        await using (var setupServices = TestServiceFactory.BuildPostgreSqlServices<PostgrePeopleV2Context>(conn))
        {
            await using var scope = setupServices.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<PostgrePeopleV2Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();
        }

        await using var runServices = TestServiceFactory.BuildPostgreSqlServices<PostgrePeopleV1Context>(conn);
        await using (var scope = runServices.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PostgrePeopleV1Context>();
            await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });
            var removed = await ctx.CleanupArchivedObjectsAsync(dryRun: false);
            Assert.True(removed >= 1);

            await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
            if (cmd.Connection!.State != System.Data.ConnectionState.Open)
            {
                await cmd.Connection.OpenAsync();
            }

            cmd.CommandText = "SELECT COUNT(1) FROM so_archived_objects";
            var remaining = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.Equal(0, remaining);
        }
    }
}
