USE ApexPostDb;
GO
/* Enable Query Store in the dedicated assessment database. */
ALTER DATABASE ApexPostDb SET QUERY_STORE = ON;
GO
/* Run before candidate indexes to capture the baseline; include Actual Execution Plan (Ctrl+M) in SSMS. */
SET STATISTICS IO ON;
SET STATISTICS TIME ON;
DECLARE @BranchCode varchar(12) = 'IND01', @Date date = CONVERT(date, SYSUTCDATETIME());

-- Workload 1: low-stock report.
SELECT s.BranchCode, s.Sku, p.Name, s.Quantity
FROM dbo.BranchStock s INNER JOIN dbo.Products p ON p.Sku = s.Sku
WHERE s.BranchCode = @BranchCode AND s.Quantity <= 5
ORDER BY s.Quantity, s.Sku;

-- Workload 2: daily sales by branch; SARGable range on CompletedAt.
SELECT BranchCode, SUM(Total) AS TotalSales, COUNT_BIG(*) AS Transactions
FROM dbo.CounterTransactions
WHERE BranchCode = @BranchCode AND Status = 'Paid'
  AND CompletedAt >= @Date AND CompletedAt < DATEADD(day, 1, @Date)
GROUP BY BranchCode;

-- Workload 3: shipment search by zone/service and recent date.
SELECT Id, BranchCode, WeightKg, Price, CreatedAt
FROM dbo.Shipments
WHERE DestinationZone = 'ZONE-01' AND ServiceLevel = 'Standard'
ORDER BY CreatedAt DESC;

-- Workload 4: money-order auditor search.
SELECT BranchCode, CreatedAt, Amount, Status, CounterTransactionId
FROM dbo.MoneyOrders
WHERE BranchCode = @BranchCode
ORDER BY CreatedAt DESC;

-- Deliberately non-SARGable comparison; capture plan/reads, then compare with range predicate below.
SELECT BranchCode, SUM(Total) AS Sales
FROM dbo.CounterTransactions
WHERE YEAR(CompletedAt) = YEAR(@Date) AND Status = 'Paid'
GROUP BY BranchCode;

-- SARGable rewrite; same intended year range.
DECLARE @YearStart datetimeoffset(7) = DATETIMEFROMPARTS(YEAR(@Date),1,1,0,0,0,0),
        @NextYear datetimeoffset(7) = DATETIMEFROMPARTS(YEAR(@Date)+1,1,1,0,0,0,0);
SELECT BranchCode, SUM(Total) AS Sales
FROM dbo.CounterTransactions
WHERE CompletedAt >= @YearStart AND CompletedAt < @NextYear AND Status = 'Paid'
GROUP BY BranchCode;
SET STATISTICS IO OFF;
SET STATISTICS TIME OFF;
GO
/* For estimated-vs-actual investigation, inspect actual plan row estimates and run: */
UPDATE STATISTICS dbo.CounterTransactions WITH FULLSCAN;
DBCC SHOW_STATISTICS ('dbo.CounterTransactions', 'IX_CounterTransactions_Branch_Status_CompletedAt') WITH HISTOGRAM;
GO
/* Index write/storage trade-off evidence (run after candidate indexes are applied). */
SELECT i.name, SUM(ps.used_page_count) * 8.0 / 1024 AS UsedMB
FROM sys.indexes i
JOIN sys.dm_db_partition_stats ps ON ps.object_id = i.object_id AND ps.index_id = i.index_id
WHERE i.object_id IN (OBJECT_ID('dbo.BranchStock'), OBJECT_ID('dbo.CounterTransactions'), OBJECT_ID('dbo.Shipments'), OBJECT_ID('dbo.MoneyOrders'))
GROUP BY i.name ORDER BY UsedMB DESC;
GO
