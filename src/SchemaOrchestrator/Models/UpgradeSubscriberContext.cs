namespace SchemaOrchestrator.Models;

public sealed record UpgradeSubscriberContext(
    string Provider,
    bool DryRun,
    IReadOnlyCollection<string> Features);
