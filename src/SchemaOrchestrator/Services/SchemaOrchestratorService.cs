using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SchemaOrchestrator.Abstractions;
using SchemaOrchestrator.Models;
using SchemaOrchestrator.Subscribers;

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
        var executionContext = new UpgradeExecutionContext(dbContext, connection, subscriberContext);

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
                    await subscriber.OnUpgradeStartedAsync(executionContext, cancellationToken);
                }
            }

            var currentSchema = await adapter.ReadCurrentSchemaAsync(connection, cancellationToken);
            var desiredSchema = CreateScriptSnapshotBuilder.Build(dbContext, adapter.DefaultSchema);
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
                    long operationId = 0;
                    if (options.EnableDataSubscribers)
                    {
                        foreach (var subscriber in _subscribers)
                        {
                            await subscriber.BeforeOperationAsync(executionContext, operation, cancellationToken);
                        }
                    }

                    try
                    {
                        operationId = await StartOperationCheckpointAsync(connection, historyId, operation, cancellationToken);
                        await ExecuteOperationAsync(connection, adapter, operation, cancellationToken);
                        executedCount++;
                        await CompleteOperationCheckpointAsync(connection, operationId, "completed", null, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        if (operationId > 0)
                        {
                            await CompleteOperationCheckpointAsync(connection, operationId, "failed", ex.Message, cancellationToken);
                        }

                        throw;
                    }

                    if (options.EnableDataSubscribers)
                    {
                        foreach (var subscriber in _subscribers)
                        {
                            await subscriber.AfterOperationAsync(executionContext, operation, cancellationToken);
                        }
                    }
                }
            }

            report.ExecutedOperations = executedCount;
            report.CompletedAtUtc = DateTime.UtcNow;
            await adapter.CompleteUpgradeHistoryAsync(connection, historyId, executedCount, "completed", null, cancellationToken);

            if (options.EnableDataSubscribers)
            {
                foreach (var subscriber in _subscribers)
                {
                    await subscriber.OnUpgradeCompletedAsync(executionContext, report, cancellationToken);
                }
            }

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schema upgrade failed for provider {Provider}", adapter.Name);
            // include failure in report-like diagnostics via history + operation checkpoints
            if (historyId > 0)
            {
                await adapter.CompleteUpgradeHistoryAsync(connection, historyId, executedCount, "failed", ex.Message, cancellationToken);
            }

            if (options.EnableDataSubscribers)
            {
                foreach (var subscriber in _subscribers)
                {
                    await subscriber.OnUpgradeFailedAsync(executionContext, ex, cancellationToken);
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
        EnsureOperationSupported(adapter, operation);

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

            case SchemaOperationKind.DropIndex:
                if (operation.IndexDefinition is null)
                {
                    throw new InvalidOperationException("DropIndex operation requires IndexDefinition.");
                }

                await ExecuteNonQueryAsync(connection, adapter.BuildDropIndexSql(operation.IndexDefinition), cancellationToken);
                return;

            case SchemaOperationKind.CreateIndex:
                if (operation.IndexDefinition is null)
                {
                    throw new InvalidOperationException("CreateIndex operation requires IndexDefinition.");
                }

                await ExecuteNonQueryAsync(connection, adapter.BuildCreateIndexSql(operation.IndexDefinition), cancellationToken);
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

        if (adapter.Name.Equals("sqlite", StringComparison.OrdinalIgnoreCase) && operation.CurrentTableDefinition is not null)
        {
            await RebuildSqliteTableForAlterAsync(connection, adapter, operation.CurrentTableDefinition, operation.TableDefinition, cancellationToken);
            return;
        }

        await ArchiveColumnAsync(connection, adapter, operation, cancellationToken);
        var addSql = adapter.BuildAddColumnSql(operation.TableDefinition, operation.ColumnDefinition);
        await ExecuteNonQueryAsync(connection, addSql, cancellationToken);
    }

    private static async Task RebuildSqliteTableForAlterAsync(
        DbConnection connection,
        IProviderAdapter adapter,
        TableSchema currentTable,
        TableSchema desiredTable,
        CancellationToken cancellationToken)
    {
        var tempName = $"__so_tmp_{desiredTable.Name}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        var tempTable = new TableSchema
        {
            Schema = desiredTable.Schema,
            Name = tempName,
            Columns = desiredTable.Columns
        };

        await ExecuteNonQueryAsync(connection, adapter.BuildCreateTableSql(tempTable), cancellationToken);

        var currentColumns = currentTable.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var insertColumns = string.Join(", ", desiredTable.Columns.Select(c => adapter.QuoteIdentifier(c.Name)));
        var selectColumns = string.Join(", ", desiredTable.Columns.Select(c =>
        {
            if (currentColumns.TryGetValue(c.Name, out _))
            {
                return adapter.QuoteIdentifier(c.Name);
            }

            return $"NULL AS {adapter.QuoteIdentifier(c.Name)}";
        }));

        var copySql =
            $"INSERT INTO {adapter.QuoteIdentifier(tempName)} ({insertColumns}) " +
            $"SELECT {selectColumns} FROM {adapter.QuoteIdentifier(desiredTable.Name)};";
        await ExecuteNonQueryAsync(connection, copySql, cancellationToken);

        await ExecuteNonQueryAsync(connection, adapter.BuildDropTableSql(currentTable.Schema, currentTable.Name), cancellationToken);
        await ExecuteNonQueryAsync(connection, adapter.BuildRenameTableSql(desiredTable.Schema, tempName, desiredTable.Name), cancellationToken);
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

    private static void EnsureOperationSupported(IProviderAdapter adapter, SchemaOperation operation)
    {
        if ((operation.Kind == SchemaOperationKind.CreateIndex || operation.Kind == SchemaOperationKind.DropIndex)
            && !adapter.Capabilities.SupportsIndexes)
        {
            throw new NotSupportedException($"Provider '{adapter.Name}' does not support index operations.");
        }

        if ((operation.Kind == SchemaOperationKind.ArchiveColumn || operation.Kind == SchemaOperationKind.ArchiveTable)
            && !adapter.Capabilities.SupportsArchiveRename)
        {
            throw new NotSupportedException($"Provider '{adapter.Name}' does not support archive rename operations.");
        }
    }

    private static async Task<long> StartOperationCheckpointAsync(
        DbConnection connection,
        long historyId,
        SchemaOperation operation,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO so_upgrade_operations
(history_id, operation_kind, schema_name, table_name, column_name, index_name, started_at_utc, status)
VALUES
(@history_id, @operation_kind, @schema_name, @table_name, @column_name, @index_name, @started_at_utc, @status);";

        AddParam(cmd, "@history_id", historyId);
        AddParam(cmd, "@operation_kind", operation.Kind.ToString());
        AddParam(cmd, "@schema_name", operation.Schema);
        AddParam(cmd, "@table_name", operation.Table);
        AddParam(cmd, "@column_name", (object?)operation.Column ?? DBNull.Value);
        AddParam(cmd, "@index_name", (object?)operation.IndexDefinition?.Name ?? DBNull.Value);
        AddParam(cmd, "@started_at_utc", DateTime.UtcNow);
        AddParam(cmd, "@status", "running");
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        await using var idCmd = connection.CreateCommand();
        idCmd.CommandText = "SELECT MAX(id) FROM so_upgrade_operations WHERE history_id = @history_id;";
        AddParam(idCmd, "@history_id", historyId);
        var value = await idCmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value);
    }

    private static async Task CompleteOperationCheckpointAsync(
        DbConnection connection,
        long operationId,
        string status,
        string? error,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE so_upgrade_operations SET completed_at_utc = @completed_at_utc, status = @status, error = @error WHERE id = @id";
        AddParam(cmd, "@completed_at_utc", DateTime.UtcNow);
        AddParam(cmd, "@status", status);
        AddParam(cmd, "@error", (object?)error ?? DBNull.Value);
        AddParam(cmd, "@id", operationId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParam(DbCommand command, string name, object value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        command.Parameters.Add(p);
    }
}
