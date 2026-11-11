using ApexPost.Domain;

namespace ApexPost.Application;

public sealed record DashboardSummary(int BranchCount, int ActiveStaffCount, int ProductCount, int LowStockCount, decimal TodaySales, int TodayTransactions);
public sealed record DailySalesRow(string BranchCode, decimal RetailSales, decimal ServiceSales, decimal TotalSales, int TransactionCount);
public sealed record LowStockRow(string BranchCode, string Sku, string ProductName, int Quantity);
public sealed record ShippingVolumeRow(string Zone, string ServiceLevel, int Count);
public sealed record MoneyOrderAuditRow(string BranchCode, int MoneyOrders, int SuccessfulVerifications, int FailedVerifications);
