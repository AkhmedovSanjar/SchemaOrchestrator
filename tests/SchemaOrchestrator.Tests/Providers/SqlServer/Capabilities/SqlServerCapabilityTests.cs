using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.SqlServer.Capabilities;

public class SqlServerCapabilityTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task UpgradeSchema_ForeignKeyChanges_ArePlannedAndApplied_WhenConnectionProvided()
    {
        if (!ProviderTestEnvironment.HasSqlServer)
        {
            return;
        }

        var conn = ProviderTestEnvironment.SqlServerConnectionString!;

        await using (var setupServices = TestServiceFactory.BuildSqlServerServices<SqlServerFkV1Context>(conn))
        {
            await using var scope = setupServices.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerFkV1Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();
        }

        await using var runServices = TestServiceFactory.BuildSqlServerServices<SqlServerFkV2Context>(conn);
        await using var runScope = runServices.CreateAsyncScope();
        var runCtx = runScope.ServiceProvider.GetRequiredService<SqlServerFkV2Context>();

        var report = await runCtx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });
        Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.DropForeignKey);
        Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.CreateForeignKey);
    }
}
