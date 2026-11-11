/* Multi-table sale in one transaction: header, line, payment and stock decrement succeed or fail together.
   Run against a disposable ApexPostDb. Change @BranchCode/@CounterId/@ClerkId/@Sku to rows that exist there. */
USE ApexPostDb;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;   -- any run-time error dooms the transaction, so a half-written sale can never commit

DECLARE @TransactionId varchar(32) = CONCAT('DEMO-TX-', FORMAT(SYSUTCDATETIME(), 'yyyyMMddHHmmss')),
        @BranchCode    varchar(12) = 'IND01',
        @CounterId     varchar(32) = 'IND01-C1',
        @ClerkId       varchar(32) = 'DEMO-CLERK',
        @Sku           varchar(32) = 'APX-001',
        @Quantity      int         = 1,
        @PaymentMethod varchar(16) = 'Cash';

DECLARE @UnitPrice decimal(18,2), @TaxCategory varchar(16), @TaxRate decimal(6,4),
        @Subtotal decimal(18,2), @Tax decimal(18,2), @Total decimal(18,2), @Now datetimeoffset(7) = SYSUTCDATETIME();

BEGIN TRY
    IF @Quantity < 1
        THROW 51101, 'Quantity must be at least 1.', 1;

    -- Price and tax always come from the server-side catalogue, never from the caller.
    SELECT @UnitPrice = p.UnitPrice, @TaxCategory = p.TaxCategory
    FROM dbo.Products AS p
    WHERE p.Sku = @Sku AND p.IsActive = 1;

    IF @UnitPrice IS NULL
        THROW 51102, 'Unknown or inactive SKU.', 1;

    SET @TaxRate = CASE @TaxCategory WHEN 'Standard' THEN 0.1800 WHEN 'Reduced' THEN 0.0500 ELSE 0.0000 END;
    SET @Subtotal = ROUND(@UnitPrice * @Quantity, 2);
    SET @Tax = ROUND(@Subtotal * @TaxRate, 2);
    SET @Total = @Subtotal + @Tax;

    BEGIN TRANSACTION;

    -- Tables are always touched in the same order (CounterTransactions -> BranchStock -> TransactionLines -> Payments)
    -- by every write path, which avoids lock-order deadlocks.
    INSERT dbo.CounterTransactions (Id, BranchCode, CounterId, ClerkId, Subtotal, Tax, Total, PaymentMethod, Status, CreatedAt, CompletedAt)
    VALUES (@TransactionId, @BranchCode, @CounterId, @ClerkId, @Subtotal, @Tax, @Total, @PaymentMethod, 'Paid', @Now, @Now);

    -- The stock rule lives in the UPDATE itself: no row is updated if it would go negative.
    UPDATE dbo.BranchStock WITH (UPDLOCK, ROWLOCK)
       SET Quantity = Quantity - @Quantity,
           UpdatedAt = @Now,
           ModifiedAtUtc = @Now
     WHERE BranchCode = @BranchCode AND Sku = @Sku AND Quantity >= @Quantity;

    IF @@ROWCOUNT <> 1
        THROW 51100, 'Insufficient stock or stock row missing.', 1;

    INSERT dbo.TransactionLines (CounterTransactionId, Id, Kind, Description, Sku, Quantity, UnitPrice, TaxRate, LineTotal)
    SELECT @TransactionId, CONCAT(@TransactionId, '-L1'), 'RetailProduct', p.Name, @Sku, @Quantity, @UnitPrice, @TaxRate, @Total
    FROM dbo.Products AS p
    WHERE p.Sku = @Sku;

    INSERT dbo.Payments (Id, CounterTransactionId, Method, Amount, PaidAtUtc)
    VALUES (CONCAT(@TransactionId, '-P1'), @TransactionId, @PaymentMethod, @Total, @Now);

    COMMIT TRANSACTION;
    SELECT @TransactionId AS TransactionId, @Subtotal AS Subtotal, @Tax AS Tax, @Total AS Total;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
