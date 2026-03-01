using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Tests.Common;

namespace SchemaOrchestrator.Tests.Providers.Sqlite.Schema;

public class SqliteSchemaUpgradeTests
{
    [Fact]
    public async Task UpgradeSchema_AddsColumnAndIndex()
    {
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setupConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setupConnection.OpenAsync();
                await using var setupServices = TestServiceFactory.BuildSqliteServices<SqlitePeopleNameOnlyContext>(setupConnection);
                await using var scope = setupServices.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            await using (var upgradeConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await upgradeConnection.OpenAsync();
                await using var upgradeServices = TestServiceFactory.BuildSqliteServices<SqlitePeopleWithEmailContext>(upgradeConnection);
                await using var scope = upgradeServices.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleWithEmailContext>();

                var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

                Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.AddColumn && o.Column == "Email");
                Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.CreateIndex);
            }
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
        }
    }
}
