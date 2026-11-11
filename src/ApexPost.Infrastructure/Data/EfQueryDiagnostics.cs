using ApexPost.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Data;

public static class EfQueryDiagnostics
{
    public static IReadOnlyDictionary<string, string> Capture(ApexPostDbContext db, int lowStockThreshold = 5)
    {
        var lowStock = db.BranchStock.AsNoTracking().Where(x => x.Quantity <= lowStockThreshold)
            .OrderBy(x => x.BranchCode).ThenBy(x => x.Quantity)
            .Select(x => new { x.BranchCode, x.Sku, x.Quantity });
        var dailySales = db.CounterTransactions.AsNoTracking()
            .Where(x => x.Status == TransactionStatus.Paid && x.CompletedAt >= DateTimeOffset.UtcNow.Date && x.CompletedAt < DateTimeOffset.UtcNow.Date.AddDays(1))
            .GroupBy(x => x.BranchCode).Select(group => new { BranchCode = group.Key, Total = group.Sum(x => x.Total), Count = group.Count() });
        var shipmentSearch = db.Shipments.AsNoTracking().Where(x => x.DestinationZone == "ZONE-01" && x.ServiceLevel == ServiceLevel.Standard)
            .OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.BranchCode, x.WeightValue, x.Price });
        return new Dictionary<string, string>
        {
            ["LowStock"] = lowStock.ToQueryString(),
            ["DailySales"] = dailySales.ToQueryString(),
            ["ShipmentSearch"] = shipmentSearch.ToQueryString()
        };
    }
}
