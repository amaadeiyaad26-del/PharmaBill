using Microsoft.EntityFrameworkCore;

namespace PharmaBill.CloudSync.Data;

public sealed class CloudDbContext(DbContextOptions<CloudDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<CloudChange> ChangeLog => Set<CloudChange>();
    public DbSet<FileBlob> FileBlobs => Set<FileBlob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("Tenants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });
        modelBuilder.Entity<Branch>(e =>
        {
            e.ToTable("Branches");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.ApiKeyHash).HasMaxLength(128).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        });
        modelBuilder.Entity<Device>(e =>
        {
            e.ToTable("Devices");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.BranchId);
        });
        modelBuilder.Entity<CloudChange>(e =>
        {
            e.ToTable("ChangeLog");
            e.HasKey(x => x.Seq);
            e.Property(x => x.Seq).ValueGeneratedOnAdd();
            e.Property(x => x.Entity).HasMaxLength(100).IsRequired();
            e.Property(x => x.HlcStamp).HasMaxLength(100).IsRequired();
            e.Property(x => x.ServerHlc).HasMaxLength(100).IsRequired();
            e.Property(x => x.SharedBranchIds).HasMaxLength(2000);
            e.HasIndex(x => new { x.TenantId, x.ChangeId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ServerMs, x.ServerCounter }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.BranchId });
        });
        modelBuilder.Entity<FileBlob>(e =>
        {
            e.ToTable("FileBlobs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.Sha256 }).IsUnique();
        });
    }
}
