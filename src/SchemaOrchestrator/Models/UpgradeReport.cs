namespace SchemaOrchestrator.Models;

public sealed class UpgradeReport
{
    public required string Provider { get; init; }
    public bool DryRun { get; init; }
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public int ExecutedOperations { get; set; }
    public string? Error { get; set; }
    public List<SchemaOperation> PlannedOperations { get; } = new();
}
