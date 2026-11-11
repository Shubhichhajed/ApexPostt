# ADR-015: Audit store and transactional outbox boundaries

- **Status:** Accepted
- **Context:** Audit logging must continue across the SQL migration and must not roll back a valid business transaction. Offline terminals need durable event publication.
- **Decision:** `EfAuditStore` uses a new factory-created DbContext and writes to `AuditEntries` independently. `AuditActionFilter` catches and logs audit-write failures without failing the business action. `ChangeTrackingOutboxInterceptor` captures safe aggregate change envelopes during EF SaveChanges and adds them to `OutboxMessages` in the same transaction as the business changes. A background dispatcher simulates publication and marks successful messages processed.
- **Alternatives:** Put audit writes in the business DbContext; publish directly to a network transport before commit; use in-memory events only. Rejected because audit failure should not roll back the sale, and publishing before commit can expose uncommitted state.
- **Consequences:** Audit durability is independent and may lag/fail; failures are critical logs and require operational monitoring. Outbox delivery is at-least-once; real consumers must deduplicate and production transport replaces the simulated log sink.
