using Microsoft.EntityFrameworkCore;
using SchemaOrchestrator.Samples.PostgreSql.Models;

namespace SchemaOrchestrator.Samples.PostgreSql.Contexts;

public sealed class SamplePostgreV2Context(DbContextOptions<SamplePostgreV2Context> options) : DbContext(options)
{
    public DbSet<CustomerV2> Customers => Set<CustomerV2>();
    public DbSet<OrderV2> Orders => Set<OrderV2>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerV2>(b =>
        {
            b.ToTable("customers", "public");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(160);
            b.Property(x => x.Email).HasMaxLength(200);
            b.HasIndex(x => x.Name).HasDatabaseName("ix_customers_name");
        });

        modelBuilder.Entity<OrderV2>(b =>
        {
            b.ToTable("orders", "public");
            b.HasKey(x => x.Id);
            b.Property(x => x.Amount);
            b.Property(x => x.Description).HasMaxLength(300);
            b.HasOne<CustomerV2>().WithMany().HasForeignKey(x => x.CustomerId).HasConstraintName("fk_orders_customers_new");
        });
    }
}
