USE ApexPostDb;
GO
CREATE OR ALTER PROCEDURE dbo.usp_DailyBranchSales
    @BranchCode varchar(12), @Date date
AS
BEGIN
    SET NOCOUNT ON;
    IF @Date IS NULL THROW 51001, 'Date is required.', 1;
    IF @BranchCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE Code = @BranchCode)
        THROW 51002, 'Branch code does not exist.', 1;
    SELECT t.BranchCode,
           CAST(SUM(CASE WHEN l.Kind = 'RetailProduct' THEN l.LineTotal ELSE 0 END) AS decimal(18,2)) AS RetailSales,
           CAST(SUM(CASE WHEN l.Kind <> 'RetailProduct' THEN l.LineTotal ELSE 0 END) AS decimal(18,2)) AS ServiceSales,
           CAST(SUM(l.LineTotal) AS decimal(18,2)) AS TotalSales,
           COUNT(DISTINCT t.Id) AS TransactionCount
    FROM dbo.CounterTransactions t
    INNER JOIN dbo.TransactionLines l ON l.CounterTransactionId = t.Id
    WHERE t.Status = 'Paid' AND t.CompletedAt >= @Date AND t.CompletedAt < DATEADD(day, 1, @Date)
      AND (@BranchCode IS NULL OR t.BranchCode = @BranchCode)
    GROUP BY t.BranchCode;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_LowStockReport
    @BranchCode varchar(12) = NULL, @Threshold int = 5
AS
BEGIN
    SET NOCOUNT ON;
    IF @Threshold < 0 THROW 51003, 'Threshold cannot be negative.', 1;
    IF @BranchCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE Code = @BranchCode)
        THROW 51004, 'Branch code does not exist.', 1;
    SELECT s.BranchCode, s.Sku, p.Name AS ProductName, s.Quantity
    FROM dbo.BranchStock s INNER JOIN dbo.Products p ON p.Sku = s.Sku
    WHERE (@BranchCode IS NULL OR s.BranchCode = @BranchCode) AND s.Quantity <= @Threshold
    ORDER BY s.BranchCode, s.Quantity, s.Sku
    OPTION (RECOMPILE); -- avoids a single cached plan being poor for NULL/all-branch vs one-branch selectivity.
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_AdjustStock
    @BranchCode varchar(12), @Sku varchar(32), @Delta int, @Reason nvarchar(250), @ChangedBy varchar(32)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NULLIF(LTRIM(RTRIM(@BranchCode)), '') IS NULL THROW 51005, 'BranchCode is required.', 1;
    IF NULLIF(LTRIM(RTRIM(@Sku)), '') IS NULL THROW 51006, 'SKU is required.', 1;
    IF @Delta = 0 THROW 51007, 'Delta must not be zero.', 1;
    IF LEN(LTRIM(RTRIM(ISNULL(@Reason, N'')))) < 5 THROW 51008, 'A reason of at least five characters is required.', 1;
    IF NULLIF(LTRIM(RTRIM(@ChangedBy)), '') IS NULL THROW 51009, 'ChangedBy is required.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.Staff WHERE Id = @ChangedBy AND Status = 'Active' AND (Role = 'Administrator' OR (Role = 'BranchManager' AND HomeBranchCode = @BranchCode))) THROW 51011, 'An active manager for this branch or an administrator must authorize stock adjustments.', 1;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE dbo.BranchStock WITH (UPDLOCK, ROWLOCK)
           SET Quantity = Quantity + @Delta, UpdatedAt = SYSUTCDATETIME(), ModifiedAtUtc = SYSUTCDATETIME()
         WHERE BranchCode = @BranchCode AND Sku = @Sku AND Quantity + @Delta >= 0;
        IF @@ROWCOUNT <> 1 THROW 51010, 'Stock row was not found or adjustment would make stock negative.', 1;
        INSERT dbo.StockAdjustments(Id, BranchCode, Sku, Delta, Reason, ChangedBy, ChangedAtUtc)
        VALUES (LOWER(REPLACE(CONVERT(varchar(36), NEWID()), '-', '')), @BranchCode, @Sku, @Delta, @Reason, @ChangedBy, SYSUTCDATETIME());
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_ShippingVolumeByZone @BranchCode varchar(12) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF @BranchCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE Code = @BranchCode) THROW 51011, 'Branch code does not exist.', 1;
    SELECT s.DestinationZone AS Zone, s.ServiceLevel, COUNT(*) AS Count
    FROM dbo.Shipments s WHERE @BranchCode IS NULL OR s.BranchCode = @BranchCode
    GROUP BY s.DestinationZone, s.ServiceLevel;
END;
GO
CREATE OR ALTER PROCEDURE dbo.usp_MoneyOrderAuditByBranch
AS
BEGIN
    SET NOCOUNT ON;
    SELECT b.Code AS BranchCode, COUNT(DISTINCT mo.Id) AS MoneyOrders,
      COUNT(DISTINCT CASE WHEN iv.Result = 1 THEN iv.Id END) AS SuccessfulVerifications,
      COUNT(DISTINCT CASE WHEN iv.Result = 2 THEN iv.Id END) AS FailedVerifications
    FROM dbo.Branches b LEFT JOIN dbo.MoneyOrders mo ON mo.BranchCode = b.Code
    LEFT JOIN dbo.IdentityVerifications iv ON iv.BranchCode = b.Code
    GROUP BY b.Code;
END;
GO
