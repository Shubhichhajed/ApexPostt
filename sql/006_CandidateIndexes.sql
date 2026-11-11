USE ApexPostDb;
GO
/* Candidate workload indexes. They are deployed in migration 202611010002. */
CREATE INDEX IX_BranchStock_Branch_Quantity ON dbo.BranchStock(BranchCode, Quantity) INCLUDE(Sku);
CREATE INDEX IX_CounterTransactions_Branch_Status_CompletedAt ON dbo.CounterTransactions(BranchCode, Status, CompletedAt) INCLUDE(Total);
CREATE INDEX IX_Shipments_Zone_Service_CreatedAt ON dbo.Shipments(DestinationZone, ServiceLevel, CreatedAt DESC) INCLUDE(BranchCode, WeightKg, Price);
CREATE INDEX IX_MoneyOrders_Branch_CreatedAt ON dbo.MoneyOrders(BranchCode, CreatedAt DESC) INCLUDE(Amount, Status, CounterTransactionId);
CREATE INDEX IX_IdentityVerifications_Branch_VerifiedAt ON dbo.IdentityVerifications(BranchCode, VerifiedAt DESC) INCLUDE(Result, CounterTransactionId);
CREATE INDEX IX_CounterTransactions_OpenBranch ON dbo.CounterTransactions(BranchCode, CreatedAt) WHERE Status = 'Open';
GO

CREATE INDEX IX_StockAdjustments_Branch_ChangedAt ON dbo.StockAdjustments(BranchCode, ChangedAtUtc DESC) INCLUDE(Sku, Delta, ChangedBy);
