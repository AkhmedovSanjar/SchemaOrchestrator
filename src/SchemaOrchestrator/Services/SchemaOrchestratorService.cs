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
    private readonly SchemaOrchestratorOptions _options;
    private readonly ILogger<SchemaOrchestratorService> _logger;

    public SchemaOrchestratorService(
        ProviderResolver providerResolver,
        ISchemaComparer schemaComparer,
        IOptions<SchemaOrchestratorOptions> options,
        ILogger<SchemaOrchestratorService> logger)
    {
        _providerResolver = providerResolver;
        _schemaComparer = schemaComparer;
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
                    await ExecuteOperationAsync(connection, adapter, operation, cancellationToken);
                    executedCount++;
                }
            }

            report.ExecutedOperations = executedCount;
            await adapter.CompleteUpgradeHistoryAsync(connection, historyId, executedCount, "completed", null, cancellationToken);
            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schema upgrade failed for provider {Provider}", adapter.Name);
            if (historyId > 0)
            {
                await adapter.CompleteUpgradeHistoryAsync(connection, historyId, executedCount, "failed", ex.Message, cancellationToken);
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
        string sql;
        switch (operation.Kind)
        {
            case SchemaOperationKind.CreateTable:
                sql = adapter.BuildCreateTableSql(operation.TableDefinition!);
                break;

            case SchemaOperationKind.AddColumn:
                sql = adapter.BuildAddColumnSql(operation.TableDefinition!, operation.ColumnDefinition!);
                break;

            case SchemaOperationKind.ArchiveTable:
                sql = BuildArchiveTableSql(adapter, operation, out var tableArchiveName);
                await ExecuteNonQueryAsync(connection, sql, cancellationToken);
                await adapter.RecordArchivedObjectAsync(
                    connection,
                    "table",
                    operation.Schema,
                    operation.Table,
                    null,
                    tableArchiveName,
                    DateTime.UtcNow,
                    DateTime.UtcNow.Add(_options.ArchiveTtl),
                    cancellationToken);
                return;

            case SchemaOperationKind.ArchiveColumn:
                sql = BuildArchiveColumnSql(adapter, operation, out var columnArchiveName);
                await ExecuteNonQueryAsync(connection, sql, cancellationToken);
                await adapter.RecordArchivedObjectAsync(
                    connection,
                    "column",
                    operation.Schema,
                    operation.Table,
                    operation.Column,
                    columnArchiveName,
                    DateTime.UtcNow,
                    DateTime.UtcNow.Add(_options.ArchiveTtl),
                    cancellationToken);
                return;

            default:
                throw new NotSupportedException($"Operation kind '{operation.Kind}' is not supported.");
        }

        await ExecuteNonQueryAsync(connection, sql, cancellationToken);
    }

    private static string BuildArchiveTableSql(IProviderAdapter adapter, SchemaOperation operation, out string archiveName)
    {
        archiveName = $"Deleted_{operation.Table}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        return adapter.BuildRenameTableSql(operation.Schema, operation.Table, archiveName);
    }

    private static string BuildArchiveColumnSql(IProviderAdapter adapter, SchemaOperation operation, out string archiveName)
    {
        if (string.IsNullOrWhiteSpace(operation.Column))
        {
            throw new InvalidOperationException("ArchiveColumn operation requires Column value.");
        }

        archiveName = $"Deleted_{operation.Column}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        return adapter.BuildRenameColumnSql(operation.Schema, operation.Table, operation.Column, archiveName);
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
