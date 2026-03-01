namespace SchemaOrchestrator.Models;

public sealed class UpgradeReport
{
    public required string Provider { get; init; }
    public bool DryRun { get; init; }
    public int ExecutedOperations { get; set; }
    public List<SchemaOperation> PlannedOperations { get; } = new();
}
