using Microsoft.EntityFrameworkCore;

namespace SchemaOrchestrator.Tests.Providers.SqlServer;

internal sealed class SqlServerPeopleV1Context : DbContext
{
    public SqlServerPeopleV1Context(DbContextOptions<SqlServerPeopleV1Context> options) : base(options) { }
    public DbSet<SqlServerPersonV1> People => Set<SqlServerPersonV1>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SqlServerPersonV1>(b =>
        {
            b.ToTable("People");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(100);
        });
    }
}

internal sealed class SqlServerPeopleV2Context : DbContext
{
    public SqlServerPeopleV2Context(DbContextOptions<SqlServerPeopleV2Context> options) : base(options) { }
    public DbSet<SqlServerPersonV2> People => Set<SqlServerPersonV2>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SqlServerPersonV2>(b =>
        {
            b.ToTable("People");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(150);
            b.Property(x => x.Email).HasMaxLength(200);
            b.HasIndex(x => x.Name).HasDatabaseName("IX_People_Name");
        });
    }
}

internal sealed class SqlServerLegacyTablesContext : DbContext
{
    public SqlServerLegacyTablesContext(DbContextOptions<SqlServerLegacyTablesContext> options) : base(options) { }
    public DbSet<SqlServerLegacyThing> LegacyThings => Set<SqlServerLegacyThing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SqlServerLegacyThing>(b =>
        {
            b.ToTable("LegacyThings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Payload).HasMaxLength(100);
        });
    }
}

internal sealed class SqlServerNoLegacyTablesContext : DbContext
{
    public SqlServerNoLegacyTablesContext(DbContextOptions<SqlServerNoLegacyTablesContext> options) : base(options) { }
    public DbSet<SqlServerAliveThing> AliveThings => Set<SqlServerAliveThing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SqlServerAliveThing>(b =>
        {
            b.ToTable("AliveThings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Payload).HasMaxLength(100);
        });
    }
}

internal sealed class SqlServerFkV1Context : DbContext
{
    public SqlServerFkV1Context(DbContextOptions<SqlServerFkV1Context> options) : base(options) { }
    public DbSet<SqlServerFkParentV1> Parents => Set<SqlServerFkParentV1>();
    public DbSet<SqlServerFkChildV1> Children => Set<SqlServerFkChildV1>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SqlServerFkParentV1>(b =>
        {
            b.ToTable("Parents");
            b.HasKey(x => x.Id);
        });

        modelBuilder.Entity<SqlServerFkChildV1>(b =>
        {
            b.ToTable("Children");
            b.HasKey(x => x.Id);
            b.HasOne<SqlServerFkParentV1>()
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .HasConstraintName("FK_Children_Parents")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class SqlServerFkV2Context : DbContext
{
    public SqlServerFkV2Context(DbContextOptions<SqlServerFkV2Context> options) : base(options) { }
    public DbSet<SqlServerFkParentV2> Parents => Set<SqlServerFkParentV2>();
    public DbSet<SqlServerFkChildV2> Children => Set<SqlServerFkChildV2>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SqlServerFkParentV2>(b =>
        {
            b.ToTable("Parents");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnType("bigint");
        });

        modelBuilder.Entity<SqlServerFkChildV2>(b =>
        {
            b.ToTable("Children");
            b.HasKey(x => x.Id);
            b.Property(x => x.ParentId).HasColumnType("bigint");
            b.HasOne<SqlServerFkParentV2>()
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .HasConstraintName("FK_Children_Parents_New")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class SqlServerPersonV1
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

internal sealed class SqlServerPersonV2
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
}

internal sealed class SqlServerLegacyThing
{
    public int Id { get; set; }
    public string Payload { get; set; } = string.Empty;
}

internal sealed class SqlServerAliveThing
{
    public int Id { get; set; }
    public string Payload { get; set; } = string.Empty;
}

internal sealed class SqlServerFkParentV1
{
    public int Id { get; set; }
}

internal sealed class SqlServerFkChildV1
{
    public int Id { get; set; }
    public int ParentId { get; set; }
}

internal sealed class SqlServerFkParentV2
{
    public long Id { get; set; }
}

internal sealed class SqlServerFkChildV2
{
    public int Id { get; set; }
    public long ParentId { get; set; }
}

internal sealed class SqlServerConstraintV1Context : DbContext
{
    public SqlServerConstraintV1Context(DbContextOptions<SqlServerConstraintV1Context> options) : base(options) { }
    public DbSet<SqlServerConstraintEntityV1> Items => Set<SqlServerConstraintEntityV1>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SqlServerConstraintEntityV1>(b =>
        {
            b.ToTable("ConstraintItems");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(50);
            b.Property(x => x.Rank).HasDefaultValue(1);
            b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_ConstraintItems_Code");
            b.ToTable(t => t.HasCheckConstraint("CK_ConstraintItems_Code", "LEN([Code]) > 0"));
        });
    }
}

internal sealed class SqlServerConstraintV2Context : DbContext
{
    public SqlServerConstraintV2Context(DbContextOptions<SqlServerConstraintV2Context> options) : base(options) { }
    public DbSet<SqlServerConstraintEntityV2> Items => Set<SqlServerConstraintEntityV2>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SqlServerConstraintEntityV2>(b =>
        {
            b.ToTable("ConstraintItems");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(80);
            b.Property(x => x.Rank).HasDefaultValue(2);
            b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_ConstraintItems_Code_V2");
            b.ToTable(t => t.HasCheckConstraint("CK_ConstraintItems_Code_V2", "LEN([Code]) >= 1"));
        });
    }
}

internal sealed class SqlServerLargeSchemaV1Context : DbContext
{
    public SqlServerLargeSchemaV1Context(DbContextOptions<SqlServerLargeSchemaV1Context> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        for (var i = 1; i <= 12; i++)
        {
            var entity = modelBuilder.SharedTypeEntity<Dictionary<string, object>>($"SqlServerLargeV1_{i}");
            entity.ToTable($"LargeTable{i}");
            entity.Property<int>("Id");
            entity.HasKey("Id");
            entity.Property<string>("Name").HasMaxLength(100);
        }
    }
}

internal sealed class SqlServerLargeSchemaV2Context : DbContext
{
    public SqlServerLargeSchemaV2Context(DbContextOptions<SqlServerLargeSchemaV2Context> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        for (var i = 1; i <= 12; i++)
        {
            var entity = modelBuilder.SharedTypeEntity<Dictionary<string, object>>($"SqlServerLargeV2_{i}");
            entity.ToTable($"LargeTable{i}");
            entity.Property<int>("Id");
            entity.HasKey("Id");
            entity.Property<string>("Name").HasMaxLength(120);
            entity.Property<string>("Extra").HasMaxLength(40);
            entity.HasIndex("Name").HasDatabaseName($"IX_LargeTable{i}_Name");
        }
    }
}

internal sealed class SqlServerConstraintEntityV1
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Rank { get; set; }
}

internal sealed class SqlServerConstraintEntityV2
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Rank { get; set; }
}

