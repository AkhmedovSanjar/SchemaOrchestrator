namespace SchemaOrchestrator.Samples.SqlServer.Models;

public sealed class CustomerV1
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class OrderV1
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public decimal Amount { get; set; }
}
