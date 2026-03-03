namespace SchemaOrchestrator.Samples.PostgreSql.Models;

public sealed class CustomerV2
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
}

public sealed class OrderV2
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
}
