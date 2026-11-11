# ApexPost Logistics API – Endpoint Notes

Base URL (Development): `https://localhost:7001`. Routes are versioned by URL segment (`v1` deprecated, `v2` current). `{trackingNumber}` must match `APX-` + 8 digits (route constraint), otherwise the route does not match and a 404 ProblemDetails is returned. Every error body is RFC 7807 ProblemDetails; validation errors are `ValidationProblemDetails`.

## Endpoints (same set in v1 and v2 unless noted)

| Verb | Route | Purpose | Auth | Success | Errors |
|---|---|---|---|---|---|
| GET | `/api/v{n}/shipments?page=&pageSize=&status=&city=` | Paged, filtered list (`pageSize` default 20, max 50) | any token | 200 | 400 invalid paging, 401 |
| GET | `/api/v{n}/shipments/{trackingNumber}` | One shipment | any token | 200 | 401, 404 unknown tracking number |
| POST | `/api/v{n}/shipments` | Create shipment | Admin | **201** + `Location` | 400 validation, 401, 403, 409 duplicate tracking number, 415 wrong content type |
| PUT | `/api/v{n}/shipments/{trackingNumber}` | Replace editable details (only while `Created`) | Admin | 200 | 400, 401, 403, 404, 409 not editable, 415 |
| PATCH | `/api/v{n}/shipments/{trackingNumber}/status` | Change status only | Courier | 200 | 400, 401, 403, 404, 409 invalid transition, 415 |
| DELETE | `/api/v{n}/shipments/{trackingNumber}` | Delete (only while `Created`) | Admin | **204** | 401, 403, 404, 409 not in Created |
| POST | `/api/v{n}/shipments/{trackingNumber}/scans` | Idempotent bulk upload of offline scans | Courier (own courierId) | 200 `{accepted, duplicatesSkipped, rejected, rejectionReasons}` | 400, 401, 403 other courier, 404, 415 |
| GET | `/api/v{n}/shipments/{trackingNumber}/scans` | Scan history, newest first | any token | 200 | 401, 404 |
| GET | `/api/v2/partner/shipments/{trackingNumber}` | Read-only partner tracking (v2 only) | `X-Api-Key` | 200 | 401 missing/invalid key, 404, 500 key not configured |
| GET | `/health` | Liveness for the courier client | none | 200 | – |
| POST | `/dev/token` | Development-only token issuer (not in OpenAPI) | none | 200 | 400 |

Any unhandled exception becomes a 500 ProblemDetails from the global `IExceptionHandler` (no stack trace outside Development).

## Status transitions

`Created → PickedUp → InTransit → OutForDelivery → Delivered | Failed`. Anything else (skipping a step, going backwards, changing a final status) returns **409**.

## Validation rules

| Rule | Where |
|---|---|
| Tracking number `^APX-\d{8}$` | `[RegularExpression]` on `CreateShipmentRequest`, route constraint on paths |
| Weight 0.1–70 kg | `[Range]` |
| Recipient name, phone, address, city required | `[Required]` + `[StringLength]` |
| Phone format: 7–15 digits, optional `+`, spaces, `()`, `.`, `-` | custom `[InternationalPhone]` attribute |
| Scan time not in the future; a `Delivered` scan needs the recipient signature name in `notes` | `IValidatableObject` on `ScanItemRequest` (custom rules) |
| Scan ID format `scan::{Guid}`, scan type one of Pickup/InTransit/Delivered/Failed, 1–100 scans per upload | attributes on `ScanUploadRequest` / `ScanItemRequest` |

Requests are bound to the four request DTOs only (`CreateShipmentRequest`, `UpdateShipmentRequest`, `UpdateStatusRequest`, `ScanUploadRequest`), never to EF entities, so fields like status or audit columns cannot be over-posted.

## v1 vs v2 responses

```jsonc
// v1 (deprecated)
{ "trackingNumber": "APX-10000001", "status": "InTransit", "weight": 2.5,
  "recipientName": "Alex Morgan", "recipientPhone": "+44 20 7946 0958",
  "recipientAddress": "100 Example Avenue", "recipientCity": "Springfield",
  "notes": null, "createdAtUtc": "2026-11-10T09:00:00Z" }

// v2
{ "trackingNumber": "APX-10000001", "status": "InTransit", "weight": 2.5,
  "recipient": { "name": "Alex Morgan", "phone": "+44 20 7946 0958", "address": "100 Example Avenue", "city": "Springfield" },
  "latestScan": { "scanType": "Pickup", "location": "Springfield Depot", "scannedAtUtc": "2026-11-10T09:30:00Z" },
  "notes": null, "createdAtUtc": "2026-11-10T09:00:00Z" }
```

## Idempotent scan upload

Each scan carries its Part A ID. The ID is the primary key of the API's scan table, so a re-upload is counted in `duplicatesSkipped` and nothing is inserted twice; the check is done inside a serializable transaction and a concurrent insert of the same ID is also reported as a duplicate. Invalid individual scans are counted in `rejected` with a reason prefixed by the scan ID (`"scan::…: reason"`).
