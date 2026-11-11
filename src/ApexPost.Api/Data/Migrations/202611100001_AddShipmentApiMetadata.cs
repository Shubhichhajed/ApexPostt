using ApexPost.Api.Data;
using ApexPost.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ApexPost.Api.Data.Migrations;

[DbContext(typeof(ApexPostApiDbContext))]
[Migration("202611100001_AddShipmentApiMetadata")]
public sealed class AddShipmentApiMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ShipmentApiMetadata",
            columns: table => new
            {
                TrackingNumber = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                RecipientName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                RecipientPhone = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                RecipientAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                RecipientCity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ShipmentApiMetadata", x => x.TrackingNumber));

        migrationBuilder.CreateIndex(name: "IX_ShipmentApiMetadata_Status", table: "ShipmentApiMetadata", column: "Status");
        migrationBuilder.CreateIndex(name: "IX_ShipmentApiMetadata_RecipientCity", table: "ShipmentApiMetadata", column: "RecipientCity");

        migrationBuilder.CreateTable(
            name: "ShipmentApiScans",
            columns: table => new
            {
                ScanId = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                TrackingNumber = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                ScanType = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                CourierId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                Location = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                ScannedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShipmentApiScans", x => x.ScanId);
                table.ForeignKey("FK_ShipmentApiScans_ShipmentApiMetadata_TrackingNumber",
                    x => x.TrackingNumber, "ShipmentApiMetadata", "TrackingNumber", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ShipmentApiScans_TrackingNumber_ScannedAtUtc",
            table: "ShipmentApiScans", columns: new[] { "TrackingNumber", "ScannedAtUtc" });

        // Backfill metadata for shipments created by the Week 3 application before this API extension existed.
        migrationBuilder.Sql("""
            INSERT INTO [ShipmentApiMetadata]
                ([TrackingNumber], [Status], [RecipientName], [RecipientPhone], [RecipientAddress], [RecipientCity], [Notes])
            SELECT s.[Id], 'Created', '', '', '', s.[DestinationZone], NULL
            FROM [Shipments] AS s
            WHERE NOT EXISTS (SELECT 1 FROM [ShipmentApiMetadata] AS m WHERE m.[TrackingNumber] = s.[Id]);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ShipmentApiScans");
        migrationBuilder.DropTable(name: "ShipmentApiMetadata");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
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
