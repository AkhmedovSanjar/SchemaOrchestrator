namespace SchemaOrchestrator.Models;

public sealed class UpgradeOptions
{
    public bool DryRun { get; set; }
    public bool EnableDataSubscribers { get; set; } = true;
    public IReadOnlyCollection<string> Features { get; set; } = Array.Empty<string>();
}
