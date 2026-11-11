using ApexPost.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexPost.Infrastructure.Data.Configurations;

internal static class MappingConventions
{
    public static void ConfigureId<TEntity>(EntityTypeBuilder<TEntity> b) where TEntity : class, IEntity
        => b.HasKey(x => x.Id);

    public static void ConfigureAudit<TEntity>(EntityTypeBuilder<TEntity> b) where TEntity : class
    {
        b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("datetimeoffset").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property<string>("CreatedBy").HasMaxLength(64).IsUnicode(false).HasDefaultValue("system");
        b.Property<DateTimeOffset?>("ModifiedAtUtc").HasColumnType("datetimeoffset");
    }

    public static void ConfigureAudit<TOwner, TDependent>(OwnedNavigationBuilder<TOwner, TDependent> b)
        where TOwner : class where TDependent : class
    {
        b.Property<DateTimeOffset>("CreatedAtUtc").HasColumnType("datetimeoffset").HasDefaultValueSql("SYSUTCDATETIME()");
        b.Property<string>("CreatedBy").HasMaxLength(64).IsUnicode(false).HasDefaultValue("system");
        b.Property<DateTimeOffset?>("ModifiedAtUtc").HasColumnType("datetimeoffset");
    }
}

public sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> b)
    {
        b.ToTable("Branches", t => { t.HasCheckConstraint("CK_Branches_Status", "[Status] IN ('Active','Closed')"); });
        MappingConventions.ConfigureId(b); MappingConventions.ConfigureAudit(b);
        b.Property(x => x.Id).HasMaxLength(32).IsUnicode(false); b.Property(x => x.Code).HasMaxLength(12).IsUnicode(false).IsRequired();
        b.HasAlternateKey(x => x.Code); b.Property(x => x.Name).HasMaxLength(120).IsRequired(); b.Property(x => x.Region).HasMaxLength(60).IsRequired();
        b.Property(x => x.OpeningHours).HasMaxLength(32).IsUnicode(false); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        b.OwnsMany(x => x.Counters, c => { c.ToTable("Counters"); c.WithOwner().HasForeignKey("BranchCode").HasPrincipalKey(x => x.Code); c.Property<string>("BranchCode").HasMaxLength(12).IsUnicode(false); c.Property(x => x.Id).HasMaxLength(32).IsUnicode(false); c.Property(x => x.Name).HasMaxLength(80); c.HasKey("BranchCode", "Id"); MappingConventions.ConfigureAudit(c); });
    }
}

public sealed class StaffConfiguration : IEntityTypeConfiguration<Staff>
{
    public void Configure(EntityTypeBuilder<Staff> b)
    {
        b.ToTable("Staff", t => { t.HasCheckConstraint("CK_Staff_Status", "[Status] IN ('Active','Inactive')"); t.HasCheckConstraint("CK_Staff_Role", "[Role] IN ('CounterClerk','BranchManager','Auditor','Administrator')"); });
        MappingConventions.ConfigureId(b); MappingConventions.ConfigureAudit(b); b.Property(x => x.Id).HasMaxLength(32).IsUnicode(false);
        b.Property(x => x.Name).HasMaxLength(120).IsRequired(); b.Property(x => x.Email).HasMaxLength(254).IsUnicode(false).IsRequired(); b.HasIndex(x => x.Email).IsUnique();
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(24).IsUnicode(false).IsRequired(); b.Property(x => x.HomeBranchCode).HasMaxLength(12).IsUnicode(false);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false); b.Property(x => x.PasswordHash).HasMaxLength(256).IsUnicode(false);
        b.HasOne<RoleRecord>().WithMany().HasForeignKey(x => x.Role).HasPrincipalKey(x => x.Name).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.HomeBranchCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("Products", t => { t.HasCheckConstraint("CK_Products_UnitPrice", "[UnitPrice] >= 0"); t.HasCheckConstraint("CK_Products_TaxCategory", "[TaxCategory] IN ('Standard','Reduced','ZeroRated')"); });
        MappingConventions.ConfigureId(b); MappingConventions.ConfigureAudit(b); b.Property(x => x.Id).HasMaxLength(32).IsUnicode(false);
        b.Property(x => x.Sku).HasMaxLength(32).IsUnicode(false).IsRequired(); b.HasIndex(x => x.Sku).IsUnique(); b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
        b.HasOne<ProductCategoryRecord>().WithMany().HasForeignKey(x => x.Category).HasPrincipalKey(x => x.Name).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.TaxCategory).HasConversion<string>().HasMaxLength(16).IsUnicode(false); b.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
    }
}

public sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> b)
    {
        b.ToTable("BranchStock", t => t.HasCheckConstraint("CK_BranchStock_Quantity", "[Quantity] >= 0"));
        MappingConventions.ConfigureId(b); MappingConventions.ConfigureAudit(b); b.Property(x => x.Id).HasMaxLength(32).IsUnicode(false);
        b.Property(x => x.BranchCode).HasMaxLength(12).IsUnicode(false); b.Property(x => x.Sku).HasMaxLength(32).IsUnicode(false);
        b.HasIndex(x => new { x.BranchCode, x.Sku }).IsUnique(); b.Property<byte[]>("RowVersion").IsRowVersion();
        b.Property(x => x.UpdatedAt).HasColumnType("datetimeoffset");
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.Sku).HasPrincipalKey(x => x.Sku).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ShipmentRateConfiguration : IEntityTypeConfiguration<ShipmentRate>
{
    public void Configure(EntityTypeBuilder<ShipmentRate> b)
    {
        b.ToTable("ShippingRates", t => { t.HasCheckConstraint("CK_ShippingRates_Rates", "[BaseRate] >= 0 AND [PerKgRate] >= 0 AND [MaximumWeightKg] > 0"); });
        b.Ignore(x => x.Id);
        b.HasKey(x => new { x.Zone, x.ServiceLevel }); MappingConventions.ConfigureAudit(b); b.Property(x => x.Zone).HasMaxLength(16).IsUnicode(false); b.Property(x => x.ServiceLevel).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        b.HasOne<ShippingZoneRecord>().WithMany().HasForeignKey(x => x.Zone).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.BaseRate).HasColumnType("decimal(18,2)"); b.Property(x => x.PerKgRate).HasColumnType("decimal(18,2)"); b.Property(x => x.MaximumWeightKg).HasColumnType("decimal(9,3)");
    }
}

public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> b)
    {
        b.ToTable("Shipments", t => { t.HasCheckConstraint("CK_Shipments_Kind", "[Kind] IN ('Parcel','Letter')"); t.HasCheckConstraint("CK_Shipments_Weight", "[WeightKg] > 0 AND [WeightKg] <= 70"); t.HasCheckConstraint("CK_Shipments_Dimensions", "[LengthCm] > 0 AND [WidthCm] > 0 AND [HeightCm] > 0"); });
        MappingConventions.ConfigureId(b); MappingConventions.ConfigureAudit(b); b.Property(x => x.Id).HasMaxLength(32).IsUnicode(false); b.Property(x => x.BranchCode).HasMaxLength(12).IsUnicode(false);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12).IsUnicode(false); b.Property(x => x.ServiceLevel).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        b.Property(x => x.DestinationZone).HasMaxLength(16).IsUnicode(false); b.Ignore(x => x.WeightKg); b.Property(x => x.WeightValue).HasConversion(ValueObjectConverters.WeightConverter).HasColumnName("WeightKg").HasColumnType("decimal(9,3)"); b.Property(x => x.LengthCm).HasColumnType("decimal(9,2)"); b.Property(x => x.WidthCm).HasColumnType("decimal(9,2)"); b.Property(x => x.HeightCm).HasColumnType("decimal(9,2)"); b.Property(x => x.Price).HasColumnType("decimal(18,2)"); b.Property(x => x.CreatedAt).HasColumnType("datetimeoffset");
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ShipmentRate>().WithMany().HasForeignKey(x => new { x.DestinationZone, x.ServiceLevel }).HasPrincipalKey(x => new { x.Zone, x.ServiceLevel }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MoneyOrderConfiguration : IEntityTypeConfiguration<MoneyOrder>
{
    public void Configure(EntityTypeBuilder<MoneyOrder> b)
    {
        b.ToTable("MoneyOrders", t => { t.HasCheckConstraint("CK_MoneyOrders_Amount", "[Amount] > 0 AND [Fee] >= 0"); t.HasCheckConstraint("CK_MoneyOrders_Status", "[Status] IN ('PendingPayment','Issued','Cancelled')"); });
        MappingConventions.ConfigureId(b); MappingConventions.ConfigureAudit(b); b.Property(x => x.Id).HasMaxLength(32).IsUnicode(false); b.Property(x => x.BranchCode).HasMaxLength(12).IsUnicode(false);
        b.Property(x => x.Amount).HasColumnType("decimal(18,2)"); b.Property(x => x.Fee).HasColumnType("decimal(18,2)"); b.Property(x => x.Sender).HasMaxLength(120); b.Property(x => x.Recipient).HasMaxLength(120); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false); b.Property(x => x.CreatedAt).HasColumnType("datetimeoffset");
        b.HasIndex(x => new { x.BranchCode, x.CreatedAt }); b.HasIndex(x => x.CounterTransactionId); b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Restrict); b.HasOne<CounterTransaction>().WithMany().HasForeignKey(x => x.CounterTransactionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<IdentityVerificationRecord>().WithMany().HasForeignKey(x => x.IdentityVerificationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CounterTransactionConfiguration : IEntityTypeConfiguration<CounterTransaction>
{
    public void Configure(EntityTypeBuilder<CounterTransaction> b)
    {
        b.ToTable("CounterTransactions", t => { t.HasCheckConstraint("CK_CounterTransactions_Status", "[Status] IN ('Open','Paid','Voided','Refunded')"); t.HasCheckConstraint("CK_CounterTransactions_PaymentMethod", "[PaymentMethod] IS NULL OR [PaymentMethod] IN ('Cash','Card','Upi','MoneyOrder')"); t.HasCheckConstraint("CK_CounterTransactions_Totals", "[Subtotal] >= 0 AND [Tax] >= 0 AND [Total] >= 0"); });
        MappingConventions.ConfigureId(b); MappingConventions.ConfigureAudit(b); b.Property(x => x.Id).HasMaxLength(32).IsUnicode(false); b.Property(x => x.BranchCode).HasMaxLength(12).IsUnicode(false); b.Property(x => x.CounterId).HasMaxLength(32).IsUnicode(false); b.Property(x => x.ClerkId).HasMaxLength(32).IsUnicode(false);
        b.Property(x => x.Subtotal).HasColumnType("decimal(18,2)"); b.Property(x => x.Tax).HasColumnType("decimal(18,2)"); b.Property(x => x.Total).HasColumnType("decimal(18,2)"); b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false); b.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(16).IsUnicode(false); b.Property(x => x.Reason).HasMaxLength(300); b.Property(x => x.CreatedAt).HasColumnType("datetimeoffset"); b.Property(x => x.CompletedAt).HasColumnType("datetimeoffset"); b.Property<byte[]>("RowVersion").IsRowVersion();
        b.HasIndex(x => new { x.BranchCode, x.CreatedAt }); b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Restrict); b.HasOne<Staff>().WithMany().HasForeignKey(x => x.ClerkId).OnDelete(DeleteBehavior.Restrict);
        b.OwnsMany(x => x.Lines, l => { l.ToTable("TransactionLines", table => { table.HasCheckConstraint("CK_TransactionLines_Values", "[Quantity] > 0 AND [UnitPrice] >= 0 AND [TaxRate] BETWEEN 0 AND 1 AND [LineTotal] >= 0"); table.HasCheckConstraint("CK_TransactionLines_Kind", "[Kind] IN ('RetailProduct','Shipping','MoneyOrder','OtherService')"); }); l.WithOwner().HasForeignKey("CounterTransactionId"); l.Property<string>("CounterTransactionId").HasMaxLength(32).IsUnicode(false); l.Property(x => x.Id).HasMaxLength(32).IsUnicode(false); l.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20).IsUnicode(false); l.Property(x => x.Description).HasMaxLength(240); l.Property(x => x.Sku).HasMaxLength(32).IsUnicode(false); l.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)"); l.Property(x => x.TaxRate).HasColumnType("decimal(6,4)"); l.Property(x => x.LineTotal).HasColumnType("decimal(18,2)"); l.HasKey("CounterTransactionId", "Id"); MappingConventions.ConfigureAudit(l); });
    }
}

public sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> b)
    {
        b.ToTable("AuditEntries"); MappingConventions.ConfigureAudit(b); b.HasKey(x => x.Id); b.Property(x => x.Id).HasMaxLength(32).IsUnicode(false); b.Property(x => x.StaffId).HasMaxLength(32).IsUnicode(false); b.Property(x => x.Role).HasMaxLength(24).IsUnicode(false); b.Property(x => x.BranchCode).HasMaxLength(12).IsUnicode(false); b.Property(x => x.Controller).HasMaxLength(80).IsUnicode(false); b.Property(x => x.Action).HasMaxLength(80).IsUnicode(false); b.Property(x => x.RouteValues).HasMaxLength(1000); b.Property(x => x.CorrelationId).HasMaxLength(64).IsUnicode(false); b.Property(x => x.Timestamp).HasColumnType("datetimeoffset"); b.Property(x => x.Outcome).HasMaxLength(16).IsUnicode(false); b.HasIndex(x => new { x.BranchCode, x.Timestamp });
    }
}


public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("Payments", t => { t.HasCheckConstraint("CK_Payments_Amount", "[Amount] > 0"); t.HasCheckConstraint("CK_Payments_Method", "[Method] IN ('Cash','Card','Upi','MoneyOrder')"); });
        MappingConventions.ConfigureAudit(b); b.HasKey(x => x.Id); b.Property(x => x.Id).HasMaxLength(32).IsUnicode(false); b.Property(x => x.CounterTransactionId).HasMaxLength(32).IsUnicode(false);
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(16).IsUnicode(false); b.Ignore(x => x.Amount); b.Property(x => x.AmountValue).HasConversion(ValueObjectConverters.MoneyConverter).HasColumnName("Amount").HasColumnType("decimal(18,2)"); b.Property(x => x.PaidAtUtc).HasColumnType("datetimeoffset");
        b.HasOne<CounterTransaction>().WithMany().HasForeignKey(x => x.CounterTransactionId).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => x.CounterTransactionId).IsUnique();
    }
}

public sealed class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> b)
    {
        b.ToTable("StockAdjustments", t => t.HasCheckConstraint("CK_StockAdjustments_Delta", "[Delta] <> 0"));
        MappingConventions.ConfigureAudit(b); b.HasKey(x => x.Id); b.Property(x => x.Id).HasMaxLength(32).IsUnicode(false);
        // StockAdjustment is written by usp_AdjustStock. The SQL foreign keys remain
        // authoritative; the EF model stores the domain value objects via converters.
        b.Ignore(x => x.BranchCode); b.Property(x => x.BranchCodeValue).HasConversion(ValueObjectConverters.BranchCodeConverter).HasColumnName("BranchCode").HasMaxLength(12).IsUnicode(false);
        b.Ignore(x => x.Sku); b.Property(x => x.SkuValue).HasConversion(ValueObjectConverters.SkuConverter).HasColumnName("Sku").HasMaxLength(32).IsUnicode(false);
        b.Property(x => x.Reason).HasMaxLength(250).IsRequired(); b.Property(x => x.ChangedBy).HasMaxLength(32).IsUnicode(false); b.Property(x => x.ChangedAtUtc).HasColumnType("datetimeoffset");
        b.HasOne<Staff>().WithMany().HasForeignKey(x => x.ChangedBy).OnDelete(DeleteBehavior.Restrict); b.HasIndex(x => new { x.BranchCodeValue, x.ChangedAtUtc });
    }
}
