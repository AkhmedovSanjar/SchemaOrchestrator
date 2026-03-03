# SQL Server Sample

Demonstrates SchemaOrchestrator runtime schema upgrade on SQL Server.

## Folder structure

- `Program.cs`: entrypoint
- `SampleRunner.cs`: scenario command handler
- `Contexts/`: V1 and V2 EF Core DbContexts
- `Models/`: V1 and V2 entity classes
- `Subscribers/`: subscriber hook examples

## Run

Set connection string from environment variable or argument:

```powershell
$env:SO_SAMPLE_SQLSERVER_CONN="Server=localhost,1433;Database=SchemaOrchestratorSample;User Id=sa;Password=YourStrong!Passw0rd;Encrypt=False;TrustServerCertificate=True"
dotnet run --project samples/SchemaOrchestrator.Samples.SqlServer -- init-v1
dotnet run --project samples/SchemaOrchestrator.Samples.SqlServer -- upgrade-v2
dotnet run --project samples/SchemaOrchestrator.Samples.SqlServer -- dry-run
dotnet run --project samples/SchemaOrchestrator.Samples.SqlServer -- cleanup
dotnet run --project samples/SchemaOrchestrator.Samples.SqlServer -- restore-latest-archive
```

Optional second argument: explicit SQL Server connection string.
