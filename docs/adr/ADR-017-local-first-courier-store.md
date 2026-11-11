# ADR-017: Local-first courier store with Couchbase Lite and an outbox

- **Status:** Accepted
- **Context:** Couriers lose coverage but must still record pickups and deliveries. Scans must survive app restarts and be uploaded later without duplicates.
- **Decision:** Every scan is written first to an embedded Couchbase Lite database (`apexpost_courier`, default collection) as a JSON document with ID `scan::{Guid}` and `syncStatus = Pending`. `IScanRepository` hides Couchbase types from the rest of the code. `OutboxService` reads Pending scans and marks them Synced/Failed from per-scan outcomes; anything without an outcome stays Pending. Value indexes on `trackingNumber` and `syncStatus` back the two main SQL++ queries; all queries use parameters.
- **Alternatives:** Online-only (loses scans offline); SQLite with hand-written JSON columns (more mapping code, no built-in change listener); full Couchbase Sync Gateway (not needed this week and the brief excludes it).
- **Consequences:** Saving a scan never depends on the network. The scan ID doubles as the idempotency key for the API (ADR-018). One process must own the database file, so the Part A harness and the courier client use different folders.
