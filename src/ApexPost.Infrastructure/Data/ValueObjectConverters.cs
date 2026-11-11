using ApexPost.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ApexPost.Infrastructure.Data;

/// <summary>EF Core scalar converters used by the persistence mappings: Payment.Money,
/// Shipment.Weight and StockAdjustment.BranchCode/Sku.</summary>
public static class ValueObjectConverters
{
    public static readonly ValueConverter<Money, decimal> MoneyConverter =
        new(value => value.Amount, value => new Money(value, "INR"));

    public static readonly ValueConverter<Weight, decimal> WeightConverter =
        new(value => value.Kilograms, value => new Weight(value));

    public static readonly ValueConverter<BranchCode, string> BranchCodeConverter =
        new(value => value.Value, value => new BranchCode(value));

    public static readonly ValueConverter<Sku, string> SkuConverter =
        new(value => value.Value, value => new Sku(value));
}
