using Microsoft.EntityFrameworkCore;

namespace SchemaOrchestrator.Tests.Providers.PostgreSql;

internal sealed class PostgrePeopleV1Context : DbContext
{
    public PostgrePeopleV1Context(DbContextOptions<PostgrePeopleV1Context> options) : base(options) { }
    public DbSet<PostgrePersonV1> People => Set<PostgrePersonV1>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PostgrePersonV1>(b =>
        {
            b.ToTable("people", "public");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(100);
        });
    }
}

internal sealed class PostgrePeopleV2Context : DbContext
{
    public PostgrePeopleV2Context(DbContextOptions<PostgrePeopleV2Context> options) : base(options) { }
    public DbSet<PostgrePersonV2> People => Set<PostgrePersonV2>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PostgrePersonV2>(b =>
        {
            b.ToTable("people", "public");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(150);
            b.Property(x => x.Email).HasMaxLength(200);
            b.HasIndex(x => x.Name).HasDatabaseName("ix_people_name");
        });
    }
}

internal sealed class PostgreLegacyTablesContext : DbContext
{
    public PostgreLegacyTablesContext(DbContextOptions<PostgreLegacyTablesContext> options) : base(options) { }
    public DbSet<PostgreLegacyThing> LegacyThings => Set<PostgreLegacyThing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PostgreLegacyThing>(b =>
        {
            b.ToTable("legacy_things", "public");
            b.HasKey(x => x.Id);
            b.Property(x => x.Payload).HasMaxLength(100);
        });
    }
}

internal sealed class PostgreNoLegacyTablesContext : DbContext
{
    public PostgreNoLegacyTablesContext(DbContextOptions<PostgreNoLegacyTablesContext> options) : base(options) { }
    public DbSet<PostgreAliveThing> AliveThings => Set<PostgreAliveThing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PostgreAliveThing>(b =>
        {
            b.ToTable("alive_things", "public");
            b.HasKey(x => x.Id);
            b.Property(x => x.Payload).HasMaxLength(100);
        });
    }
}

internal sealed class PostgreFkV1Context : DbContext
{
    public PostgreFkV1Context(DbContextOptions<PostgreFkV1Context> options) : base(options) { }
    public DbSet<PostgreFkParentV1> Parents => Set<PostgreFkParentV1>();
    public DbSet<PostgreFkChildV1> Children => Set<PostgreFkChildV1>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PostgreFkParentV1>(b =>
        {
            b.ToTable("parents", "public");
            b.HasKey(x => x.Id);
        });

        modelBuilder.Entity<PostgreFkChildV1>(b =>
        {
            b.ToTable("children", "public");
            b.HasKey(x => x.Id);
            b.HasOne<PostgreFkParentV1>()
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .HasConstraintName("fk_children_parents")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class PostgreFkV2Context : DbContext
{
    public PostgreFkV2Context(DbContextOptions<PostgreFkV2Context> options) : base(options) { }
    public DbSet<PostgreFkParentV2> Parents => Set<PostgreFkParentV2>();
    public DbSet<PostgreFkChildV2> Children => Set<PostgreFkChildV2>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PostgreFkParentV2>(b =>
        {
            b.ToTable("parents", "public");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnType("bigint");
        });

        modelBuilder.Entity<PostgreFkChildV2>(b =>
        {
            b.ToTable("children", "public");
            b.HasKey(x => x.Id);
            b.Property(x => x.ParentId).HasColumnType("bigint");
            b.HasOne<PostgreFkParentV2>()
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .HasConstraintName("fk_children_parents_new")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class PostgrePersonV1
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

internal sealed class PostgrePersonV2
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
}

internal sealed class PostgreLegacyThing
{
    public int Id { get; set; }
    public string Payload { get; set; } = string.Empty;
}

internal sealed class PostgreAliveThing
{
    public int Id { get; set; }
    public string Payload { get; set; } = string.Empty;
}

internal sealed class PostgreFkParentV1
{
    public int Id { get; set; }
}

internal sealed class PostgreFkChildV1
{
    public int Id { get; set; }
    public int ParentId { get; set; }
}

internal sealed class PostgreFkParentV2
{
    public long Id { get; set; }
}

internal sealed class PostgreFkChildV2
{
    public int Id { get; set; }
    public long ParentId { get; set; }
}

