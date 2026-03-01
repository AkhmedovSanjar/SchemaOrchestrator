using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.SqlServer.Schema;

public class SqlServerLargeAndConstraintTests
{
    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task DryRun_LargeSchema_IsDeterministic()
    {
        if (!ProviderTestEnvironment.HasSqlServer) return;
        var conn = ProviderTestEnvironment.SqlServerConnectionString!;

        await using (var setup = TestServiceFactory.BuildSqlServerServices<SqlServerLargeSchemaV1Context>(conn))
        await using (var scope = setup.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerLargeSchemaV1Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();
        }

        List<string> first;
        List<string> second;

        await using (var run1 = TestServiceFactory.BuildSqlServerServices<SqlServerLargeSchemaV2Context>(conn))
        await using (var scope = run1.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerLargeSchemaV2Context>();
            var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = true });
            first = report.PlannedOperations.Select(o => $"{o.Kind}:{o.Table}:{o.Column}:{o.IndexDefinition?.Name}").ToList();
        }

        await using (var run2 = TestServiceFactory.BuildSqlServerServices<SqlServerLargeSchemaV2Context>(conn))
        await using (var scope = run2.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerLargeSchemaV2Context>();
            var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = true });
            second = report.PlannedOperations.Select(o => $"{o.Kind}:{o.Table}:{o.Column}:{o.IndexDefinition?.Name}").ToList();
        }

        Assert.Equal(first, second);
        Assert.True(first.Count >= 24);
    }

    [Fact]
    [Trait("Category", "SqlServer")]
    public async Task ConstraintEvolution_CompletesAndIndexesEvolve()
    {
        if (!ProviderTestEnvironment.HasSqlServer) return;
        var conn = ProviderTestEnvironment.SqlServerConnectionString!;

        await using (var setup = TestServiceFactory.BuildSqlServerServices<SqlServerConstraintV1Context>(conn))
        await using (var scope = setup.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerConstraintV1Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();
        }

        await using var run = TestServiceFactory.BuildSqlServerServices<SqlServerConstraintV2Context>(conn);
        await using (var scope = run.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<SqlServerConstraintV2Context>();
            var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });
            Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.DropIndex || o.Kind == SchemaOperationKind.CreateIndex || o.Kind == SchemaOperationKind.AlterColumn);
        }
    }
}
