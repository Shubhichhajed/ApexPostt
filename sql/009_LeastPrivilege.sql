USE ApexPostDb;
GO
IF DATABASE_PRINCIPAL_ID('ApexPostAppRole') IS NULL CREATE ROLE ApexPostAppRole;
GO
/* Read access for the query services and narrowly scoped write access for app use cases. */
GRANT SELECT ON SCHEMA::dbo TO ApexPostAppRole;
GRANT INSERT, UPDATE ON dbo.Branches TO ApexPostAppRole;
GRANT INSERT, UPDATE ON dbo.Counters TO ApexPostAppRole;
GRANT INSERT, UPDATE ON dbo.Staff TO ApexPostAppRole;
GRANT INSERT, UPDATE ON dbo.Products TO ApexPostAppRole;
GRANT INSERT ON dbo.ShippingRates TO ApexPostAppRole;
GRANT INSERT, UPDATE ON dbo.BranchStock TO ApexPostAppRole;
GRANT INSERT, UPDATE ON dbo.CounterTransactions TO ApexPostAppRole;
GRANT INSERT ON dbo.TransactionLines TO ApexPostAppRole;
GRANT INSERT ON dbo.Payments TO ApexPostAppRole;
GRANT INSERT ON dbo.Shipments TO ApexPostAppRole;
GRANT INSERT, UPDATE ON dbo.MoneyOrders TO ApexPostAppRole;
GRANT INSERT ON dbo.IdentityVerifications TO ApexPostAppRole;
GRANT INSERT ON dbo.AuditEntries TO ApexPostAppRole;
GRANT INSERT, UPDATE ON dbo.OutboxMessages TO ApexPostAppRole;
GRANT INSERT ON dbo.SyncInbox TO ApexPostAppRole;
GRANT EXECUTE ON OBJECT::dbo.usp_DailyBranchSales TO ApexPostAppRole;
GRANT EXECUTE ON OBJECT::dbo.usp_LowStockReport TO ApexPostAppRole;
GRANT EXECUTE ON OBJECT::dbo.usp_AdjustStock TO ApexPostAppRole;
GRANT EXECUTE ON OBJECT::dbo.usp_ShippingVolumeByZone TO ApexPostAppRole;
GRANT EXECUTE ON OBJECT::dbo.usp_MoneyOrderAuditByBranch TO ApexPostAppRole;
DENY DELETE ON SCHEMA::dbo TO ApexPostAppRole;
DENY ALTER ON SCHEMA::dbo TO ApexPostAppRole;
GO
/* Add the runtime database user to ApexPostAppRole separately. Run migrations with a separate deployment identity. */
