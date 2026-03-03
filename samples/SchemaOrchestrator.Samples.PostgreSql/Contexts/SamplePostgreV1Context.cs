using Microsoft.EntityFrameworkCore;
using SchemaOrchestrator.Samples.PostgreSql.Models;

namespace SchemaOrchestrator.Samples.PostgreSql.Contexts;

public sealed class SamplePostgreV1Context(DbContextOptions<SamplePostgreV1Context> options) : DbContext(options)
{
    public DbSet<CustomerV1> Customers => Set<CustomerV1>();
    public DbSet<OrderV1> Orders => Set<OrderV1>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerV1>(b =>
        {
            b.ToTable("customers", "public");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(120);
        });

        modelBuilder.Entity<OrderV1>(b =>
        {
            b.ToTable("orders", "public");
            b.HasKey(x => x.Id);
            b.Property(x => x.Amount);
            b.HasOne<CustomerV1>().WithMany().HasForeignKey(x => x.CustomerId).HasConstraintName("fk_orders_customers");
        });
    }
}
