using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchemaOrchestrator.Samples.PostgreSql;
using SchemaOrchestrator.Samples.PostgreSql.Contexts;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Samples.PostgreSql;

public class PostgreSqlSampleScenarioTests
{
    [Fact]
    [Trait("Category", "Samples")]
    [Trait("Category", "PostgreSql")]
    public async Task AllSampleScenarios_RunSuccessfully_WhenConnectionProvided()
    {
        if (!ProviderTestEnvironment.HasPostgreSql)
        {
            return;
        }

        var connectionString = BuildIsolatedDatabaseConnectionString(ProviderTestEnvironment.PostgreSqlConnectionString!);
        try
        {
            Assert.Equal(0, await SampleRunner.RunAsync(["init-v1", connectionString]));
            Assert.Equal(0, await SampleRunner.RunAsync(["upgrade-v2", connectionString]));
            Assert.Equal(0, await SampleRunner.RunAsync(["dry-run", connectionString]));
            Assert.Equal(0, await SampleRunner.RunAsync(["cleanup", connectionString]));
            Assert.Equal(0, await SampleRunner.RunAsync(["restore-latest-archive", connectionString]));
        }
        finally
        {
            await EnsureDeletedAsync(connectionString);
        }
    }

    private static string BuildIsolatedDatabaseConnectionString(string baseConnectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = $"schema_orchestrator_sample_{Guid.NewGuid():N}"
        };

        return builder.ConnectionString;
    }

    private static async Task EnsureDeletedAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SamplePostgreV1Context>()
            .UseNpgsql(connectionString)
            .Options;

        await using var ctx = new SamplePostgreV1Context(options);
        await ctx.Database.EnsureDeletedAsync();
    }
}
