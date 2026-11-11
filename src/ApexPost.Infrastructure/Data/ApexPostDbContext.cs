using ApexPost.Domain;
using ApexPost.Infrastructure.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Data;

public sealed class ApexPostDbContext(DbContextOptions<ApexPostDbContext> options) : DbContext(options)
{
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockItem> BranchStock => Set<StockItem>();
    public DbSet<ShipmentRate> ShippingRates => Set<ShipmentRate>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<IdentityVerificationRecord> IdentityVerifications => Set<IdentityVerificationRecord>();
    public DbSet<MoneyOrder> MoneyOrders => Set<MoneyOrder>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<CounterTransaction> CounterTransactions => Set<CounterTransaction>();
    public DbSet<AuditRecord> AuditEntries => Set<AuditRecord>();
    public DbSet<RoleRecord> Roles => Set<RoleRecord>();
    public DbSet<ProductCategoryRecord> ProductCategories => Set<ProductCategoryRecord>();
    public DbSet<ShippingZoneRecord> ShippingZones => Set<ShippingZoneRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<SyncInboxRecord> SyncInbox => Set<SyncInboxRecord>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditMetadata();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAuditMetadata();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyAuditMetadata()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.Metadata.FindProperty("CreatedAtUtc") is null) continue;
            if (entry.State == EntityState.Added)
            {
                entry.Property("CreatedAtUtc").CurrentValue = now;
                if (entry.Metadata.FindProperty("CreatedBy") is not null && string.IsNullOrWhiteSpace(entry.Property("CreatedBy").CurrentValue as string))
                    entry.Property("CreatedBy").CurrentValue = "system";
            }
            else if (entry.Metadata.FindProperty("ModifiedAtUtc") is not null)
            {
                entry.Property("ModifiedAtUtc").CurrentValue = now;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ConfigureModel(modelBuilder);

    internal static void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApexPostDbContext).Assembly);
    }
}
