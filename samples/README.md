# Sample Projects

These are runnable console apps that demonstrate SchemaOrchestrator upgrade flows without EF migration files.

## Projects

- `samples/SchemaOrchestrator.Samples.Sqlite`
- `samples/SchemaOrchestrator.Samples.SqlServer`
- `samples/SchemaOrchestrator.Samples.PostgreSql`

Each sample has the same internal layout:

- `Program.cs` (thin entrypoint)
- `SampleRunner.cs` (scenario orchestration)
- `Contexts/` (V1/V2 DbContext definitions)
- `Models/` (V1/V2 entity models)
- `Subscribers/` (subscriber hook examples)
- `README.md` (provider-specific usage)

Each sample supports the same scenarios:

- `init-v1`: create baseline v1 schema
- `upgrade-v2`: execute schema upgrade to v2
- `dry-run`: plan-only upgrade report (no schema changes)
- `cleanup`: run TTL cleanup for archived objects
- `restore-latest-archive`: restore most recent archived object

## Sample READMEs

- `samples/SchemaOrchestrator.Samples.Sqlite/README.md`
- `samples/SchemaOrchestrator.Samples.SqlServer/README.md`
- `samples/SchemaOrchestrator.Samples.PostgreSql/README.md`

## SQLite quick run

```bash
dotnet run --project samples/SchemaOrchestrator.Samples.Sqlite -- init-v1
dotnet run --project samples/SchemaOrchestrator.Samples.Sqlite -- upgrade-v2
dotnet run --project samples/SchemaOrchestrator.Samples.Sqlite -- dry-run
```

Optional second argument is database file path.

## SQL Server quick run

```bash
$env:SO_SAMPLE_SQLSERVER_CONN="Server=localhost,1433;Database=SchemaOrchestratorSample;User Id=sa;Password=YourStrong!Passw0rd;Encrypt=False;TrustServerCertificate=True"
dotnet run --project samples/SchemaOrchestrator.Samples.SqlServer -- init-v1
dotnet run --project samples/SchemaOrchestrator.Samples.SqlServer -- upgrade-v2
```

You can also pass the connection string as the second argument.

## PostgreSQL quick run

```bash
$env:SO_SAMPLE_POSTGRES_CONN="Host=localhost;Port=5432;Database=schema_orchestrator_sample;Username=postgres;Password=postgres"
dotnet run --project samples/SchemaOrchestrator.Samples.PostgreSql -- init-v1
dotnet run --project samples/SchemaOrchestrator.Samples.PostgreSql -- upgrade-v2
```

You can also pass the connection string as the second argument.
