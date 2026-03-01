namespace SchemaOrchestrator.Models;

public sealed class SchemaOrchestratorOptions
{
    public DestructiveChangePolicy MigrationsPolicy { get; set; } = DestructiveChangePolicy.Archive;
    public TimeSpan ArchiveTtl { get; set; } = TimeSpan.FromDays(30);
    public bool EnableLocking { get; set; } = true;
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public string LockName { get; set; } = "SchemaOrchestratorUpgrade";
}
