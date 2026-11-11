using ApexPost.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Api.Data;

/// <summary>
/// API-owned extension tables for recipient metadata and courier scan history.
/// This separate context leaves the Week 3 ApexPostDbContext and its model unchanged.
/// </summary>
public sealed class ApexPostApiDbContext(DbContextOptions<ApexPostApiDbContext> options) : DbContext(options)
{
    public DbSet<ShipmentMetadata> ShipmentMetadata => Set<ShipmentMetadata>();
    public DbSet<ApiScanRecord> ShipmentScans => Set<ApiScanRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShipmentMetadata>(entity =>
        {
            entity.ToTable("ShipmentApiMetadata");
            entity.HasKey(x => x.TrackingNumber);
            entity.Property(x => x.TrackingNumber).HasMaxLength(32).IsUnicode(false);
            entity.Property(x => x.Status).HasMaxLength(32).IsUnicode(false).IsRequired();
            entity.Property(x => x.RecipientName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.RecipientPhone).HasMaxLength(40).IsUnicode(false).IsRequired();
            entity.Property(x => x.RecipientAddress).HasMaxLength(500).IsRequired();
            entity.Property(x => x.RecipientCity).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.RecipientCity);
            entity.HasMany(x => x.Scans).WithOne().HasForeignKey(x => x.TrackingNumber)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiScanRecord>(entity =>
        {
            entity.ToTable("ShipmentApiScans");
            entity.HasKey(x => x.ScanId);
            entity.Property(x => x.ScanId).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.TrackingNumber).HasMaxLength(32).IsUnicode(false).IsRequired();
            entity.Property(x => x.ScanType).HasMaxLength(24).IsUnicode(false).IsRequired();
            entity.Property(x => x.CourierId).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(x => x.Location).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ScannedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.UploadedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasIndex(x => new { x.TrackingNumber, x.ScannedAtUtc });
        });
    }
}
