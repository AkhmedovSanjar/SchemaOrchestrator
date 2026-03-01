using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.SqlServer.Cleanup;

public class SqlServerCleanupTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task CleanupDryRun_AndRestoreArchivedColumn_Works_WhenConnectionProvided()
    {
        if (!ProviderTestEnvironment.HasSqlServer)
        {
            return;
        }

        var conn = ProviderTestEnvironment.SqlServerConnectionString!;

        await using (var setupServices = TestServiceFactory.BuildSqlServerServices<SqlServerPeopleV2Context>(conn))
        {
            await using var scope = setupServices.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerPeopleV2Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();
        }

        long archiveId;
        await using (var archiveServices = TestServiceFactory.BuildSqlServerServices<SqlServerPeopleV1Context>(conn))
        {
            await using var scope = archiveServices.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerPeopleV1Context>();
            await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

            await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
            if (cmd.Connection!.State != System.Data.ConnectionState.Open)
            {
                await cmd.Connection.OpenAsync();
            }

            cmd.CommandText = "SELECT TOP(1) id FROM so_archived_objects ORDER BY id DESC";
            archiveId = Convert.ToInt64(await cmd.ExecuteScalarAsync());

            var dryRunCount = await ctx.CleanupArchivedObjectsAsync(dryRun: true);
            Assert.True(dryRunCount >= 1);

            var restored = await ctx.RestoreArchivedObjectAsync(archiveId);
            Assert.True(restored);
        }
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task CleanupPhysicalRun_RemovesArchivedMetadata_WhenConnectionProvided()
    {
        if (!ProviderTestEnvironment.HasSqlServer)
        {
            return;
        }

        var conn = ProviderTestEnvironment.SqlServerConnectionString!;

        await using (var setupServices = TestServiceFactory.BuildSqlServerServices<SqlServerPeopleV2Context>(conn))
        {
            await using var scope = setupServices.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerPeopleV2Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();
        }

        await using var runServices = TestServiceFactory.BuildSqlServerServices<SqlServerPeopleV1Context>(conn);
        await using (var scope = runServices.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerPeopleV1Context>();
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

