# Courier Client – Resilience (Polly via Microsoft.Extensions.Http.Resilience)

Every call from `ApexApiClient` goes through one resilience pipeline registered with
`AddHttpClient<ApexApiClient>().AddHttpMessageHandler<AuthHeaderHandler>().AddResilienceHandler("apexpost-api", ...)`.
Code: `src/ApexPost.Courier/Resilience/ApexApiResilience.cs`. Values: `src/ApexPost.Courier/appsettings.json` → `Resilience`.

```
request ─► AuthHeaderHandler ─► [ total timeout 45 s
                                   └► retry (3, exponential + jitter)
                                        └► circuit breaker
                                             └► per-attempt timeout 5 s ] ─► HTTPS ─► ApexPost.Api
```

The order matters: the total timeout caps the whole logical call including back-off; the retry sits outside the breaker so that once the circuit opens the retry stops immediately instead of hammering it; the per-attempt timeout is innermost so a single hung connection is cut off and counted as a failure.

## Chosen values and why

| Setting | Value | Reason |
|---|---|---|
| Retry attempts | **3** (4 tries in total) | Enough to ride out a short blip (Wi-Fi hand-over, app pool restart) without making the courier wait long. |
| Back-off | **Exponential from 1 s, with jitter** (≈1 s, 2 s, 4 s) | Gives the server time to recover; jitter stops many couriers retrying in lock-step after an outage. |
| What is retried | Network errors (`HttpRequestException`), attempt timeouts, **408** and **5xx** only | These can succeed on a resend. A **4xx is never retried**: 400/403/404/409 will fail the same way every time, 401 needs a new login, 429 asks us to back off. |
| Which requests | GET/PUT/DELETE, plus POSTs explicitly marked idempotent (the scan upload) | Retrying a non-idempotent request could create duplicates. `POST /dev/token` is not marked and is not retried. |
| Per-attempt timeout | **5 s** | A healthy API answers in well under a second; 5 s allows a slow mobile link but does not let one stuck request eat the whole budget. |
| Total timeout | **45 s** | Covers 4 × 5 s attempts plus ≈7 s of back-off with headroom. The `HttpClient.Timeout` is set to infinite so the pipeline owns all timeouts. |
| Circuit breaker | Opens when **≥ 50 %** of at least **4** calls fail within **30 s**; stays open **20 s** | One fully failed logical call (4 attempts) is enough evidence the server is down. While open, calls fail instantly with `BrokenCircuitException`, scans keep being saved locally and the screen shows **"Server unavailable – working offline"**. After 20 s one trial call is let through (half-open); success closes the circuit. |
| Sync batch size | **20** scans per upload | Keeps request bodies small on poor networks; a failure only affects one batch. |
| Health probe | `GET /health` every **5 s**, 3 s timeout, *no* resilience handler | Detects "API back" quickly; it bypasses the breaker so recovery is noticed even while the circuit is open. |

## Why the scan upload is safe to retry

`POST /api/v2/shipments/{trackingNumber}/scans` is normally not idempotent, but ours is made idempotent on purpose:

1. Each scan is created on the device with a permanent ID `scan::{Guid}` (Part A).
2. The server stores the scan ID as the **primary key** of `ShipmentApiScans` and inserts inside a serializable transaction; a repeated ID is counted in `duplicatesSkipped` instead of being inserted (Part B6).
3. So if the first attempt reached the server but the response was lost (timeout, dropped connection) the retry simply reports the scan as a duplicate. The outcome is the same as one successful call.

The client marks this request with `ApexApiResilience.IdempotentRequest`; the retry predicate only retries POSTs that carry that marker.

## Logging

- Every retry: `Retry {Attempt} of {MaxAttempts} for {Method} {Path} in {DelayMs} ms after {Reason}` (Warning).
- Every circuit change: `Circuit OPEN …` (Error), `Circuit HALF-OPEN …` (Warning), `Circuit CLOSED …` (Information).
- Attempt timeouts: `Attempt timed out after {TimeoutSeconds} s`.
- Connectivity: `Connectivity changed: ONLINE/OFFLINE`, and each sync result.
- Tokens, API keys and request bodies are never logged.

The Sync Status screen shows the same information: online/offline, circuit state, last sync time, last result, consecutive failures and the retry count.

## Offline sync flow (D9)

1. A new scan is saved in Couchbase Lite as `Pending` (works with no network).
2. `ConnectivityMonitor` combines the OS network event (`NetworkChange.NetworkAvailabilityChanged`) with a `/health` probe and raises `ConnectivityChanged`.
3. When the state becomes online, `SyncCoordinator` runs `OutboxService.SyncPendingScansAsync(batchSize: 20)` using the real `ApiShipmentUploader` (which replaced Part A's fake uploader).
4. Each scan is marked from the API response: in `rejectionReasons` or a permanent 4xx → `Failed`; accepted or duplicate → `Synced`; no answer (network/5xx/circuit open) → stays `Pending` and the run stops.
5. The Part A collection change listener fires for every status change and keeps the pending count on screen up to date.

## MAUI to console mapping

The trainer allowed a .NET console client instead of MAUI for Part D-II. Everything except the UI technology is the same:

| Brief (MAUI) | This project (`ApexPost.Courier`) |
|---|---|
| `builder.Services.AddHttpClient<ApexApiClient>()` | identical (`Program.cs`) |
| `AuthHeaderHandler : DelegatingHandler` reading `SecureStorage` | identical handler reading `ITokenStore` |
| `SecureStorage` | `ProtectedTokenStore` (Data Protection, DPAPI on Windows) |
| `Connectivity.ConnectivityChanged` | `ConnectivityMonitor.ConnectivityChanged` (OS network event + `/health` probe) |
| Android emulator / Windows base URLs | `ApexApi:BaseUrls:AndroidEmulator` / `ApexApi:BaseUrls:Windows` |
| Pages: Login, New Scan, Pending Scans, Sync Status | console screens with the same names (`Screens/CourierConsole.cs`) |
| Offline banner | yellow banner line printed above every screen while offline/circuit open |
