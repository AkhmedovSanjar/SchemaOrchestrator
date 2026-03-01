using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Tests;

public class SqliteIntegrationTests
{
    [Fact]
    public async Task UpgradeSchema_AddsColumnAndIndex()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"so_test_{Guid.NewGuid():N}.db");
        try
        {
            await using (var setupConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setupConnection.OpenAsync();
                await using var setupServices = BuildServices<InitialContext>(setupConnection);
                await using var scope = setupServices.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<InitialContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            await using (var upgradeConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await upgradeConnection.OpenAsync();
                await using var upgradeServices = BuildServices<UpgradedContext>(upgradeConnection);
                await using var scope = upgradeServices.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<UpgradedContext>();

                var report = await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

                Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.AddColumn && o.Column == "Email");
                Assert.Contains(report.PlannedOperations, o => o.Kind == SchemaOperationKind.CreateIndex);
            }
        }
        finally
        {
            await DeleteFileWithRetryAsync(dbPath);
        }
    }

    [Fact]
    public async Task CleanupDryRun_AndRestoreArchivedColumn_Works()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"so_test_{Guid.NewGuid():N}.db");
        try
        {
            await using (var setupConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setupConnection.OpenAsync();
                await using var services = BuildServices<UpgradedContext>(setupConnection);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<UpgradedContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            long archiveId;
            await using (var archiveConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await archiveConnection.OpenAsync();
                await using var services = BuildServices<InitialContext>(archiveConnection);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<InitialContext>();
                await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false });

                await using var cmd = archiveConnection.CreateCommand();
                cmd.CommandText = "SELECT id FROM so_archived_objects ORDER BY id DESC LIMIT 1";
                var value = await cmd.ExecuteScalarAsync();
                archiveId = Convert.ToInt64(value);

                var dryRunCount = await ctx.CleanupArchivedObjectsAsync(dryRun: true);
                Assert.True(dryRunCount >= 1);
            }

            await using (var restoreConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await restoreConnection.OpenAsync();
                await using var services = BuildServices<InitialContext>(restoreConnection);
                await using var scope = services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<InitialContext>();

                var restored = await ctx.RestoreArchivedObjectAsync(archiveId);
                Assert.True(restored);
            }
        }
        finally
        {
            await DeleteFileWithRetryAsync(dbPath);
        }
    }

    private static ServiceProvider BuildServices<TContext>(Microsoft.Data.Sqlite.SqliteConnection connection)
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSchemaOrchestrator(options =>
        {
            options.MigrationsPolicy = DestructiveChangePolicy.Archive;
            options.ArchiveTtl = TimeSpan.Zero;
        });

        services.AddDbContext<TContext>(opt => opt.UseSqlite(connection));
        return services.BuildServiceProvider();
    }

    private static async Task DeleteFileWithRetryAsync(string path)
    {
        for (var i = 0; i < 10; i++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }
            catch (IOException)
            {
                await Task.Delay(100);
            }
        }
    }

    private sealed class InitialContext : DbContext
    {
        public InitialContext(DbContextOptions<InitialContext> options) : base(options)
        {
        }

        public DbSet<PersonV1> People => Set<PersonV1>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PersonV1>(b =>
            {
                b.ToTable("People");
                b.HasKey(x => x.Id);
                b.Property(x => x.Name).HasMaxLength(100);
            });
        }
    }

    private sealed class UpgradedContext : DbContext
    {
        public UpgradedContext(DbContextOptions<UpgradedContext> options) : base(options)
        {
        }

        public DbSet<PersonV2> People => Set<PersonV2>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PersonV2>(b =>
            {
                b.ToTable("People");
                b.HasKey(x => x.Id);
                b.Property(x => x.Name).HasMaxLength(150);
                b.Property(x => x.Email).HasMaxLength(200);
                b.HasIndex(x => x.Name).HasDatabaseName("IX_People_Name");
            });
        }
    }

    private sealed class PersonV1
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class PersonV2
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
    }
}
