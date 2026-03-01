using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchemaOrchestrator.Abstractions;
using SchemaOrchestrator.Models;

namespace SchemaOrchestrator.Services;

public sealed class SchemaOrchestratorService : ISchemaOrchestrator
{
    private readonly ProviderResolver _providerResolver;
    private readonly ISchemaComparer _schemaComparer;
    private readonly IReadOnlyList<ISchemaUpgradeSubscriber> _subscribers;
    private readonly SchemaOrchestratorOptions _options;
    private readonly ILogger<SchemaOrchestratorService> _logger;

    public SchemaOrchestratorService(
        ProviderResolver providerResolver,
        ISchemaComparer schemaComparer,
        IEnumerable<ISchemaUpgradeSubscriber> subscribers,
        IOptions<SchemaOrchestratorOptions> options,
        ILogger<SchemaOrchestratorService> logger)
    {
        _providerResolver = providerResolver;
        _schemaComparer = schemaComparer;
        _subscribers = subscribers.ToArray();
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UpgradeReport> UpgradeSchemaAsync(DbContext dbContext, UpgradeOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new UpgradeOptions();

        var adapter = _providerResolver.Resolve(dbContext);
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        var connection = dbContext.Database.GetDbConnection();

        var owner = Environment.MachineName + ":" + Guid.NewGuid().ToString("N");
        var subscriberContext = new UpgradeSubscriberContext(adapter.Name, options.DryRun, options.Features);

        long historyId = 0;
        var executedCount = 0;

        try
        {
            await adapter.EnsureOrchestratorTablesAsync(connection, cancellationToken);

            if (_options.EnableLocking)
            {
                await adapter.AcquireLockAsync(connection, _options.LockName, owner, _options.LockTimeout, cancellationToken);
            }

            historyId = await adapter.StartUpgradeHistoryAsync(connection, options.DryRun, cancellationToken);

            if (options.EnableDataSubscribers)
            {
                foreach (var subscriber in _subscribers)
                {
                    await subscriber.OnUpgradeStartedAsync(dbContext, connection, subscriberContext, cancellationToken);
                }
            }

            var currentSchema = await adapter.ReadCurrentSchemaAsync(connection, cancellationToken);
            var desiredSchema = EfModelSchemaBuilder.Build(dbContext, adapter.DefaultSchema);
            var operations = _schemaComparer.Compare(currentSchema, desiredSchema, _options.MigrationsPolicy);

            var report = new UpgradeReport
            {
                Provider = adapter.Name,
                DryRun = options.DryRun
            };

            report.PlannedOperations.AddRange(operations);

            if (!options.DryRun)
            {
                foreach (var operation in operations)
                {
                    if (options.EnableDataSubscribers)
                    {
                        foreach (var subscriber in _subscribers)
                        {
                            await subscriber.BeforeOperationAsync(dbContext, connection, subscriberContext, operation, cancellationToken);
                        }
                    }

                    await ExecuteOperationAsync(connection, adapter, operation, cancellationToken);
                    executedCount++;

                    if (options.EnableDataSubscribers)
                    {
                        foreach (var subscriber in _subscribers)
                        {
                            await subscriber.AfterOperationAsync(dbContext, connection, subscriberContext, operation, cancellationToken);
                        }
                    }
                }
            }

            report.ExecutedOperations = executedCount;
            await adapter.CompleteUpgradeHistoryAsync(connection, historyId, executedCount, "completed", null, cancellationToken);

            if (options.EnableDataSubscribers)
            {
                foreach (var subscriber in _subscribers)
                {
                    await subscriber.OnUpgradeCompletedAsync(dbContext, connection, subscriberContext, report, cancellationToken);
                }
            }

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schema upgrade failed for provider {Provider}", adapter.Name);
            if (historyId > 0)
            {
                await adapter.CompleteUpgradeHistoryAsync(connection, historyId, executedCount, "failed", ex.Message, cancellationToken);
            }

            if (options.EnableDataSubscribers)
            {
                foreach (var subscriber in _subscribers)
                {
                    await subscriber.OnUpgradeFailedAsync(dbContext, connection, subscriberContext, ex, cancellationToken);
                }
            }

            throw;
        }
        finally
        {
            if (_options.EnableLocking)
            {
                await adapter.ReleaseLockAsync(connection, _options.LockName, owner, cancellationToken);
            }

            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private async Task ExecuteOperationAsync(DbConnection connection, IProviderAdapter adapter, SchemaOperation operation, CancellationToken cancellationToken)
    {
        switch (operation.Kind)
        {
            case SchemaOperationKind.CreateTable:
                await ExecuteNonQueryAsync(connection, adapter.BuildCreateTableSql(operation.TableDefinition!), cancellationToken);
                return;

            case SchemaOperationKind.AddColumn:
                await ExecuteNonQueryAsync(connection, adapter.BuildAddColumnSql(operation.TableDefinition!, operation.ColumnDefinition!), cancellationToken);
                return;

            case SchemaOperationKind.AlterColumn:
                await ExecuteAlterColumnAsync(connection, adapter, operation, cancellationToken);
                return;

            case SchemaOperationKind.ArchiveTable:
                await ArchiveTableAsync(connection, adapter, operation, cancellationToken);
                return;

            case SchemaOperationKind.ArchiveColumn:
                await ArchiveColumnAsync(connection, adapter, operation, cancellationToken);
                return;

            default:
                throw new NotSupportedException($"Operation kind '{operation.Kind}' is not supported.");
        }
    }

    private async Task ExecuteAlterColumnAsync(DbConnection connection, IProviderAdapter adapter, SchemaOperation operation, CancellationToken cancellationToken)
    {
        if (operation.ColumnDefinition is null || operation.CurrentColumnDefinition is null || operation.TableDefinition is null)
        {
            throw new InvalidOperationException("AlterColumn operation requires current and desired column definitions.");
        }

        if (adapter.SupportsInPlaceColumnAlter)
        {
            var sql = adapter.BuildAlterColumnSql(operation.TableDefinition, operation.CurrentColumnDefinition, operation.ColumnDefinition);
            await ExecuteNonQueryAsync(connection, sql, cancellationToken);
            return;
        }

        await ArchiveColumnAsync(connection, adapter, operation, cancellationToken);
        var addSql = adapter.BuildAddColumnSql(operation.TableDefinition, operation.ColumnDefinition);
        await ExecuteNonQueryAsync(connection, addSql, cancellationToken);
    }

    private async Task ArchiveTableAsync(DbConnection connection, IProviderAdapter adapter, SchemaOperation operation, CancellationToken cancellationToken)
    {
        var archiveName = $"Deleted_{operation.Table}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        var sql = adapter.BuildRenameTableSql(operation.Schema, operation.Table, archiveName);
        await ExecuteNonQueryAsync(connection, sql, cancellationToken);

        await adapter.RecordArchivedObjectAsync(
            connection,
            "table",
            operation.Schema,
            operation.Table,
            null,
            archiveName,
            DateTime.UtcNow,
            DateTime.UtcNow.Add(_options.ArchiveTtl),
            cancellationToken);
    }

    private async Task ArchiveColumnAsync(DbConnection connection, IProviderAdapter adapter, SchemaOperation operation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operation.Column))
        {
            throw new InvalidOperationException("ArchiveColumn operation requires Column value.");
        }

        var archiveName = $"Deleted_{operation.Column}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        var sql = adapter.BuildRenameColumnSql(operation.Schema, operation.Table, operation.Column, archiveName);
        await ExecuteNonQueryAsync(connection, sql, cancellationToken);

        await adapter.RecordArchivedObjectAsync(
            connection,
            "column",
            operation.Schema,
            operation.Table,
            operation.Column,
            archiveName,
            DateTime.UtcNow,
            DateTime.UtcNow.Add(_options.ArchiveTtl),
            cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
