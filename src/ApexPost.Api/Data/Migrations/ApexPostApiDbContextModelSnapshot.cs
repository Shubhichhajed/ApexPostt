using ApexPost.Api.Data;
using ApexPost.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ApexPost.Api.Data.Migrations;

[DbContext(typeof(ApexPostApiDbContext))]
public sealed class ApexPostApiDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.14");
        modelBuilder.Entity<ShipmentMetadata>(entity =>
        {
            entity.Property(x => x.TrackingNumber).HasMaxLength(32).IsUnicode(false);
            entity.Property(x => x.Status).HasMaxLength(32).IsUnicode(false).IsRequired();
            entity.Property(x => x.RecipientName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.RecipientPhone).HasMaxLength(40).IsUnicode(false).IsRequired();
            entity.Property(x => x.RecipientAddress).HasMaxLength(500).IsRequired();
            entity.Property(x => x.RecipientCity).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasKey(x => x.TrackingNumber);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.RecipientCity);
            entity.HasMany(x => x.Scans).WithOne().HasForeignKey(x => x.TrackingNumber).OnDelete(DeleteBehavior.Cascade);
            entity.ToTable("ShipmentApiMetadata");
        });
        modelBuilder.Entity<ApiScanRecord>(entity =>
        {
            entity.Property(x => x.ScanId).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.TrackingNumber).HasMaxLength(32).IsUnicode(false).IsRequired();
            entity.Property(x => x.ScanType).HasMaxLength(24).IsUnicode(false).IsRequired();
            entity.Property(x => x.CourierId).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(x => x.Location).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ScannedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.UploadedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasKey(x => x.ScanId);
            entity.HasIndex(x => new { x.TrackingNumber, x.ScannedAtUtc });
            entity.ToTable("ShipmentApiScans");
        });
    }
}
