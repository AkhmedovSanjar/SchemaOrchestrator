using Microsoft.EntityFrameworkCore;

namespace SchemaOrchestrator.Tests.Providers.Sqlite;

internal sealed class SqlitePeopleNameOnlyContext : DbContext
{
    public SqlitePeopleNameOnlyContext(DbContextOptions<SqlitePeopleNameOnlyContext> options) : base(options)
    {
    }

    public DbSet<PersonNameOnly> People => Set<PersonNameOnly>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PersonNameOnly>(b =>
        {
            b.ToTable("People");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(100);
        });
    }
}

internal sealed class SqlitePeopleWithEmailContext : DbContext
{
    public SqlitePeopleWithEmailContext(DbContextOptions<SqlitePeopleWithEmailContext> options) : base(options)
    {
    }

    public DbSet<PersonWithEmail> People => Set<PersonWithEmail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PersonWithEmail>(b =>
        {
            b.ToTable("People");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(150);
            b.Property(x => x.Email).HasMaxLength(200);
            b.HasIndex(x => x.Name).HasDatabaseName("IX_People_Name");
        });
    }
}

internal sealed class SqliteLegacyTablesContext : DbContext
{
    public SqliteLegacyTablesContext(DbContextOptions<SqliteLegacyTablesContext> options) : base(options)
    {
    }

    public DbSet<LegacyThing> LegacyThings => Set<LegacyThing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LegacyThing>(b =>
        {
            b.ToTable("LegacyThings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Payload).HasMaxLength(100);
        });
    }
}

internal sealed class SqliteNoLegacyTablesContext : DbContext
{
    public SqliteNoLegacyTablesContext(DbContextOptions<SqliteNoLegacyTablesContext> options) : base(options)
    {
    }

    public DbSet<AliveThing> AliveThings => Set<AliveThing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AliveThing>(b =>
        {
            b.ToTable("AliveThings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Payload).HasMaxLength(100);
        });
    }
}

internal sealed class SqliteCodeAsTextContext : DbContext
{
    public SqliteCodeAsTextContext(DbContextOptions<SqliteCodeAsTextContext> options) : base(options)
    {
    }

    public DbSet<CodeAsTextEntity> Items => Set<CodeAsTextEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CodeAsTextEntity>(b =>
        {
            b.ToTable("CodeItems");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasColumnType("TEXT");
        });
    }
}

internal sealed class SqliteCodeAsIntContext : DbContext
{
    public SqliteCodeAsIntContext(DbContextOptions<SqliteCodeAsIntContext> options) : base(options)
    {
    }

    public DbSet<CodeAsIntEntity> Items => Set<CodeAsIntEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CodeAsIntEntity>(b =>
        {
            b.ToTable("CodeItems");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasColumnType("INTEGER");
        });
    }
}

internal sealed class SqliteFkV1Context : DbContext
{
    public SqliteFkV1Context(DbContextOptions<SqliteFkV1Context> options) : base(options)
    {
    }

    public DbSet<FkParentV1> Parents => Set<FkParentV1>();
    public DbSet<FkChildV1> Children => Set<FkChildV1>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FkParentV1>(b =>
        {
            b.ToTable("Parents");
            b.HasKey(x => x.Id);
        });

        modelBuilder.Entity<FkChildV1>(b =>
        {
            b.ToTable("Children");
            b.HasKey(x => x.Id);
            b.HasOne<FkParentV1>()
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .HasConstraintName("FK_Children_Parents")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class SqliteFkV2Context : DbContext
{
    public SqliteFkV2Context(DbContextOptions<SqliteFkV2Context> options) : base(options)
    {
    }

    public DbSet<FkParentV2> Parents => Set<FkParentV2>();
    public DbSet<FkChildV2> Children => Set<FkChildV2>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FkParentV2>(b =>
        {
            b.ToTable("Parents");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnType("BIGINT");
        });

        modelBuilder.Entity<FkChildV2>(b =>
        {
            b.ToTable("Children");
            b.HasKey(x => x.Id);
            b.Property(x => x.ParentId).HasColumnType("BIGINT");
            b.HasOne<FkParentV2>()
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .HasConstraintName("FK_Children_Parents_New")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class PersonNameOnly
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

internal sealed class PersonWithEmail
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
}

internal sealed class LegacyThing
{
    public int Id { get; set; }
    public string Payload { get; set; } = string.Empty;
}

internal sealed class AliveThing
{
    public int Id { get; set; }
    public string Payload { get; set; } = string.Empty;
}

internal sealed class CodeAsTextEntity
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
}

internal sealed class CodeAsIntEntity
{
    public int Id { get; set; }
    public int Code { get; set; }
}

internal sealed class FkParentV1
{
    public int Id { get; set; }
}

internal sealed class FkChildV1
{
    public int Id { get; set; }
    public int ParentId { get; set; }
}

internal sealed class FkParentV2
{
    public long Id { get; set; }
}

internal sealed class FkChildV2
{
    public int Id { get; set; }
    public long ParentId { get; set; }
}
