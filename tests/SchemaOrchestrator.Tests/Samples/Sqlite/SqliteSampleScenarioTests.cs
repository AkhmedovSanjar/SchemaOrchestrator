using SchemaOrchestrator.Samples.Sqlite;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Samples.Sqlite;

public class SqliteSampleScenarioTests
{
    [Fact]
    [Trait("Category", "Samples")]
    [Trait("Category", "Sqlite")]
    public async Task AllSampleScenarios_RunSuccessfully()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            Assert.Equal(0, await SampleRunner.RunAsync(["init-v1", dbPath]));
            Assert.Equal(0, await SampleRunner.RunAsync(["upgrade-v2", dbPath]));
            Assert.Equal(0, await SampleRunner.RunAsync(["dry-run", dbPath]));
            Assert.Equal(0, await SampleRunner.RunAsync(["cleanup", dbPath]));
            Assert.Equal(0, await SampleRunner.RunAsync(["restore-latest-archive", dbPath]));
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }
}
