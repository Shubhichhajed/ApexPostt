# ApexPost Courier – Offline Design

## Why the app saves locally first

Couriers can lose mobile coverage while collecting or delivering parcels. An online-first workflow would prevent a scan from being recorded or risk losing it if the connection drops after the courier action. Therefore, every new scan is written to the embedded Couchbase Lite database first, with `syncStatus = Pending`. The UI can confirm the local write immediately, and the scan remains available after an app restart or while the device is offline. Queries for a tracking number, pending scans and courier scan counts run against the local database and do not require a network connection.

The `OutboxService` later reads Pending documents and submits them to the uploader. Each scan has a stable `scan::{Guid}` ID. An accepted scan is marked `Synced`; an already-known duplicate is also marked `Synced`; a rejected scan is marked `Failed` with its reason. If the upload fails before a per-scan result is received, the scan remains `Pending` so it can be retried safely. A failed scan can be manually reset to Pending with `RetryScanAsync`.

## Conflict handling: Last-write-wins vs. server-decides

**Last-write-wins (LWW)** means that when two devices edit the same mutable document, the version with the later write timestamp wins. It is simple, but clock skew can select the wrong version and one edit can silently overwrite another. LWW is acceptable for low-risk fields where overwriting is tolerable.

**Server-decides** means the server validates competing updates against authoritative business rules. It can reject invalid state transitions, preserve an audit trail and identify the authenticated courier who submitted a scan. It requires a server round trip and more implementation work, but avoids relying on device clocks.

**Recommendation for ApexPost:** Treat scans as append-only events with unique IDs, so two couriers recording different events do not overwrite each other. If the same scan ID is uploaded again, the API deduplicates it. For shipment status and any correction to an existing event, the server should decide using the allowed status-transition rules and the authenticated courier identity. This combines local availability with authoritative server-side decisions.

## Why the embedded database must not be shared by multiple processes

Couchbase Lite is an embedded database owned by the application process. Multiple processes opening and writing the same database files can conflict over database locks, transactions, write-ahead logs and cached or memory-mapped pages. This can cause failed writes, stale reads, damaged indexes or database corruption. One courier app process should own the database. Background synchronization should run inside that same process. Other applications or services should use the API rather than opening the database files directly.

## Change notifications and lifecycle

A collection change listener logs changed document IDs and recalculates the Pending count so the UI can stay current. Its listener token is removed when the listener is disposed and before `CourierDatabase` closes. The local database is named `apexpost_courier`, uses its default collection and creates value indexes on `trackingNumber` and `syncStatus` to support the common offline queries.

## From fake uploader to the real API (Part D)

Part A ships with `FakeShipmentUploader` behind the `IShipmentUploader` interface. The courier client (`src/ApexPost.Courier`) registers `ApiShipmentUploader` for the same interface, so `OutboxService` did not change: it still reads Pending scans and marks them Synced or Failed by scan ID. The only addition is a batch size (20 by default) so that uploads stay small and a dropped connection only affects the current batch. Scans that get no answer stay Pending and the next run sends them again; because the server de-duplicates by scan ID, that resend is harmless.

The courier client keeps its database in its own folder (`%LOCALAPPDATA%/ApexPost/Courier/db`), separate from the Part A console harness, so the two programs never open the same embedded database at the same time.
