namespace SchemaOrchestrator.Models;

public sealed record ProviderCapabilities(
    bool SupportsInPlaceColumnAlter,
    bool SupportsIndexes,
    bool SupportsArchiveRename,
    bool SupportsForeignKeys,
    bool SupportsCheckConstraints);
