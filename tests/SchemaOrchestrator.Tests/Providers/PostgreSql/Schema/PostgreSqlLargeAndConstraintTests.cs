using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.PostgreSql.Schema;

public class PostgreSqlLargeAndConstraintTests
{
    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task DryRun_LargeSchema_IsDeterministic()
    {
        if (!ProviderTestEnvironment.HasPostgreSql) return;
        var conn = ProviderTestEnvironment.PostgreSqlConnectionString!;

        await using (var setup = TestServiceFactory.BuildPostgreSqlServices<PostgreLargeSchemaV1Context>(conn))
        await using (var scope = setup.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PostgreLargeSchemaV1Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();
        }

        List<string> first;
        List<string> second;

        await using (var run1 = TestServiceFactory.BuildPostgreSqlServices<PostgreLargeSchemaV2Context>(conn))
        await using (var scope = run1.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PostgreLargeSchemaV2Context>();
            var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = true });
            first = report.PlannedOperations.Select(o => $"{o.Kind}:{o.Table}:{o.Column}:{o.IndexDefinition?.Name}").ToList();
        }

        await using (var run2 = TestServiceFactory.BuildPostgreSqlServices<PostgreLargeSchemaV2Context>(conn))
        await using (var scope = run2.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PostgreLargeSchemaV2Context>();
            var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = true });
            second = report.PlannedOperations.Select(o => $"{o.Kind}:{o.Table}:{o.Column}:{o.IndexDefinition?.Name}").ToList();
        }

        Assert.Equal(first, second);
        Assert.True(first.Count >= 24);
    }

    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task ConstraintEvolution_CompletesAndIndexesEvolve()
    {
        if (!ProviderTestEnvironment.HasPostgreSql) return;
        var conn = ProviderTestEnvironment.PostgreSqlConnectionString!;

        await using (var setup = TestServiceFactory.BuildPostgreSqlServices<PostgreConstraintV1Context>(conn))
        await using (var scope = setup.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PostgreConstraintV1Context>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();
        }

        await using var run = TestServiceFactory.BuildPostgreSqlServices<PostgreConstraintV2Context>(conn);
        await using (var scope = run.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PostgreConstraintV2Context>();
            var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });
            Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.DropIndex || o.Kind == SchemaOperationKind.CreateIndex || o.Kind == SchemaOperationKind.AlterColumn);
        }
    }
}
