# PostgreSQL Sample

Demonstrates SchemaOrchestrator runtime schema upgrade on PostgreSQL.

## Folder structure

- `Program.cs`: entrypoint
- `SampleRunner.cs`: scenario command handler
- `Contexts/`: V1 and V2 EF Core DbContexts
- `Models/`: V1 and V2 entity classes
- `Subscribers/`: subscriber hook examples

## Run

Set connection string from environment variable or argument:

```powershell
$env:SO_SAMPLE_POSTGRES_CONN="Host=localhost;Port=5432;Database=schema_orchestrator_sample;Username=postgres;Password=postgres"
dotnet run --project samples/SchemaOrchestrator.Samples.PostgreSql -- init-v1
dotnet run --project samples/SchemaOrchestrator.Samples.PostgreSql -- upgrade-v2
dotnet run --project samples/SchemaOrchestrator.Samples.PostgreSql -- dry-run
dotnet run --project samples/SchemaOrchestrator.Samples.PostgreSql -- cleanup
dotnet run --project samples/SchemaOrchestrator.Samples.PostgreSql -- restore-latest-archive
```

Optional second argument: explicit PostgreSQL connection string.
