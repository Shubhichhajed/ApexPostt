using Microsoft.Extensions.Configuration;
using ApexPost.Application;
using ApexPost.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Reports;

/// <summary>Read side uses isolated no-tracking query contexts and parameterized stored procedures.</summary>
public sealed class EfReportService(IDbContextFactory<ApexPostDbContext> factory, IConfiguration configuration) : IReportService
{
    public async Task<DashboardSummary> GetDashboardAsync(string? branchCode, CancellationToken cancellationToken = default)
    {
        // Independent dashboard queries use separate contexts: EF DbContext is not thread-safe.
        var branchTask = CountBranchesAsync(branchCode, cancellationToken);
        var staffTask = CountActiveStaffAsync(branchCode, cancellationToken);
        var productTask = CountProductsAsync(cancellationToken);
        var stockTask = CountLowStockAsync(branchCode, cancellationToken);
        var salesTask = SumTodaySalesAsync(branchCode, cancellationToken);
        var transactionTask = CountTodaySalesTransactionsAsync(branchCode, cancellationToken);
        await Task.WhenAll(branchTask, staffTask, productTask, stockTask, salesTask, transactionTask);
        return new DashboardSummary(await branchTask, await staffTask, await productTask, await stockTask, await salesTask, await transactionTask);
    }

    public async Task<IReadOnlyList<DailySalesRow>> GetDailySalesAsync(DateOnly day, string? branchCode, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var date = day.ToDateTime(TimeOnly.MinValue);
        var rows = await db.Database.SqlQuery<DailySalesResult>($"EXEC dbo.usp_DailyBranchSales @BranchCode={branchCode}, @Date={date}").ToListAsync(cancellationToken);
        return rows.Select(x => new DailySalesRow(x.BranchCode, x.RetailSales, x.ServiceSales, x.TotalSales, x.TransactionCount)).ToArray();
    }

    public async Task<IReadOnlyList<LowStockRow>> GetLowStockAsync(string? branchCode, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var threshold = configuration.GetValue("Inventory:LowStockThreshold", 5);
        var rows = await db.Database.SqlQuery<LowStockResult>($"EXEC dbo.usp_LowStockReport @BranchCode={branchCode}, @Threshold={threshold}").ToListAsync(cancellationToken);
        return rows.Select(x => new LowStockRow(x.BranchCode, x.Sku, x.ProductName, x.Quantity)).ToArray();
    }

    public async Task<IReadOnlyList<ShippingVolumeRow>> GetShippingVolumeAsync(string? branchCode, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Database.SqlQuery<ShippingVolumeResult>($"EXEC dbo.usp_ShippingVolumeByZone @BranchCode={branchCode}").ToListAsync(cancellationToken);
        return rows.Select(x => new ShippingVolumeRow(x.Zone, x.ServiceLevel, x.Count)).ToArray();
    }

    public async Task<IReadOnlyList<MoneyOrderAuditRow>> GetMoneyOrderAuditAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Database.SqlQuery<MoneyOrderAuditResult>($"EXEC dbo.usp_MoneyOrderAuditByBranch").ToListAsync(cancellationToken);
        return rows.Select(x => new MoneyOrderAuditRow(x.BranchCode, x.MoneyOrders, x.SuccessfulVerifications, x.FailedVerifications)).ToArray();
    }

    private async Task<int> CountBranchesAsync(string? branchCode, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        return await db.Branches.AsNoTracking().CountAsync(x => branchCode == null || x.Code == branchCode, token);
    }

    private async Task<int> CountActiveStaffAsync(string? branchCode, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        return await db.Staff.AsNoTracking().CountAsync(x => x.Status == ApexPost.Domain.StaffStatus.Active && (branchCode == null || x.HomeBranchCode == branchCode), token);
    }

    private async Task<int> CountProductsAsync(CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        return await db.Products.AsNoTracking().CountAsync(token);
    }

    private async Task<int> CountLowStockAsync(string? branchCode, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var threshold = configuration.GetValue("Inventory:LowStockThreshold", 5);
        return await db.BranchStock.AsNoTracking().CountAsync(x => x.Quantity <= threshold && (branchCode == null || x.BranchCode == branchCode), token);
    }

    private async Task<decimal> SumTodaySalesAsync(string? branchCode, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var start = DateTimeOffset.UtcNow.Date;
        var end = start.AddDays(1);
        return await db.CounterTransactions.AsNoTracking().Where(x => x.Status == ApexPost.Domain.TransactionStatus.Paid && x.CompletedAt >= start && x.CompletedAt < end && (branchCode == null || x.BranchCode == branchCode)).SumAsync(x => (decimal?)x.Total, token) ?? 0m;
    }

    private async Task<int> CountTodaySalesTransactionsAsync(string? branchCode, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var start = DateTimeOffset.UtcNow.Date;
        var end = start.AddDays(1);
        return await db.CounterTransactions.AsNoTracking().CountAsync(x => x.Status == ApexPost.Domain.TransactionStatus.Paid && x.CompletedAt >= start && x.CompletedAt < end && (branchCode == null || x.BranchCode == branchCode), token);
    }
}
