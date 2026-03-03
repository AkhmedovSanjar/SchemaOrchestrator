using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SchemaOrchestrator.Samples.SqlServer;
using SchemaOrchestrator.Samples.SqlServer.Contexts;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Samples.SqlServer;

public class SqlServerSampleScenarioTests
{
    [Fact]
    [Trait("Category", "Samples")]
    [Trait("Category", "SqlServer")]
    public async Task AllSampleScenarios_RunSuccessfully_WhenConnectionProvided()
    {
        if (!ProviderTestEnvironment.HasSqlServer)
        {
            return;
        }

        var connectionString = BuildIsolatedDatabaseConnectionString(ProviderTestEnvironment.SqlServerConnectionString!);
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
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = $"SchemaOrchestratorSample_{Guid.NewGuid():N}"
        };

        return builder.ConnectionString;
    }

    private static async Task EnsureDeletedAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SampleSqlServerV1Context>()
            .UseSqlServer(connectionString)
            .Options;

        await using var ctx = new SampleSqlServerV1Context(options);
        await ctx.Database.EnsureDeletedAsync();
    }
}
