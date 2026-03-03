using Microsoft.EntityFrameworkCore;
using SchemaOrchestrator.Samples.SqlServer.Models;

namespace SchemaOrchestrator.Samples.SqlServer.Contexts;

public sealed class SampleSqlServerV2Context(DbContextOptions<SampleSqlServerV2Context> options) : DbContext(options)
{
    public DbSet<CustomerV2> Customers => Set<CustomerV2>();
    public DbSet<OrderV2> Orders => Set<OrderV2>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerV2>(b =>
        {
            b.ToTable("Customers", "dbo");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(160);
            b.Property(x => x.Email).HasMaxLength(200);
            b.HasIndex(x => x.Name).HasDatabaseName("IX_Customers_Name");
        });

        modelBuilder.Entity<OrderV2>(b =>
        {
            b.ToTable("Orders", "dbo");
            b.HasKey(x => x.Id);
            b.Property(x => x.Amount);
            b.Property(x => x.Description).HasMaxLength(300);
            b.HasOne<CustomerV2>().WithMany().HasForeignKey(x => x.CustomerId).HasConstraintName("FK_Orders_Customers_New");
        });
    }
}
