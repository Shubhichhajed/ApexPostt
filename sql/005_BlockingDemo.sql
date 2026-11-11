/* Session A: hold a stock row lock briefly. Run this batch in SSMS window 1. */
USE ApexPostDb;
BEGIN TRANSACTION;
UPDATE dbo.BranchStock SET Quantity = Quantity WHERE BranchCode = 'IND01' AND Sku = 'APX-001';
WAITFOR DELAY '00:00:15';
COMMIT TRANSACTION;
GO
/* Session B: while A is waiting, run this in SSMS window 2. Under READ COMMITTED this read may block until A commits. */
USE ApexPostDb;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
SELECT Quantity FROM dbo.BranchStock WHERE BranchCode = 'IND01' AND Sku = 'APX-001';
GO
