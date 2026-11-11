# API Versioning, Deprecation and Contracts

URL-segment versioning with Asp.Versioning: `/api/v1/...` and `/api/v2/...`. Both versions call the same `IShipmentService`; only the response DTOs and the mapping (`ShipmentResponseMapper`) differ. `ReportApiVersions = true`, so every response carries `api-supported-versions: 2.0` and `api-deprecated-versions: 1.0`.

| Version | Status | Contract file |
|---|---|---|
| v1 | **Deprecated** (`[ApiVersion("1.0", Deprecated = true)]`), sunset **30 June 2027** | `contracts/v1.json` |
| v2 | Current | `contracts/v2.json` |

## Change list (v1 → v2)

| # | Change | Breaking? | Why |
|---|---|---|---|
| 1 | `recipientName`, `recipientPhone`, `recipientAddress`, `recipientCity` moved into a nested `recipient { name, phone, address, city }` object | **Breaking** | Existing JSON paths disappear; a v1 client reading `recipientName` would get nothing. |
| 2 | `status` is a string enum (`Created`, `PickedUp`, `InTransit`, `OutForDelivery`, `Delivered`, `Failed`) instead of free text | **Breaking** for generated clients | The OpenAPI type changes from `string` to an enum, so generated SDKs get a different type and must handle a closed set of values. |
| 3 | New `latestScan { scanType, location, scannedAtUtc }` (nullable) | Non-breaking | Adding an optional field does not affect clients that ignore unknown fields. |
| 4 | New read-only `GET /api/v2/partner/shipments/{trackingNumber}` with `X-Api-Key` | Non-breaking | New endpoint; nothing existing changes. |
| 5 | Request bodies (`CreateShipmentRequest`, `UpdateShipmentRequest`, `UpdateStatusRequest`, `ScanUploadRequest`) | Unchanged | Same validation rules in both versions. |
| 6 | v1 marked deprecated + `api-deprecated-versions` / `Sunset` headers | Non-breaking | Tells callers to migrate without changing the payload. |

Because changes 1 and 2 are breaking they go into a new version; v1 keeps the Part B contract exactly as it was.

## Deprecation and sunset plan for v1

1. **Now:** v1 is marked deprecated. Swagger UI lists it as "v1 (deprecated)", every operation in `v1.json` has `deprecated: true`, and responses include `api-deprecated-versions: 1.0` and a `Sunset` header with a link to the policy page.
2. **Notify:** publish the v2 contract and generated SDK; email known consumers with the change list above.
3. **Monitor:** watch request logs for `/api/v1/` traffic per client and contact remaining users.
4. **Freeze:** no new features in v1; only security fixes.
5. **Sunset – 30 June 2027:** v1 routes start returning 410 Gone with a ProblemDetails body pointing to v2, then are removed in the next release.

## Keeping contracts and the SDK in step

1. Change the API, run it in Development.
2. `tools/refresh-contracts.sh` downloads `/openapi/v1.json` and `/openapi/v2.json` into `contracts/`.
3. Review the diff of `contracts/*.json` – it shows exactly what changed for clients.
4. Rebuild `src/ApexPost.Client`; NSwag regenerates the SDK from `contracts/v2.json`.
