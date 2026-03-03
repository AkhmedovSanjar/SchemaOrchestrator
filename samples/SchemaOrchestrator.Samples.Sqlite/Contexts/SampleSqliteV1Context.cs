using Microsoft.EntityFrameworkCore;
using SchemaOrchestrator.Samples.Sqlite.Models;

namespace SchemaOrchestrator.Samples.Sqlite.Contexts;

public sealed class SampleSqliteV1Context(DbContextOptions<SampleSqliteV1Context> options) : DbContext(options)
{
    public DbSet<CustomerV1> Customers => Set<CustomerV1>();
    public DbSet<OrderV1> Orders => Set<OrderV1>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerV1>(b =>
        {
            b.ToTable("Customers");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(120);
        });

        modelBuilder.Entity<OrderV1>(b =>
        {
            b.ToTable("Orders");
            b.HasKey(x => x.Id);
            b.Property(x => x.Amount);
            b.HasOne<CustomerV1>().WithMany().HasForeignKey(x => x.CustomerId).HasConstraintName("FK_Orders_Customers");
        });
    }
}
