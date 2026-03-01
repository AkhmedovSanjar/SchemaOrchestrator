using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.PostgreSql.Capabilities;

public class PostgreSqlCapabilityTests
{
    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task UpgradeSchema_ForeignKeyChanges_ArePlannedAndApplied_WhenConnectionProvided()
    {
        if (!ProviderTestEnvironment.HasPostgreSql)
        {
            return;
        }

        var conn = ProviderTestEnvironment.PostgreSqlConnectionString!;

        await using (var setupServices = TestServiceFactory.BuildPostgreSqlServices<PostgreFkV1Context>(conn))
        {
            await using var scope = setupServices.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<PostgreFkV1Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();
        }

        await using var runServices = TestServiceFactory.BuildPostgreSqlServices<PostgreFkV2Context>(conn);
        await using var runScope = runServices.CreateAsyncScope();
        var runCtx = runScope.ServiceProvider.GetRequiredService<PostgreFkV2Context>();

        var report = await runCtx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });
        Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.DropForeignKey);
        Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.CreateForeignKey);
    }
}


