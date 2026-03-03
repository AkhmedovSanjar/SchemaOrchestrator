using Microsoft.EntityFrameworkCore;
using SchemaOrchestrator.Samples.SqlServer.Models;

namespace SchemaOrchestrator.Samples.SqlServer.Contexts;

public sealed class SampleSqlServerV1Context(DbContextOptions<SampleSqlServerV1Context> options) : DbContext(options)
{
    public DbSet<CustomerV1> Customers => Set<CustomerV1>();
    public DbSet<OrderV1> Orders => Set<OrderV1>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerV1>(b =>
        {
            b.ToTable("Customers", "dbo");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(120);
        });

        modelBuilder.Entity<OrderV1>(b =>
        {
            b.ToTable("Orders", "dbo");
            b.HasKey(x => x.Id);
            b.Property(x => x.Amount);
            b.HasOne<CustomerV1>().WithMany().HasForeignKey(x => x.CustomerId).HasConstraintName("FK_Orders_Customers");
        });
    }
}
