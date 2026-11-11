USE ApexPostDb;
GO
-- 1. Daily branch sales: INNER JOIN and conditional aggregation; date predicate is SARGable.
DECLARE @Date date = CONVERT(date, SYSUTCDATETIME()), @BranchCode varchar(12) = NULL;
SELECT t.BranchCode,
       SUM(CASE WHEN l.Kind = 'RetailProduct' THEN l.LineTotal ELSE 0 END) AS RetailSales,
       SUM(CASE WHEN l.Kind <> 'RetailProduct' THEN l.LineTotal ELSE 0 END) AS ServiceSales,
       SUM(l.LineTotal) AS TotalSales, COUNT(DISTINCT t.Id) AS TransactionCount
FROM dbo.CounterTransactions AS t
INNER JOIN dbo.TransactionLines AS l ON l.CounterTransactionId = t.Id
WHERE t.Status = 'Paid' AND t.CompletedAt >= @Date AND t.CompletedAt < DATEADD(day, 1, @Date)
  AND (@BranchCode IS NULL OR t.BranchCode = @BranchCode)
GROUP BY t.BranchCode;
GO
-- 2. Branches with no paid sales today: LEFT JOIN preserves branches with no matching sale.
DECLARE @Today date = CONVERT(date, SYSUTCDATETIME());
SELECT b.Code, b.Name
FROM dbo.Branches AS b
LEFT JOIN dbo.CounterTransactions AS t
  ON t.BranchCode = b.Code AND t.Status = 'Paid'
 AND t.CompletedAt >= @Today AND t.CompletedAt < DATEADD(day, 1, @Today)
WHERE b.Status = 'Active' AND t.Id IS NULL;
GO
-- 3. Top 10 products by revenue in each region.
WITH ProductRevenue AS
(
 SELECT b.Region, p.Sku, p.Name,
        SUM(l.LineTotal) AS Revenue,
        ROW_NUMBER() OVER(PARTITION BY b.Region ORDER BY SUM(l.LineTotal) DESC, p.Sku) AS RegionRank
 FROM dbo.TransactionLines AS l
 INNER JOIN dbo.CounterTransactions AS t ON t.Id = l.CounterTransactionId AND t.Status = 'Paid'
 INNER JOIN dbo.Branches AS b ON b.Code = t.BranchCode
 INNER JOIN dbo.Products AS p ON p.Sku = l.Sku
 WHERE l.Kind = 'RetailProduct'
 GROUP BY b.Region, p.Sku, p.Name
)
SELECT Region, Sku, Name, Revenue, RegionRank FROM ProductRevenue WHERE RegionRank <= 10 ORDER BY Region, RegionRank;
GO
-- 4. Shipping volume by zone and service level.
SELECT DestinationZone, ServiceLevel, COUNT_BIG(*) AS ShipmentCount, SUM(Price) AS Revenue
FROM dbo.Shipments GROUP BY DestinationZone, ServiceLevel HAVING COUNT_BIG(*) > 0;
GO
-- 5. Money orders and identity-verification outcomes per branch.
SELECT b.Code AS BranchCode, COUNT(DISTINCT mo.Id) AS MoneyOrders,
       COUNT(DISTINCT CASE WHEN iv.Result = 1 THEN iv.Id END) AS SuccessfulVerifications,
       COUNT(DISTINCT CASE WHEN iv.Result = 2 THEN iv.Id END) AS FailedVerifications
FROM dbo.Branches AS b
LEFT JOIN dbo.MoneyOrders AS mo ON mo.BranchCode = b.Code
LEFT JOIN dbo.IdentityVerifications AS iv ON iv.BranchCode = b.Code
GROUP BY b.Code;
GO
-- SARGability demonstration: avoid YEAR(CompletedAt) = @Year.
DECLARE @Start datetimeoffset(7) = '2026-01-01T00:00:00+00:00', @End datetimeoffset(7) = '2027-01-01T00:00:00+00:00';
SELECT BranchCode, SUM(Total) AS Sales FROM dbo.CounterTransactions
WHERE Status = 'Paid' AND CompletedAt >= @Start AND CompletedAt < @End GROUP BY BranchCode;
GO
