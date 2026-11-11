# ADR-014: Stock concurrency and offline conflict resolution

- **Status:** Accepted
- **Context:** Multiple terminals may sell the last stock unit, and offline terminals may replay stale operations.
- **Decision:** Defer stock decrement until payment completion. The payment use case checks all required SKUs, adjusts stock, marks the transaction paid and inserts Payment in one SaveChanges transaction. `BranchStock` and `CounterTransactions` use SQL Server rowversion; stale writes become HTTP 409. Voids/refunds restore stock once because only Paid transactions can enter those transitions. Open abandoned baskets do not reserve inventory.
- **Offline policy:** Every terminal operation carries `(ClientId, OperationId)` and is deduplicated in `SyncInbox`. Stock, payments and counter transactions use server-wins plus a client refresh; conflicting profile/catalogue changes are held for manual review. Client timestamps are informational, not trusted for financial ordering.
- **Alternatives:** Last-write-wins for all data; client-side stock decrement; global in-process semaphore as the only lock. Rejected because they can overwrite financial state or fail across multiple app instances.
- **Consequences:** A stale terminal must refresh after 409. Outbox messages are persisted in the same commit and delivered at least once; consumers deduplicate by message ID.
