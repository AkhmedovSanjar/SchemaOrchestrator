namespace SchemaOrchestrator.Models;

public sealed record IndexSchema(
    string Schema,
    string Table,
    string Name,
    IReadOnlyList<string> Columns,
    bool IsUnique = false)
{
    public string Key => $"{Schema}.{Table}.{Name}";
}
