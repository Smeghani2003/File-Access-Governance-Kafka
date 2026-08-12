using Microsoft.EntityFrameworkCore;

namespace FileAccessGovernance.QueryApi.Data;

/// <summary>
/// Read-mostly mapping onto the schema created by /db/migrations/001_initial_schema.sql.
/// The Query API never writes FsObjects/SecurityDescriptors — only the Ingestion
/// Consumer's stored procedure does (see design doc §5.1) — so this context is used
/// read-only in practice, but isn't marked no-tracking globally to keep it simple
/// for a junior engineer to extend later.
/// </summary>
public sealed class FileAccessGovernanceDbContext : DbContext
{
    public FileAccessGovernanceDbContext(DbContextOptions<FileAccessGovernanceDbContext> options)
        : base(options)
    {
    }

    public DbSet<FsObject> FsObjects => Set<FsObject>();
    public DbSet<SecurityDescriptor> SecurityDescriptors => Set<SecurityDescriptor>();
    public DbSet<SecurityDescriptorAce> SecurityDescriptorAces => Set<SecurityDescriptorAce>();
    public DbSet<SidNameCacheEntry> SidNameCache => Set<SidNameCacheEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FsObject>(e =>
        {
            e.ToTable("FsObjects", "dbo");
            e.HasKey(x => x.ObjectId);
            e.Property(x => x.PathHash).HasColumnType("binary(32)");
            e.Property(x => x.ParentPathHash).HasColumnType("binary(32)");
            e.Property(x => x.FullPath).HasMaxLength(4000);
            e.Property(x => x.ShareName).HasMaxLength(256);
            e.HasIndex(x => x.PathHash).IsUnique();
        });

        modelBuilder.Entity<SecurityDescriptor>(e =>
        {
            e.ToTable("SecurityDescriptors", "dbo");
            e.HasKey(x => x.DescriptorId);
            e.Property(x => x.DescriptorHash).HasMaxLength(64).IsFixedLength();
            e.Property(x => x.OwnerSid).HasMaxLength(184);
            e.HasIndex(x => x.DescriptorHash).IsUnique();
        });

        modelBuilder.Entity<SecurityDescriptorAce>(e =>
        {
            e.ToTable("SecurityDescriptorAces", "dbo");
            e.HasKey(x => x.AceId);
            e.Property(x => x.TrusteeSid).HasMaxLength(184);
            e.HasIndex(x => x.DescriptorId);
        });

        modelBuilder.Entity<SidNameCacheEntry>(e =>
        {
            e.ToTable("SidNameCache", "dbo");
            e.HasKey(x => x.Sid);
            e.Property(x => x.Sid).HasMaxLength(184);
            e.Property(x => x.DisplayName).HasMaxLength(256);
        });
    }
}
