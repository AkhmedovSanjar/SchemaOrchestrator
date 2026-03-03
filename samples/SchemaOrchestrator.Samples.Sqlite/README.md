# SQLite Sample

Demonstrates SchemaOrchestrator runtime schema upgrade on SQLite.

## Folder structure

- `Program.cs`: entrypoint
- `SampleRunner.cs`: scenario command handler
- `Contexts/`: V1 and V2 EF Core DbContexts
- `Models/`: V1 and V2 entity classes
- `Subscribers/`: subscriber hook examples

## Run

```bash
dotnet run --project samples/SchemaOrchestrator.Samples.Sqlite -- init-v1
dotnet run --project samples/SchemaOrchestrator.Samples.Sqlite -- upgrade-v2
dotnet run --project samples/SchemaOrchestrator.Samples.Sqlite -- dry-run
dotnet run --project samples/SchemaOrchestrator.Samples.Sqlite -- cleanup
dotnet run --project samples/SchemaOrchestrator.Samples.Sqlite -- restore-latest-archive
```

Optional second argument: sqlite db path (default `schema-orchestrator-sqlite.db`).
