using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.Sqlite.Schema;

public class SqliteAlterRebuildTests
{
    [Fact]
    public async Task UpgradeSchema_AlterColumn_RebuildsTableAndKeepsData()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setupConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setupConnection.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqliteCodeAsTextContext>(setupConnection);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqliteCodeAsTextContext>();
                await ctx.Database.EnsureCreatedAsync();

                await using var insert = setupConnection.CreateCommand();
                insert.CommandText = "INSERT INTO CodeItems (Id, Code) VALUES (1, '123')";
                await insert.ExecuteNonQueryAsync();
            }

            await using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                await using var services = TestServiceFactory.BuildSqliteServices<SqliteCodeAsIntContext>(conn);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqliteCodeAsIntContext>();

                var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });
                Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.AlterColumn && o.Column == "Code");

                await using var countCmd = conn.CreateCommand();
                countCmd.CommandText = "SELECT COUNT(1) FROM CodeItems WHERE Id = 1";
                var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                Assert.Equal(1, count);
            }
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }
}
