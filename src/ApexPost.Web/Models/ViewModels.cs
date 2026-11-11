using System.ComponentModel.DataAnnotations;
using ApexPost.Domain;

namespace ApexPost.Web.Models;

public sealed class LoginViewModel
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password), StringLength(256, MinimumLength = 10)] public string Password { get; set; } = "";
    [StringLength(2048)] public string? ReturnUrl { get; set; }
}
public sealed class AddRetailLineViewModel
{
    [Required, StringLength(32)] public string TransactionId { get; set; } = "";
    [Required, StringLength(32, MinimumLength = 3)] public string Sku { get; set; } = "";
    [Range(1, 1000)] public int Quantity { get; set; } = 1;
}
public sealed class CompleteTransactionViewModel
{
    [Required, StringLength(32)] public string TransactionId { get; set; } = "";
    [Required, EnumDataType(typeof(PaymentMethod))] public PaymentMethod PaymentMethod { get; set; }
}
public sealed class VoidTransactionViewModel
{
    [Required, StringLength(32)] public string TransactionId { get; set; } = "";
    [Required, StringLength(500, MinimumLength = 5)] public string Reason { get; set; } = "";
}
public sealed class ShippingViewModel
{
    [StringLength(32)] public string? TransactionId { get; set; }
    [Required, EnumDataType(typeof(ShipmentKind))] public ShipmentKind Kind { get; set; }
    [Range(0.01, 100)] public decimal WeightKg { get; set; } = 1;
    [Range(1, 300)] public decimal LengthCm { get; set; } = 20;
    [Range(1, 300)] public decimal WidthCm { get; set; } = 15;
    [Range(1, 300)] public decimal HeightCm { get; set; } = 10;
    [Required, StringLength(12)] public string DestinationZone { get; set; } = "ZONE-01";
    [Required, EnumDataType(typeof(ServiceLevel))] public ServiceLevel ServiceLevel { get; set; }
}
public sealed class MoneyOrderViewModel
{
    [StringLength(32)] public string? TransactionId { get; set; }
    [Range(1, 1000000)] public decimal Amount { get; set; }
    [Required, StringLength(120)] public string Sender { get; set; } = "";
    [Required, StringLength(120)] public string Recipient { get; set; } = "";
    [Required, StringLength(12)] public string BranchCode { get; set; } = "";
    [StringLength(32)] public string? IdentityVerificationId { get; set; }
    [Required, EnumDataType(typeof(PaymentMethod))] public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
}


public sealed class ShippingEstimateViewModel
{
    public ShipmentKind Kind { get; set; }
    public decimal WeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public string DestinationZone { get; set; } = "ZONE-01";
    public ServiceLevel ServiceLevel { get; set; }
    public decimal Price { get; set; }
}
