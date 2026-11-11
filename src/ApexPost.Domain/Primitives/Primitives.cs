using System.Globalization;

namespace ApexPost.Domain;

public interface IEntity { string Id { get; } }

public sealed class DomainException(string message) : Exception(message) { }
public sealed class ConcurrencyConflictException(string message) : Exception(message) { }

public readonly record struct Money(decimal Amount, string Currency = "INR")
{
    public Money Add(Money other)
    {
        if (!StringComparer.Ordinal.Equals(Currency, other.Currency)) throw new DomainException("Currencies must match.");
        return new(decimal.Round(Amount + other.Amount, 2, MidpointRounding.AwayFromZero), Currency);
    }
    public static Money Zero => new(0m);
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Currency} {Amount:0.00}");
}

public readonly record struct Weight
{
    public decimal Kilograms { get; }
    public Weight(decimal kilograms)
    {
        if (kilograms <= 0m) throw new DomainException("Weight must be greater than zero.");
        Kilograms = kilograms;
    }
}

public readonly record struct BranchCode
{
    public string Value { get; }
    public BranchCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 12) throw new DomainException("Branch code is required and must be at most 12 characters.");
        Value = value.Trim().ToUpperInvariant();
    }
    public override string ToString() => Value;
}

public readonly record struct Sku
{
    public string Value { get; }
    public Sku(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32) throw new DomainException("SKU is required and must be at most 32 characters.");
        Value = value.Trim().ToUpperInvariant();
    }
    public override string ToString() => Value;
}

public enum StaffRole { CounterClerk, BranchManager, Auditor, Administrator }
public enum BranchStatus { Active, Closed }
public enum StaffStatus { Active, Inactive }
public enum ProductCategory { Packaging, Stationery, OfficeSupplies }
public enum TaxCategory { Standard, Reduced, ZeroRated }
public enum TransactionStatus { Open, Paid, Voided, Refunded }
public enum PaymentMethod { Cash, Card, Upi, MoneyOrder }
public enum LineKind { RetailProduct, Shipping, MoneyOrder, OtherService }
public enum ShipmentKind { Parcel, Letter }
public enum ServiceLevel { Standard, Express, Registered }
public enum IdentityDocumentType { Passport, DrivingLicence, NationalId, Other }
public enum VerificationResult { Pending, Successful, Failed }
public enum MoneyOrderStatus { PendingPayment, Issued, Cancelled }

public sealed record Address(string Line1, string City, string Region, string PostalCode, string Country = "India");

public sealed class Branch : IEntity
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [RequiredValue] public string Code { get; init; } = "";
     [RequiredValue] public string Name { get => field; set => field = value?.Trim() ?? string.Empty; } = "";
    public string Region { get; set; } = "";
    public string OpeningHours { get; set; } = "09:00-17:00";
    public BranchStatus Status { get; set; } = BranchStatus.Active;
    public List<Counter> Counters { get; set; } = [];
}
public sealed record Counter(string Id, string Name, bool IsActive = true);

public sealed class Staff : IEntity
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [RequiredValue] public string Name { get; set; } = "";
    [RequiredValue] public string Email { get; set; } = "";
    public StaffRole Role { get; set; }
    public string HomeBranchCode { get; set; } = "";
    public StaffStatus Status { get; set; } = StaffStatus.Active;
    public string PasswordHash { get; set; } = "";
}

public sealed class Product : IEntity
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public ProductCategory Category { get; set; }
    public decimal UnitPrice { get; set; }
    public TaxCategory TaxCategory { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class StockItem : IEntity
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string BranchCode { get; set; } = "";
    public string Sku { get; set; } = "";
    [System.Text.Json.Serialization.JsonInclude]
    public int Quantity { get; private set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static StockItem Create(string branchCode, string sku, int quantity)
    {
        if (quantity < 0) throw new DomainException("Initial stock cannot be negative.");
        return new StockItem { BranchCode = new BranchCode(branchCode).Value, Sku = new Sku(sku).Value, Quantity = quantity };
    }

    public void Adjust(int delta)
    {
        if (delta < -Quantity) throw new DomainException("Insufficient stock; stock cannot become negative.");
        if (delta > 0 && Quantity > int.MaxValue - delta) throw new DomainException("Stock quantity exceeds the supported limit.");
        Quantity += delta;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public sealed record ShipmentRate(string Zone, ServiceLevel ServiceLevel, decimal BaseRate, decimal PerKgRate, decimal MaximumWeightKg) : IEntity
{
    public string Id => $"{Zone}:{ServiceLevel}";
}
public sealed class Shipment : IEntity
{
    public string BranchCode { get; set; } = "";
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public ShipmentKind Kind { get; set; }
    public decimal WeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public string DestinationZone { get; set; } = "";
    public ServiceLevel ServiceLevel { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Weight WeightValue { get => new(WeightKg); private set => WeightKg = value.Kilograms; }
}
public sealed class IdentityVerification : IEntity
{
    public string BranchCode { get; set; } = "";
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public IdentityDocumentType DocumentType { get; set; }
    public string MaskedDocumentNumber { get; set; } = "****";
    public VerificationResult Result { get; set; }
    public string VerifiedByStaffId { get; set; } = "";
    public string CounterTransactionId { get; set; } = "";
    public DateTimeOffset VerifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public static string Mask(string? documentNumber)
    {
        var clean = (documentNumber ?? string.Empty).Trim();
        return clean.Length <= 4 ? new string('*', Math.Max(4, clean.Length)) : $"****{clean[^4..]}";
    }
}
public sealed class MoneyOrder : IEntity
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public decimal Amount { get; set; }
    public string Sender { get; set; } = "";
    public string Recipient { get; set; } = "";
    public decimal Fee { get; set; }
    public string BranchCode { get; set; } = "";
    public string CounterTransactionId { get; set; } = "";
    public MoneyOrderStatus Status { get; set; }
    public string? IdentityVerificationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class Payment : IEntity
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string CounterTransactionId { get; set; } = string.Empty;
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset PaidAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Money AmountValue { get => new(Amount, "INR"); private set => Amount = value.Amount; }
}

public sealed class StockAdjustment : IEntity
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string BranchCode { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int Delta { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ChangedBy { get; set; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public BranchCode BranchCodeValue { get => new(BranchCode); private set => BranchCode = value.Value; }
    public Sku SkuValue { get => new(Sku); private set => Sku = value.Value; }
}

public sealed record TransactionLine(string Id, LineKind Kind, string Description, string? Sku, int Quantity, decimal UnitPrice, decimal TaxRate, decimal LineTotal);
public sealed class CounterTransaction : IEntity
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string BranchCode { get; set; } = "";
    public string CounterId { get; set; } = "";
    public string ClerkId { get; set; } = "";
    public List<TransactionLine> Lines { get; set; } = [];
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public TransactionStatus Status { get; set; } = TransactionStatus.Open;
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public void AddLine(TransactionLine line)
    {
        if (Status != TransactionStatus.Open) throw new DomainException("Only open transactions can be changed.");
        if (line.Quantity <= 0 || line.UnitPrice < 0 || line.TaxRate is < 0m or > 1m || string.IsNullOrWhiteSpace(line.Description)) throw new DomainException("Invalid transaction line.");
        var expectedLineTotal = decimal.Round(line.UnitPrice * line.Quantity * (1m + line.TaxRate), 2, MidpointRounding.AwayFromZero);
        if (line.LineTotal != expectedLineTotal) throw new DomainException("Transaction line total does not match the unit price, quantity and tax.");
        Lines.Add(line); Recalculate();
    }
    public void Recalculate()
    {
        Subtotal = new Money(Lines.Sum(x => x.UnitPrice * x.Quantity)).Amount.RoundMoney();
        Tax = new Money(Lines.Sum(x => x.UnitPrice * x.Quantity * x.TaxRate)).Amount.RoundMoney();
        Total = new Money(Subtotal + Tax).Amount.RoundMoney();
    }
    public void MarkPaid(PaymentMethod method)
    {
        if (Status != TransactionStatus.Open || Lines.Count == 0) throw new DomainException("A non-empty open transaction is required for payment.");
        if (!Enum.IsDefined(typeof(PaymentMethod), method)) throw new DomainException("Unsupported payment method.");
        PaymentMethod = method; Status = TransactionStatus.Paid; CompletedAt = DateTimeOffset.UtcNow;
    }
    public void Void(string reason)
    {
        if (Status != TransactionStatus.Paid || string.IsNullOrWhiteSpace(reason)) throw new DomainException("Only paid transactions can be voided, and a reason is required.");
        Status = TransactionStatus.Voided; Reason = reason.Trim();
    }
    public void Refund(string reason)
    {
        if (Status != TransactionStatus.Paid || string.IsNullOrWhiteSpace(reason)) throw new DomainException("Only paid transactions can be refunded, and a reason is required.");
        Status = TransactionStatus.Refunded; Reason = reason.Trim();
    }
}

public sealed record AuditRecord(string Id, string StaffId, string Role, string BranchCode, string Controller, string Action, string RouteValues, int StatusCode, long DurationMs, string CorrelationId, DateTimeOffset Timestamp, string Outcome);

public interface IShippingPricingStrategy
{
    decimal Calculate(ShipmentKind kind, Weight weight, string zone, ServiceLevel level, IReadOnlyCollection<ShipmentRate> rates);
}

public sealed class ZoneRateShippingPricingStrategy : IShippingPricingStrategy
{
    public decimal Calculate(ShipmentKind kind, Weight weight, string zone, ServiceLevel level, IReadOnlyCollection<ShipmentRate> rates) => BusinessRules.ShippingPrice(kind, weight, zone, level, rates);
}

public sealed class MoneyOrderPolicy(decimal threshold)
{
    public decimal Threshold { get; } = Math.Max(0m, threshold);
    public Func<decimal, bool> RequiresIdentityVerification => amount => amount > Threshold;
}

public static class BusinessRules
{
    public static decimal TaxRate(TaxCategory category) => category switch { TaxCategory.Standard => 0.18m, TaxCategory.Reduced => 0.05m, TaxCategory.ZeroRated => 0m, _ => throw new ArgumentOutOfRangeException(nameof(category)) };
    public static decimal ShippingPrice(ShipmentKind kind, Weight weight, string zone, ServiceLevel level, IReadOnlyCollection<ShipmentRate> rates)
    {
        if (!Enum.IsDefined(typeof(ShipmentKind), kind) || !Enum.IsDefined(typeof(ServiceLevel), level)) throw new DomainException("Unsupported shipment type or service level.");
        var rate = rates.FirstOrDefault(r => string.Equals(r.Zone, zone, StringComparison.OrdinalIgnoreCase) && r.ServiceLevel == level)
            ?? throw new DomainException("No shipping rate is configured for the selected zone and service level.");
        if (weight.Kilograms > rate.MaximumWeightKg) throw new DomainException("Shipment exceeds the maximum weight for this service level.");
        var kindSurcharge = kind == ShipmentKind.Letter ? 10m : 0m;
        var multiplier = level switch { ServiceLevel.Standard => 1m, ServiceLevel.Express => 1.75m, ServiceLevel.Registered => 1.35m, _ => throw new ArgumentOutOfRangeException(nameof(level)) };
        return decimal.Round((rate.BaseRate + rate.PerKgRate * weight.Kilograms + kindSurcharge) * multiplier, 2, MidpointRounding.AwayFromZero);
    }
    public static void EnsureCanTransact(Staff staff, Branch branch, string requestedBranch)
    {
        if (staff.Status != StaffStatus.Active) throw new DomainException("Inactive staff cannot process transactions.");
        if (branch.Status != BranchStatus.Active) throw new DomainException("Closed branches cannot accept transactions.");
        if (staff.Role == StaffRole.Auditor) throw new DomainException("Auditors cannot open or process counter transactions.");
        var assigned = new BranchCode(staff.HomeBranchCode);
        var requested = new BranchCode(requestedBranch);
        if (staff.Role != StaffRole.Administrator && assigned != requested) throw new DomainException("Staff can transact only at their assigned branch.");
    }
    public static void EnsureMoneyOrderVerified(decimal amount, decimal threshold, bool successfulVerificationInSameTransaction)
    {
        if (amount > threshold && !successfulVerificationInSameTransaction) throw new DomainException("A successful identity verification in the same transaction is required for this money order.");
    }
    public static void EnsureManager(Staff staff, string reason)
    {
        if (staff.Role != StaffRole.BranchManager && staff.Role != StaffRole.Administrator) throw new DomainException("Branch Manager authorization is required.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("A reason is required.");
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class RequiredValueAttribute : Attribute { }
