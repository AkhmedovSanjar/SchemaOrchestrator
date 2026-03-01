using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.Sqlite.Capabilities;

public class SqliteCapabilityTests
{
    [Fact]
    public async Task UpgradeSchema_WhenForeignKeyOperationsRequired_ThrowsNotSupported()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setupConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setupConnection.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqliteFkV1Context>(setupConnection);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqliteFkV1Context>();
                await ctx.Database.EnsureCreatedAsync();
            }

            await using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqliteFkV2Context>(conn);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqliteFkV2Context>();

                var ex = await Assert.ThrowsAsync<NotSupportedException>(() => ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false }));
                Assert.Contains("foreign key", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }
}

