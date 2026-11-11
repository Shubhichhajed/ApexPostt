using ApexPost.Domain;
using Xunit;

namespace ApexPost.UnitTests.Domain;

public sealed class BusinessRuleTests
{
    [Fact]
    public void Stock_adjustment_rejects_negative_quantity()
    {
        var stock = StockItem.Create("IND01", "APX-001", 2);
        Assert.Throws<DomainException>(() => stock.Adjust(-3));
        Assert.Equal(2, stock.Quantity);
    }

    [Fact]
    public void Money_order_above_threshold_requires_successful_verification()
    {
        Assert.Throws<DomainException>(() => BusinessRules.EnsureMoneyOrderVerified(1001m, 1000m, false));
        BusinessRules.EnsureMoneyOrderVerified(1001m, 1000m, true);
        BusinessRules.EnsureMoneyOrderVerified(1000m, 1000m, false);
    }

    [Fact]
    public void Parcel_weight_limit_is_enforced_for_selected_service_level()
    {
        var rates = new[] { new ShipmentRate("ZONE-01", ServiceLevel.Standard, 50m, 10m, 2m) };
        Assert.Throws<DomainException>(() => BusinessRules.ShippingPrice(ShipmentKind.Parcel, new Weight(2.1m), "ZONE-01", ServiceLevel.Standard, rates));
    }

    [Fact]
    public void Shipping_pricing_uses_configured_zone_rate_and_service_multiplier()
    {
        var rates = new[] { new ShipmentRate("ZONE-01", ServiceLevel.Standard, 50m, 10m, 10m), new ShipmentRate("ZONE-01", ServiceLevel.Express, 50m, 10m, 10m) };
        Assert.Equal(70m, BusinessRules.ShippingPrice(ShipmentKind.Parcel, new Weight(2m), "ZONE-01", ServiceLevel.Standard, rates));
        Assert.Equal(122.50m, BusinessRules.ShippingPrice(ShipmentKind.Parcel, new Weight(2m), "ZONE-01", ServiceLevel.Express, rates));
    }

    [Fact]
    public void Inactive_staff_closed_branch_and_cross_branch_transaction_are_rejected()
    {
        var staff = new Staff { HomeBranchCode = "IND01", Status = StaffStatus.Inactive };
        var branch = new Branch { Code = "IND01", Status = BranchStatus.Active };
        Assert.Throws<DomainException>(() => BusinessRules.EnsureCanTransact(staff, branch, "IND01"));
        staff.Status = StaffStatus.Active; branch.Status = BranchStatus.Closed;
        Assert.Throws<DomainException>(() => BusinessRules.EnsureCanTransact(staff, branch, "IND01"));
        branch.Status = BranchStatus.Active;
        Assert.Throws<DomainException>(() => BusinessRules.EnsureCanTransact(staff, branch, "DEL01"));
    }

    [Fact]
    public void Identity_document_is_masked_and_never_returns_full_number()
    {
        Assert.Equal("****6789", IdentityVerification.Mask("ABCD123456789"));
        Assert.Equal("****", IdentityVerification.Mask("1234"));
    }

    [Fact]
    public void Voids_require_manager_and_reason()
    {
        var clerk = new Staff { Role = StaffRole.CounterClerk };
        Assert.Throws<DomainException>(() => BusinessRules.EnsureManager(clerk, "mistake"));
        var manager = new Staff { Role = StaffRole.BranchManager };
        Assert.Throws<DomainException>(() => BusinessRules.EnsureManager(manager, " "));
        BusinessRules.EnsureManager(manager, "Customer request");
    }

    [Fact]
    public void Transaction_recalculates_tax_and_rejects_mutation_after_payment()
    {
        var transaction = new CounterTransaction();
        transaction.AddLine(new TransactionLine("1", LineKind.RetailProduct, "Box", "APX-001", 2, 10m, 0.18m, 23.60m));
        Assert.Equal(20m, transaction.Subtotal);
        Assert.Equal(3.60m, transaction.Tax);
        Assert.Equal(23.60m, transaction.Total);
        transaction.MarkPaid(PaymentMethod.Cash);
        Assert.Throws<DomainException>(() => transaction.AddLine(new TransactionLine("2", LineKind.Shipping, "Ship", null, 1, 5m, 0m, 5m)));
    }
}
