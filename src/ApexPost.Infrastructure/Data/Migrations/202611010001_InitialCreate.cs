using ApexPost.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ApexPost.Infrastructure.Data.Migrations;

[DbContext(typeof(ApexPostDbContext))]
[Migration("202611010001_InitialCreate")]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        CREATE TABLE dbo.Roles (Name varchar(24) NOT NULL CONSTRAINT PK_Roles PRIMARY KEY, CONSTRAINT CK_Roles_Name CHECK(Name IN ('CounterClerk','BranchManager','Auditor','Administrator')));
        INSERT dbo.Roles(Name) VALUES ('CounterClerk'),('BranchManager'),('Auditor'),('Administrator');
        CREATE TABLE dbo.Branches (
         Id varchar(32) NOT NULL CONSTRAINT PK_Branches PRIMARY KEY, Code varchar(12) NOT NULL, Name nvarchar(120) NOT NULL, Region nvarchar(60) NOT NULL,
         OpeningHours varchar(32) NOT NULL CONSTRAINT DF_Branches_OpeningHours DEFAULT '09:00-17:00', Status varchar(16) NOT NULL CONSTRAINT DF_Branches_Status DEFAULT 'Active',
         CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_Branches_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_Branches_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL,
         CONSTRAINT UQ_Branches_Code UNIQUE(Code), CONSTRAINT CK_Branches_Status CHECK(Status IN ('Active','Closed')));
        CREATE TABLE dbo.Counters (
         BranchCode varchar(12) NOT NULL, Id varchar(32) NOT NULL, Name nvarchar(80) NOT NULL, IsActive bit NOT NULL CONSTRAINT DF_Counters_IsActive DEFAULT 1,
         CONSTRAINT PK_Counters PRIMARY KEY(BranchCode,Id), CONSTRAINT FK_Counters_Branches FOREIGN KEY(BranchCode) REFERENCES dbo.Branches(Code) ON DELETE NO ACTION);
        CREATE TABLE dbo.Staff (
         Id varchar(32) NOT NULL CONSTRAINT PK_Staff PRIMARY KEY, Name nvarchar(120) NOT NULL, Email varchar(254) NOT NULL, Role varchar(24) NOT NULL,
         HomeBranchCode varchar(12) NOT NULL, Status varchar(16) NOT NULL CONSTRAINT DF_Staff_Status DEFAULT 'Active', PasswordHash varchar(256) NOT NULL,
         CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_Staff_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_Staff_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL,
         CONSTRAINT UQ_Staff_Email UNIQUE(Email), CONSTRAINT CK_Staff_Role CHECK(Role IN ('CounterClerk','BranchManager','Auditor','Administrator')), CONSTRAINT CK_Staff_Status CHECK(Status IN ('Active','Inactive')),
         CONSTRAINT FK_Staff_Roles FOREIGN KEY(Role) REFERENCES dbo.Roles(Name) ON DELETE NO ACTION, CONSTRAINT FK_Staff_Branches FOREIGN KEY(HomeBranchCode) REFERENCES dbo.Branches(Code) ON DELETE NO ACTION);
        CREATE TABLE dbo.ProductCategories (Name varchar(32) NOT NULL CONSTRAINT PK_ProductCategories PRIMARY KEY, CONSTRAINT CK_ProductCategories_Name CHECK(Name IN ('Packaging','Stationery','OfficeSupplies')));
        INSERT dbo.ProductCategories(Name) VALUES ('Packaging'),('Stationery'),('OfficeSupplies');
        CREATE TABLE dbo.Products (
         Id varchar(32) NOT NULL CONSTRAINT PK_Products PRIMARY KEY, Sku varchar(32) NOT NULL, Name nvarchar(160) NOT NULL, Category varchar(32) NOT NULL,
         UnitPrice decimal(18,2) NOT NULL, TaxCategory varchar(16) NOT NULL, IsActive bit NOT NULL CONSTRAINT DF_Products_IsActive DEFAULT 1,
         CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_Products_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_Products_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL,
         CONSTRAINT UQ_Products_Sku UNIQUE(Sku), CONSTRAINT CK_Products_UnitPrice CHECK(UnitPrice >= 0), CONSTRAINT CK_Products_TaxCategory CHECK(TaxCategory IN ('Standard','Reduced','ZeroRated')), CONSTRAINT FK_Products_Categories FOREIGN KEY(Category) REFERENCES dbo.ProductCategories(Name) ON DELETE NO ACTION);
        CREATE TABLE dbo.BranchStock (
         Id varchar(32) NOT NULL CONSTRAINT PK_BranchStock PRIMARY KEY, BranchCode varchar(12) NOT NULL, Sku varchar(32) NOT NULL, Quantity int NOT NULL, UpdatedAt datetimeoffset(7) NOT NULL,
         RowVersion rowversion NOT NULL, CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_BranchStock_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_BranchStock_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL,
         CONSTRAINT UQ_BranchStock_Branch_Sku UNIQUE(BranchCode,Sku), CONSTRAINT CK_BranchStock_Quantity CHECK(Quantity >= 0),
         CONSTRAINT FK_BranchStock_Branches FOREIGN KEY(BranchCode) REFERENCES dbo.Branches(Code) ON DELETE NO ACTION, CONSTRAINT FK_BranchStock_Products FOREIGN KEY(Sku) REFERENCES dbo.Products(Sku) ON DELETE NO ACTION);
        CREATE TABLE dbo.StockAdjustments (
         Id varchar(32) NOT NULL CONSTRAINT PK_StockAdjustments PRIMARY KEY, BranchCode varchar(12) NOT NULL, Sku varchar(32) NOT NULL, Delta int NOT NULL, Reason nvarchar(250) NOT NULL, ChangedBy varchar(32) NOT NULL, ChangedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_StockAdjustments_ChangedAt DEFAULT SYSUTCDATETIME(),
         CONSTRAINT CK_StockAdjustments_Delta CHECK(Delta <> 0), CONSTRAINT FK_StockAdjustments_Branches FOREIGN KEY(BranchCode) REFERENCES dbo.Branches(Code) ON DELETE NO ACTION,
         CONSTRAINT FK_StockAdjustments_Products FOREIGN KEY(Sku) REFERENCES dbo.Products(Sku) ON DELETE NO ACTION, CONSTRAINT FK_StockAdjustments_Staff FOREIGN KEY(ChangedBy) REFERENCES dbo.Staff(Id) ON DELETE NO ACTION);
        CREATE TABLE dbo.ShippingZones (Code varchar(16) NOT NULL CONSTRAINT PK_ShippingZones PRIMARY KEY, Region nvarchar(60) NOT NULL);
        INSERT dbo.ShippingZones(Code,Region) VALUES ('ZONE-01','Central'),('ZONE-02','Central'),('ZONE-03','North'),('ZONE-04','North'),('ZONE-05','West'),('ZONE-06','West'),('ZONE-07','South'),('ZONE-08','South'),('ZONE-09','East'),('ZONE-10','East');
        CREATE TABLE dbo.ShippingRates (
         Zone varchar(16) NOT NULL, ServiceLevel varchar(16) NOT NULL, BaseRate decimal(18,2) NOT NULL, PerKgRate decimal(18,2) NOT NULL, MaximumWeightKg decimal(9,3) NOT NULL,
         CONSTRAINT PK_ShippingRates PRIMARY KEY(Zone,ServiceLevel), CONSTRAINT CK_ShippingRates_Rates CHECK(BaseRate >= 0 AND PerKgRate >= 0 AND MaximumWeightKg > 0),
         CONSTRAINT CK_ShippingRates_Level CHECK(ServiceLevel IN ('Standard','Express','Registered')), CONSTRAINT FK_ShippingRates_Zones FOREIGN KEY(Zone) REFERENCES dbo.ShippingZones(Code) ON DELETE NO ACTION);
        CREATE TABLE dbo.CounterTransactions (
         Id varchar(32) NOT NULL CONSTRAINT PK_CounterTransactions PRIMARY KEY, BranchCode varchar(12) NOT NULL, CounterId varchar(32) NOT NULL, ClerkId varchar(32) NOT NULL,
         Subtotal decimal(18,2) NOT NULL, Tax decimal(18,2) NOT NULL, Total decimal(18,2) NOT NULL, PaymentMethod varchar(16) NULL, Status varchar(16) NOT NULL,
         Reason nvarchar(300) NULL, CreatedAt datetimeoffset(7) NOT NULL, CompletedAt datetimeoffset(7) NULL, RowVersion rowversion NOT NULL,
         CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_CounterTransactions_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_CounterTransactions_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL,
         CONSTRAINT CK_CounterTransactions_Status CHECK(Status IN ('Open','Paid','Voided','Refunded')), CONSTRAINT CK_CounterTransactions_PaymentMethod CHECK(PaymentMethod IS NULL OR PaymentMethod IN ('Cash','Card','Upi','MoneyOrder')), CONSTRAINT CK_CounterTransactions_Totals CHECK(Subtotal >= 0 AND Tax >= 0 AND Total >= 0),
         CONSTRAINT FK_CounterTransactions_Branch FOREIGN KEY(BranchCode) REFERENCES dbo.Branches(Code) ON DELETE NO ACTION, CONSTRAINT FK_CounterTransactions_Counter FOREIGN KEY(BranchCode,CounterId) REFERENCES dbo.Counters(BranchCode,Id) ON DELETE NO ACTION,
         CONSTRAINT FK_CounterTransactions_Clerk FOREIGN KEY(ClerkId) REFERENCES dbo.Staff(Id) ON DELETE NO ACTION);
        CREATE TABLE dbo.TransactionLines (
         CounterTransactionId varchar(32) NOT NULL, Id varchar(32) NOT NULL, Kind varchar(20) NOT NULL, Description nvarchar(240) NOT NULL, Sku varchar(32) NULL,
         Quantity int NOT NULL, UnitPrice decimal(18,2) NOT NULL, TaxRate decimal(6,4) NOT NULL, LineTotal decimal(18,2) NOT NULL,
         CONSTRAINT PK_TransactionLines PRIMARY KEY(CounterTransactionId,Id), CONSTRAINT CK_TransactionLines_Values CHECK(Quantity > 0 AND UnitPrice >= 0 AND TaxRate BETWEEN 0 AND 1 AND LineTotal >= 0), CONSTRAINT CK_TransactionLines_Kind CHECK(Kind IN ('RetailProduct','Shipping','MoneyOrder','OtherService')),
         CONSTRAINT FK_TransactionLines_Transactions FOREIGN KEY(CounterTransactionId) REFERENCES dbo.CounterTransactions(Id) ON DELETE NO ACTION,
         CONSTRAINT FK_TransactionLines_Products FOREIGN KEY(Sku) REFERENCES dbo.Products(Sku) ON DELETE NO ACTION);
        CREATE TABLE dbo.Payments (
         Id varchar(32) NOT NULL CONSTRAINT PK_Payments PRIMARY KEY, CounterTransactionId varchar(32) NOT NULL, Method varchar(16) NOT NULL, Amount decimal(18,2) NOT NULL, PaidAtUtc datetimeoffset(7) NOT NULL,
         CONSTRAINT UQ_Payments_Transaction UNIQUE(CounterTransactionId), CONSTRAINT CK_Payments_Amount CHECK(Amount > 0), CONSTRAINT CK_Payments_Method CHECK(Method IN ('Cash','Card','Upi','MoneyOrder')),
         CONSTRAINT FK_Payments_Transactions FOREIGN KEY(CounterTransactionId) REFERENCES dbo.CounterTransactions(Id) ON DELETE NO ACTION);
        CREATE TABLE dbo.Shipments (
         Id varchar(32) NOT NULL CONSTRAINT PK_Shipments PRIMARY KEY, BranchCode varchar(12) NOT NULL, Kind varchar(12) NOT NULL, WeightKg decimal(9,3) NOT NULL,
         LengthCm decimal(9,2) NOT NULL, WidthCm decimal(9,2) NOT NULL, HeightCm decimal(9,2) NOT NULL, DestinationZone varchar(16) NOT NULL, ServiceLevel varchar(16) NOT NULL,
         Price decimal(18,2) NOT NULL, CreatedAt datetimeoffset(7) NOT NULL, CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_Shipments_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_Shipments_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL,
         CONSTRAINT CK_Shipments_Kind CHECK(Kind IN ('Parcel','Letter')), CONSTRAINT CK_Shipments_Weight CHECK(WeightKg > 0 AND WeightKg <= 70), CONSTRAINT CK_Shipments_Dimensions CHECK(LengthCm > 0 AND WidthCm > 0 AND HeightCm > 0),
         CONSTRAINT FK_Shipments_Branch FOREIGN KEY(BranchCode) REFERENCES dbo.Branches(Code) ON DELETE NO ACTION, CONSTRAINT FK_Shipments_Rate FOREIGN KEY(DestinationZone,ServiceLevel) REFERENCES dbo.ShippingRates(Zone,ServiceLevel) ON DELETE NO ACTION);
        CREATE TABLE dbo.IdentityVerifications (
         Id varchar(32) NOT NULL CONSTRAINT PK_IdentityVerifications PRIMARY KEY, BranchCode varchar(12) NOT NULL, CounterTransactionId varchar(32) NOT NULL, VerifiedByStaffId varchar(32) NOT NULL,
         DocumentType int NOT NULL, MaskedDocumentNumber varchar(8) NOT NULL, Result int NOT NULL, VerifiedAt datetimeoffset(7) NOT NULL,
         CONSTRAINT CK_IdentityVerifications_MaskedDocument CHECK(LEN(MaskedDocumentNumber) BETWEEN 4 AND 8 AND LEFT(MaskedDocumentNumber,4)='****'), CONSTRAINT CK_IdentityVerifications_DocumentType CHECK(DocumentType BETWEEN 0 AND 3), CONSTRAINT CK_IdentityVerifications_Result CHECK(Result BETWEEN 0 AND 2),
         CONSTRAINT FK_IdentityVerifications_Branch FOREIGN KEY(BranchCode) REFERENCES dbo.Branches(Code) ON DELETE NO ACTION,
         CONSTRAINT FK_IdentityVerifications_Transaction FOREIGN KEY(CounterTransactionId) REFERENCES dbo.CounterTransactions(Id) ON DELETE NO ACTION,
         CONSTRAINT FK_IdentityVerifications_Staff FOREIGN KEY(VerifiedByStaffId) REFERENCES dbo.Staff(Id) ON DELETE NO ACTION);
        CREATE TABLE dbo.MoneyOrders (
         Id varchar(32) NOT NULL CONSTRAINT PK_MoneyOrders PRIMARY KEY, Amount decimal(18,2) NOT NULL, Sender nvarchar(120) NOT NULL, Recipient nvarchar(120) NOT NULL, Fee decimal(18,2) NOT NULL,
         BranchCode varchar(12) NOT NULL, CounterTransactionId varchar(32) NOT NULL, Status varchar(20) NOT NULL, IdentityVerificationId varchar(32) NULL, CreatedAt datetimeoffset(7) NOT NULL,
         CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_MoneyOrders_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_MoneyOrders_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL,
         CONSTRAINT CK_MoneyOrders_Amount CHECK(Amount > 0 AND Fee >= 0), CONSTRAINT CK_MoneyOrders_Status CHECK(Status IN ('PendingPayment','Issued','Cancelled')),
         CONSTRAINT FK_MoneyOrders_Branch FOREIGN KEY(BranchCode) REFERENCES dbo.Branches(Code) ON DELETE NO ACTION, CONSTRAINT FK_MoneyOrders_Transaction FOREIGN KEY(CounterTransactionId) REFERENCES dbo.CounterTransactions(Id) ON DELETE NO ACTION,
         CONSTRAINT FK_MoneyOrders_Verification FOREIGN KEY(IdentityVerificationId) REFERENCES dbo.IdentityVerifications(Id) ON DELETE NO ACTION);
        CREATE TABLE dbo.AuditEntries (
         Id varchar(32) NOT NULL CONSTRAINT PK_AuditEntries PRIMARY KEY, StaffId varchar(32) NOT NULL, Role varchar(24) NOT NULL, BranchCode varchar(12) NOT NULL,
         Controller varchar(80) NOT NULL, Action varchar(80) NOT NULL, RouteValues nvarchar(1000) NOT NULL, StatusCode int NOT NULL, DurationMs bigint NOT NULL,
         CorrelationId varchar(64) NOT NULL, Timestamp datetimeoffset(7) NOT NULL, Outcome varchar(16) NOT NULL);
        
        /* Standard audit columns on lookup, owned and operational tables as well as aggregates. */
        ALTER TABLE dbo.Roles ADD CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_Roles_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_Roles_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL;
        ALTER TABLE dbo.Counters ADD CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_Counters_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_Counters_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL;
        ALTER TABLE dbo.ProductCategories ADD CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_ProductCategories_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_ProductCategories_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL;
        ALTER TABLE dbo.ShippingZones ADD CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_ShippingZones_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_ShippingZones_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL;
        ALTER TABLE dbo.ShippingRates ADD CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_ShippingRates_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_ShippingRates_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL;
        ALTER TABLE dbo.TransactionLines ADD CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_TransactionLines_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_TransactionLines_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL;
        ALTER TABLE dbo.Payments ADD CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_Payments_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_Payments_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL;
        ALTER TABLE dbo.IdentityVerifications ADD CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_IdentityVerifications_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_IdentityVerifications_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL;
        ALTER TABLE dbo.AuditEntries ADD CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_AuditEntries_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_AuditEntries_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL;
        ALTER TABLE dbo.StockAdjustments ADD CreatedAtUtc datetimeoffset(7) NOT NULL CONSTRAINT DF_StockAdjustments_CreatedAt DEFAULT SYSUTCDATETIME(), CreatedBy varchar(64) NOT NULL CONSTRAINT DF_StockAdjustments_CreatedBy DEFAULT 'system', ModifiedAtUtc datetimeoffset(7) NULL;
        """);
            }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS dbo.MoneyOrders; DROP TABLE IF EXISTS dbo.IdentityVerifications; DROP TABLE IF EXISTS dbo.AuditEntries; DROP TABLE IF EXISTS dbo.Payments; DROP TABLE IF EXISTS dbo.TransactionLines; DROP TABLE IF EXISTS dbo.CounterTransactions; DROP TABLE IF EXISTS dbo.Shipments; DROP TABLE IF EXISTS dbo.ShippingRates; DROP TABLE IF EXISTS dbo.ShippingZones; DROP TABLE IF EXISTS dbo.StockAdjustments; DROP TABLE IF EXISTS dbo.BranchStock; DROP TABLE IF EXISTS dbo.Products; DROP TABLE IF EXISTS dbo.ProductCategories; DROP TABLE IF EXISTS dbo.Staff; DROP TABLE IF EXISTS dbo.Counters; DROP TABLE IF EXISTS dbo.Branches; DROP TABLE IF EXISTS dbo.Roles;");
    }
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");
        ApexPostDbContext.ConfigureModel(modelBuilder);
    }
}
