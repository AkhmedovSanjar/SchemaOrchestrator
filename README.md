# SchemaOrchestrator

SchemaOrchestrator is a provider-agnostic schema/data upgrade engine built on top of EF Core.
It applies database changes without EF migration files by diffing the live database against a temporary model snapshot generated from the current `DbContext`.

## Vision

Build a `DataUpgrade`-style library that:

- works with `SqlServer`, `PostgreSQL`, and `SQLite`
- performs schema + optional data migrations from runtime model state
- supports safe, ordered operations (tables, columns, constraints, indexes, functions where supported)
- supports subscriber hooks for custom data transition scripts
- avoids destructive drops by default (rename/archive/soft-delete strategy)

## Why this project

EF Core migrations are file-based and developer-managed.  
SchemaOrchestrator is designed for teams that want:

- automatic runtime upgrades across tenants/environments
- no migration file drift
- centralized, programmable upgrade rules
- provider-specific SQL handled behind one abstraction

## Concept summary

SchemaOrchestrator is a unified engine that:

- creates a temporary model snapshot from the current EF Core model
- compares live database schema with the temporary snapshot
- generates ordered, provider-safe operations
- executes schema and optional data migration hooks
- applies non-destructive deletion and delayed cleanup policies
- works through provider adapters for SQL Server, PostgreSQL, and SQLite

## Deleted object pattern (shared across all providers)

For deleted tables/columns, SchemaOrchestrator will not use DB-specific metadata features.
It will use one provider-neutral pattern:

- rename dropped tables/columns to archived names (`Deleted_<Name>_<timestamp>`)
- register each archived object in internal metadata tables (for TTL cleanup and restore support)
- run cleanup later based on policy (`Archive`/`TTL`) instead of immediate hard drop

## Target architecture

1. `IProviderAdapter`
- Reads schema metadata
- Generates provider-safe SQL
- Handles capabilities (functions, metadata tables, locks, transactional DDL)

2. `ModelSnapshotBuilder`
- Builds temporary model schema from EF Core create script
- Creates temp DB/schema depending on provider

3. `SchemaReader` + `SchemaComparer`
- Reads current + temp schema into normalized model
- Produces differences: add/change/delete/rename candidates

4. `OperationPlanner`
- Converts differences into ordered `UpgradeOperation`s
- Adds provider-specific constraints for execution order

5. `UpgradeExecutor`
- Executes operations in transaction where possible
- Emits logs + result report

6. `SubscriberPipeline`
- Attribute-based hooks for entity/property/table events
- Collects custom SQL/data scripts and injects them into plan

7. `CleanupManager`
- Applies delayed physical cleanup policy (TTL-based)
- Supports restore/undelete semantics where possible

8. `LockManager`
- Provider-specific distributed DB lock to avoid concurrent upgrades

9. `MetadataStore`
- Uses internal orchestrator tables (not DB extended properties)
- Stores archived/deleted object metadata, timestamps, and restore markers
- Keeps deletion lifecycle identical across SQL Server, PostgreSQL, and SQLite

## Provider strategy

### SQL Server
- Use `sys.*` + `INFORMATION_SCHEMA`
- Use shared orchestrator metadata tables for deletion/archive state
- Support advanced function/index/constraint operations

### PostgreSQL
- Use `information_schema` + `pg_catalog`
- Use transactional DDL and schema namespaces for temp model
- Use advisory/table locks for upgrade coordination

### SQLite
- Use `sqlite_master` + pragma metadata
- Handle limited alter-table support via table-rebuild strategy
- Keep the same delete/archive lifecycle via orchestrator metadata tables

## Advanced implementation plan

### Phase 1: Foundation
- Define core contracts: `ISchemaReader`, `ISchemaComparer`, `IOperation`, `IUpgradeExecutor`, `IProviderAdapter`
- Define normalized metadata model: tables, columns, PK/FK, indexes, constraints, functions, annotations
- Build operation ordering model (`OperationType` + dependency graph)

### Phase 2: MVP provider (PostgreSQL first)
- Implement temp schema generation from EF model
- Implement schema reader + comparer for core objects
- Implement operation planner/executor for add/change/delete table/column/index/fk
- Implement DB lock + transaction boundary

### Phase 3: Subscriber/data migration hooks
- Implement attribute-based hooks (`EntityAdded`, `PropertyAdded`, `PropertyChanged`, etc.)
- Add execution context APIs for SQL/read/scalar operations
- Inject subscriber operations into ordered plan

### Phase 4: SQL Server adapter
- Implement metadata readers and SQL generator parity with MVP surface
- Use provider-neutral metadata tables (same model as PostgreSQL/SQLite)
- Add cleanup lifecycle controls

### Phase 5: SQLite adapter
- Implement supported diff/operation subset
- Add table-recreate flow for unsupported direct alters
- Add explicit capability matrix and fallback rules

### Phase 6: Reliability and safety
- Add dry-run mode (generate plan only)
- Add destructive-change policy gates (`Block`, `Archive`, `Allow`)
- Add resumable execution markers + upgrade history table
- Add structured logs and diagnostics

### Phase 7: Testing + packaging
- Build provider-specific integration tests (Docker for SQL Server/Postgres, file DB for SQLite)
- Add scenario tests for rename detection, one-record entities, and cleanup TTL
- Package as NuGet (`SchemaOrchestrator`) with extension methods for DI/startup

## Non-goals (initially)

- Full automatic rename detection with zero false positives
- Perfect parity of provider features across SQL Server/Postgres/SQLite
- Replacing EF Core migrations for teams that require audited migration scripts

## Example usage (target API)

```csharp
services.AddSchemaOrchestrator(options =>
{
    options.MigrationsPolicy = DestructiveChangePolicy.Archive;
    options.ArchiveTtl = TimeSpan.FromDays(30);
});

await dbContext.UpgradeSchemaAsync(new UpgradeOptions
{
    EnableDataSubscribers = true,
    DryRun = false
});

var cleaned = await dbContext.CleanupArchivedObjectsAsync();
```

## Current status

- Core library implemented (`net9.0`)
- Provider adapters implemented: SQL Server, PostgreSQL, SQLite
- Shared metadata-table archive pattern implemented (`so_archived_objects`)
- Upgrade lifecycle tables implemented (`so_upgrade_history`, `so_locks`)
- EF Core extension methods implemented (`AddSchemaOrchestrator`, `UpgradeSchemaAsync`)
- Type/nullability change detection implemented (`AlterColumn` operation)
- Subscriber hook pipeline implemented (`ISchemaUpgradeSubscriber`)
- TTL cleanup worker implemented (`CleanupArchivedObjectsAsync`)
- Create-script snapshot parsing implemented (`GenerateCreateScript` -> schema snapshot)
- Index diff/planning implemented (`CreateIndex`/`DropIndex` operations)
- Foreign key diff/planning implemented (`CreateForeignKey`/`DropForeignKey` operations)
- Subscriber attribute discovery implemented (`OnUpgradeStarted`, `BeforeOperation`, etc.)
- Restore/undelete API implemented (`RestoreArchivedObjectAsync`)
- Operation checkpoint table implemented (`so_upgrade_operations`)

## Test layout

Tests are organized by purpose and provider:

- `tests/SchemaOrchestrator.Tests/Common`
- `tests/SchemaOrchestrator.Tests/Pipeline/Ordering`
- `tests/SchemaOrchestrator.Tests/Providers/Sqlite/Schema`
- `tests/SchemaOrchestrator.Tests/Providers/Sqlite/Cleanup`
- `tests/SchemaOrchestrator.Tests/Providers/Sqlite/Diagnostics`
- `tests/SchemaOrchestrator.Tests/Providers/Sqlite/Capabilities`
- `tests/SchemaOrchestrator.Tests/Providers/SqlServer/Schema`
- `tests/SchemaOrchestrator.Tests/Providers/SqlServer/Cleanup`
- `tests/SchemaOrchestrator.Tests/Providers/SqlServer/Diagnostics`
- `tests/SchemaOrchestrator.Tests/Providers/SqlServer/Capabilities`
- `tests/SchemaOrchestrator.Tests/Providers/PostgreSql/Schema`
- `tests/SchemaOrchestrator.Tests/Providers/PostgreSql/Cleanup`
- `tests/SchemaOrchestrator.Tests/Providers/PostgreSql/Diagnostics`
- `tests/SchemaOrchestrator.Tests/Providers/PostgreSql/Capabilities`

Provider integration tests use environment variables:

- `SO_TEST_SQLSERVER_CONN`
- `SO_TEST_POSTGRES_CONN`

CI workflows:

- `.github/workflows/ci.yml`:
  - build + SQLite/unit tests
  - SQL Server integration tests
  - PostgreSQL integration tests (version matrix)
  - provider/package compatibility matrix (.NET SDK versions)
- `.github/workflows/pack-validate.yml`:
  - NuGet pack validation + local package smoke-install

## Tracked implementation checklist

- [x] Step 1: True temporary snapshot from EF create script and live-vs-temp comparison
- [x] Step 2: Constraint/index/foreign-key operation coverage + dependency-safe ordering (FK + index coverage and ordering implemented)
- [x] Step 3: SQLite table-rebuild flow for unsupported alters with data copy
- [x] Step 4: Attribute-based subscriber discovery + richer execution context APIs
- [x] Step 5: Restore/undelete lifecycle and cleanup dry-run mode
- [x] Step 6: Resumable checkpoints + richer structured diagnostics
- [x] Step 7: Provider capability matrix and planner-level policy enforcement
- [x] Step 8: Provider integration tests + scenario tests (baseline SQLite integration tests implemented)
- [x] Step 9: NuGet packaging metadata and release-ready artifacts (initial package metadata configured)
