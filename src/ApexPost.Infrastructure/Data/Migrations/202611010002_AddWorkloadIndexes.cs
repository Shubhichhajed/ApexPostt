using ApexPost.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ApexPost.Infrastructure.Data.Migrations;

[DbContext(typeof(ApexPostDbContext))]
[Migration("202611010002_AddWorkloadIndexes")]
public sealed class AddWorkloadIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        CREATE INDEX IX_BranchStock_Branch_Quantity ON dbo.BranchStock(BranchCode, Quantity) INCLUDE(Sku);
        CREATE INDEX IX_CounterTransactions_Branch_Status_CompletedAt ON dbo.CounterTransactions(BranchCode, Status, CompletedAt) INCLUDE(Total);
        CREATE INDEX IX_Shipments_Zone_Service_CreatedAt ON dbo.Shipments(DestinationZone, ServiceLevel, CreatedAt DESC) INCLUDE(BranchCode, WeightKg, Price);
        CREATE INDEX IX_MoneyOrders_Branch_CreatedAt ON dbo.MoneyOrders(BranchCode, CreatedAt DESC) INCLUDE(Amount, Status, CounterTransactionId);
        CREATE INDEX IX_IdentityVerifications_Branch_VerifiedAt ON dbo.IdentityVerifications(BranchCode, VerifiedAt DESC) INCLUDE(Result, CounterTransactionId);
        CREATE INDEX IX_CounterTransactions_OpenBranch ON dbo.CounterTransactions(BranchCode, CreatedAt) WHERE Status = 'Open';
        CREATE INDEX IX_StockAdjustments_Branch_ChangedAt ON dbo.StockAdjustments(BranchCode, ChangedAtUtc DESC) INCLUDE(Sku, Delta, ChangedBy);
        """);
            }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS IX_StockAdjustments_Branch_ChangedAt ON dbo.StockAdjustments; DROP INDEX IF EXISTS IX_CounterTransactions_OpenBranch ON dbo.CounterTransactions; DROP INDEX IF EXISTS IX_IdentityVerifications_Branch_VerifiedAt ON dbo.IdentityVerifications; DROP INDEX IF EXISTS IX_MoneyOrders_Branch_CreatedAt ON dbo.MoneyOrders; DROP INDEX IF EXISTS IX_Shipments_Zone_Service_CreatedAt ON dbo.Shipments; DROP INDEX IF EXISTS IX_CounterTransactions_Branch_Status_CompletedAt ON dbo.CounterTransactions; DROP INDEX IF EXISTS IX_BranchStock_Branch_Quantity ON dbo.BranchStock;");
    }
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");
        ApexPostDbContext.ConfigureModel(modelBuilder);
    }
}
