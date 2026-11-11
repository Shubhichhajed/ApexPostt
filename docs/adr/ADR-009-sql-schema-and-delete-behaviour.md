# ADR-009: SQL Server relational schema and delete behaviour

- **Status:** Accepted
- **Context:** JSON files do not provide relational integrity, multi-row atomicity or concurrent stock protection. Week 03 requires a normalized `ApexPostDb` system of record.
- **Decision:** Use SQL Server with separate Branches, Counters, Staff/Roles, Products/ProductCategories, BranchStock/StockAdjustments, ShippingZones/ShippingRates/Shipments, CounterTransactions/TransactionLines/Payments, MoneyOrders/IdentityVerifications and AuditEntries. Add `OutboxMessages` and `SyncInbox` for offline synchronization. Apply primary/foreign keys, unique and check constraints, defaults, audit columns and rowversion.
- **Delete behaviour:** All historical relationships use `NO ACTION`/`RESTRICT`; no cascade deletion of financial, shipment, stock-adjustment or audit history. Lookup rows cannot be deleted while referenced.
- **Alternatives:** One denormalized JSON blob per branch; a shared training DB; cascade deletes. Rejected because they weaken concurrency, traceability or history retention.
- **Consequences:** More explicit mapping and migration work, but the schema protects key invariants independently of MVC validation. UTC instants use `datetimeoffset(7)` to preserve UTC offset explicitly.
