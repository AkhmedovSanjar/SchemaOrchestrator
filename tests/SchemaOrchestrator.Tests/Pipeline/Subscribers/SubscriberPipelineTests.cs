using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchemaOrchestrator.Abstractions;
using SchemaOrchestrator.Extensions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Subscribers;
using SchemaOrchestrator.Subscribers.Attributes;
using SchemaOrchestrator.Tests.Common;
using SchemaOrchestrator.Tests.Providers.Sqlite;

namespace SchemaOrchestrator.Tests.Pipeline.Subscribers;

public class SubscriberPipelineTests
{
    [Fact]
    public async Task SubscriberHooks_AreInvokedInExpectedOrder()
    {
        SubscriberRecorder.Clear();
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setupConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setupConn.OpenAsync();
                await using var provider = BuildServicesWithManualSubscriber<SqlitePeopleNameOnlyContext, OrderedSubscriberImpl>(setupConn);
                await using var scope = provider.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            await using (var runConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await runConn.OpenAsync();
                await using var provider = BuildServicesWithManualSubscriber<SqlitePeopleWithEmailContext, OrderedSubscriberImpl>(runConn);
                await using var scope = provider.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleWithEmailContext>();
                await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false, EnableDataSubscribers = true });
            }

            Assert.Contains("start", SubscriberRecorder.Events);
            Assert.Contains("before", SubscriberRecorder.Events);
            Assert.Contains("after", SubscriberRecorder.Events);
            Assert.Contains("completed", SubscriberRecorder.Events);

            var start = SubscriberRecorder.Events.IndexOf("start");
            var before = SubscriberRecorder.Events.IndexOf("before");
            var after = SubscriberRecorder.Events.IndexOf("after");
            var completed = SubscriberRecorder.Events.IndexOf("completed");
            Assert.True(start < before);
            Assert.True(before < after);
            Assert.True(after < completed);
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
            SubscriberRecorder.Clear();
        }
    }

    [Fact]
    public async Task PrivateAttributedMethods_AreDiscoveredAndInvoked()
    {
        SubscriberRecorder.Clear();
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setupConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setupConn.OpenAsync();
                await using var provider = BuildServicesWithDiscovery<SqlitePeopleNameOnlyContext>(setupConn, typeof(PrivateMethodDiscoverySubscriber).Assembly);
                await using var scope = provider.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            await using (var runConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await runConn.OpenAsync();
                await using var provider = BuildServicesWithDiscovery<SqlitePeopleWithEmailContext>(runConn, typeof(PrivateMethodDiscoverySubscriber).Assembly);
                await using var scope = provider.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleWithEmailContext>();
                await ctx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false, EnableDataSubscribers = true });
            }

            Assert.Contains("private-before", SubscriberRecorder.Events);
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
            SubscriberRecorder.Clear();
        }
    }

    [Fact]
    public async Task InvalidSubscriberMethodSignature_Throws()
    {
        var reflectedType = typeof(ServiceCollectionExtensions).Assembly
            .GetType("SchemaOrchestrator.Subscribers.ReflectedSchemaUpgradeSubscriber", throwOnError: true)!;

        var badInstance = new InvalidSignatureInternalSubscriber();
        var methods = badInstance.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.GetCustomAttribute<BeforeOperationAttribute>() is not null)
            .ToArray();

        var reflected = Activator.CreateInstance(reflectedType, badInstance, methods)!;

        await using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<SqlitePeopleNameOnlyContext>().UseSqlite(conn).Options;
        await using var db = new SqlitePeopleNameOnlyContext(options);

        var context = new UpgradeExecutionContext(db, conn, new UpgradeSubscriberContext("sqlite", false, Array.Empty<string>()));
        var operation = new SchemaOperation(SchemaOperationKind.AddColumn, "main", "People", "Email");

        var method = reflectedType.GetMethod("BeforeOperationAsync")!;
        var task = (Task)method.Invoke(reflected, new object?[] { context, operation, CancellationToken.None })!;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
    }

    [Fact]
    public async Task OnUpgradeFailed_IsInvoked_WhenSubscriberThrows()
    {
        SubscriberRecorder.Clear();
        var dbPath = TestDatabaseFiles.NewSqliteDbPath();
        try
        {
            await using (var setupConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await setupConn.OpenAsync();
                await using var provider = BuildServicesWithManualSubscriber<SqlitePeopleNameOnlyContext, ThrowingSubscriberImpl>(setupConn);
                await using var scope = provider.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<SqlitePeopleNameOnlyContext>();
                await ctx.Database.EnsureCreatedAsync();
            }

            await using var runConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await runConn.OpenAsync();
            await using var runProvider = BuildServicesWithManualSubscriber<SqlitePeopleWithEmailContext, ThrowingSubscriberImpl>(runConn);
            await using var runScope = runProvider.CreateAsyncScope();
            var runCtx = runScope.ServiceProvider.GetRequiredService<SqlitePeopleWithEmailContext>();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                runCtx.UpgradeSchemaAsync(new UpgradeOptions { DryRun = false, EnableDataSubscribers = true }));

            Assert.Contains("failed", SubscriberRecorder.Events);
        }
        finally
        {
            await TestDatabaseFiles.DeleteFileWithRetryAsync(dbPath);
            SubscriberRecorder.Clear();
        }
    }

    private static ServiceProvider BuildServicesWithDiscovery<TContext>(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        params Assembly[] assemblies)
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSchemaOrchestrator(options =>
        {
            options.MigrationsPolicy = DestructiveChangePolicy.Archive;
            options.ArchiveTtl = TimeSpan.Zero;
        });
        services.AddSchemaOrchestratorSubscribersFromAssemblies(assemblies);
        services.AddDbContext<TContext>(opt => opt.UseSqlite(connection));
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildServicesWithManualSubscriber<TContext, TSubscriber>(Microsoft.Data.Sqlite.SqliteConnection connection)
        where TContext : DbContext
        where TSubscriber : class, ISchemaUpgradeSubscriber
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSchemaOrchestrator(options =>
        {
            options.MigrationsPolicy = DestructiveChangePolicy.Archive;
            options.ArchiveTtl = TimeSpan.Zero;
        });
        services.AddScoped<ISchemaUpgradeSubscriber, TSubscriber>();
        services.AddDbContext<TContext>(opt => opt.UseSqlite(connection));
        return services.BuildServiceProvider();
    }

    private static class SubscriberRecorder
    {
        private static readonly ConcurrentQueue<string> Queue = new();
        public static List<string> Events => Queue.ToList();
        public static void Add(string value) => Queue.Enqueue(value);
        public static void Clear()
        {
            while (Queue.TryDequeue(out _)) { }
        }
    }

    private sealed class OrderedSubscriberImpl : ISchemaUpgradeSubscriber
    {
        public Task OnUpgradeStartedAsync(UpgradeExecutionContext context, CancellationToken cancellationToken)
        {
            SubscriberRecorder.Add("start");
            return Task.CompletedTask;
        }

        public Task BeforeOperationAsync(UpgradeExecutionContext context, SchemaOperation operation, CancellationToken cancellationToken)
        {
            SubscriberRecorder.Add("before");
            return Task.CompletedTask;
        }

        public Task AfterOperationAsync(UpgradeExecutionContext context, SchemaOperation operation, CancellationToken cancellationToken)
        {
            SubscriberRecorder.Add("after");
            return Task.CompletedTask;
        }

        public Task OnUpgradeCompletedAsync(UpgradeExecutionContext context, UpgradeReport report, CancellationToken cancellationToken)
        {
            SubscriberRecorder.Add("completed");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSubscriberImpl : ISchemaUpgradeSubscriber
    {
        public Task BeforeOperationAsync(UpgradeExecutionContext context, SchemaOperation operation, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("fail from subscriber");
        }

        public Task OnUpgradeFailedAsync(UpgradeExecutionContext context, Exception exception, CancellationToken cancellationToken)
        {
            SubscriberRecorder.Add("failed");
            return Task.CompletedTask;
        }
    }

    private sealed class InvalidSignatureInternalSubscriber
    {
        [BeforeOperation]
        public Task Before(UpgradeExecutionContext context, int invalidParameter)
        {
            return Task.CompletedTask;
        }
    }
}

public class PrivateMethodDiscoverySubscriber
{
    [BeforeOperation]
    private Task Before(UpgradeExecutionContext context, SchemaOperation operation)
    {
        var recorderType = typeof(SubscriberPipelineTests)
            .GetNestedType("SubscriberRecorder", BindingFlags.NonPublic)!;
        var addMethod = recorderType.GetMethod("Add", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        addMethod.Invoke(null, new object[] { "private-before" });
        return Task.CompletedTask;
    }
}
