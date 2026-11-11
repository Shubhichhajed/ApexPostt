namespace ApexPost.Infrastructure.Reports;

public sealed class DailySalesResult
{
    public string BranchCode { get; set; } = string.Empty;
    public decimal RetailSales { get; set; }
    public decimal ServiceSales { get; set; }
    public decimal TotalSales { get; set; }
    public int TransactionCount { get; set; }
}

public sealed class LowStockResult
{
    public string BranchCode { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public sealed class ShippingVolumeResult
{
    public string Zone { get; set; } = string.Empty;
    public string ServiceLevel { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class MoneyOrderAuditResult
{
    public string BranchCode { get; set; } = string.Empty;
    public int MoneyOrders { get; set; }
    public int SuccessfulVerifications { get; set; }
    public int FailedVerifications { get; set; }
}
