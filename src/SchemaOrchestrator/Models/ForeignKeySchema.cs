namespace SchemaOrchestrator.Models;

public sealed record ForeignKeySchema(
    string Schema,
    string Table,
    string Name,
    IReadOnlyList<string> Columns,
    string PrincipalSchema,
    string PrincipalTable,
    IReadOnlyList<string> PrincipalColumns)
{
    public string Key => $"{Schema}.{Table}.{Name}";

    public string SignatureKey =>
        $"{Schema}.{Table}({string.Join(',', Columns).ToLowerInvariant()})->{PrincipalSchema}.{PrincipalTable}({string.Join(',', PrincipalColumns).ToLowerInvariant()})";
}
