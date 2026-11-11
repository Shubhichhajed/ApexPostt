using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexPost.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlignApexPostModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // InitialCreate and OfflineSyncOutbox already create the columns and
            // nullability represented by the current EF model. The generated
            // ALTER/ADD operations were redundant; ADD operations failed with
            // duplicate-column errors. Keep only the missing FK indexes here.
migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_ChangedBy",
                table: "StockAdjustments",
                column: "ChangedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Staff_HomeBranchCode",
                table: "Staff",
                column: "HomeBranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_Staff_Role",
                table: "Staff",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_BranchCode",
                table: "Shipments",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_DestinationZone_ServiceLevel",
                table: "Shipments",
                columns: new[] { "DestinationZone", "ServiceLevel" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Category",
                table: "Products",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_MoneyOrders_IdentityVerificationId",
                table: "MoneyOrders",
                column: "IdentityVerificationId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityVerifications_CounterTransactionId",
                table: "IdentityVerifications",
                column: "CounterTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityVerifications_VerifiedByStaffId",
                table: "IdentityVerifications",
                column: "VerifiedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_CounterTransactions_ClerkId",
                table: "CounterTransactions",
                column: "ClerkId");

            migrationBuilder.CreateIndex(
                name: "IX_BranchStock_Sku",
                table: "BranchStock",
                column: "Sku");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove only indexes introduced here. Do not drop columns owned by
            // InitialCreate or OfflineSyncOutbox.
            migrationBuilder.DropIndex(
                name: "IX_StockAdjustments_ChangedBy",
                table: "StockAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_Staff_HomeBranchCode",
                table: "Staff");

            migrationBuilder.DropIndex(
                name: "IX_Staff_Role",
                table: "Staff");

            migrationBuilder.DropIndex(
                name: "IX_Shipments_BranchCode",
                table: "Shipments");

            migrationBuilder.DropIndex(
                name: "IX_Shipments_DestinationZone_ServiceLevel",
                table: "Shipments");

            migrationBuilder.DropIndex(
                name: "IX_Products_Category",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_MoneyOrders_IdentityVerificationId",
                table: "MoneyOrders");

            migrationBuilder.DropIndex(
                name: "IX_IdentityVerifications_CounterTransactionId",
                table: "IdentityVerifications");

            migrationBuilder.DropIndex(
                name: "IX_IdentityVerifications_VerifiedByStaffId",
                table: "IdentityVerifications");

            migrationBuilder.DropIndex(
                name: "IX_CounterTransactions_ClerkId",
                table: "CounterTransactions");

            migrationBuilder.DropIndex(
                name: "IX_BranchStock_Sku",
                table: "BranchStock");
        }
    }
}
