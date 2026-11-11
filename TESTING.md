# Tests

```powershell
dotnet test tests/ApexPost.UnitTests        # no database needed
dotnet test tests/ApexPost.Courier.Tests    # no API or database needed (HTTP is stubbed)
dotnet test tests/ApexPost.IntegrationTests # needs SQL Server (creates and drops its own test database)
```

`scripts/Run-Tests.ps1` / `scripts/run-tests.sh` run everything in Release and write a TRX report to `TestResults/`.

## ApexPost.UnitTests

| Area | Tests |
|---|---|
| Domain rules (Week 2–3) | stock never negative, inactive staff, branch rules, money-order identity threshold, parcel weight limits, pricing, value objects |
| Application services | unit of work commits once on success and never on failure (spy UoW) |
| API request validation | data-driven matrix (`ApiRequestValidationMatrixTests`): tracking-number pattern, weight 0.1–70, phone format, required recipient fields, status values, scan ID format, scan type, future scan time, Delivered-needs-signature rule, batch size 1–100, route constraint |
| API authorization metadata | Courier-only upload/PATCH, Admin-only create/update/delete, any-authenticated reads, v1 deprecated / v2 current, partner controller marked for API-key middleware |
| Global exception handler | 404/409/400 mapping to ProblemDetails, 500 hides the message outside Development |
| Partner API key | constant-time validator; middleware: unmarked endpoint untouched, missing/wrong key 401, valid key passes, no configured key 500 |
| Offline outbox (Part A) | outcome→status mapping by scan ID, empty outbox, incomplete response keeps Pending, unknown IDs rejected, retry of failed scans, **batches of 20**, interrupted upload stops and keeps Pending, scan ID format |

## ApexPost.Courier.Tests (Part D-II)

| Area | Tests |
|---|---|
| `AuthHeaderHandler` | adds `Bearer` token from the store; no header when logged out; **401 clears the token and raises SessionExpired**; 403 keeps the token |
| Resilience pipeline | 408/5xx transient, every 4xx (incl. 429) not; network errors/attempt timeouts transient; only idempotent requests retried; upload retried on 503 then succeeds; 400/403/404/409 never retried; non-idempotent POST not retried; **circuit opens after repeated failures and then fails fast without calling the server** |
| JSON / ProblemDetails | source-generated camelCase serialisation; unknown and null fields tolerated; malformed body → friendly error; `ValidationProblemDetails` → readable message; non-JSON error page handled; 401 → session expired |
| Uploader | rejection reasons matched to scan IDs; accepted/rejected per scan; one POST per tracking number; permanent 4xx marks the batch Failed; 5xx leaves scans Pending and stops |
| Helpers | courierId/expiry read from a JWT payload; base URL per platform and HTTPS-only |

## ApexPost.IntegrationTests (Week 3, SQL Server)

`WebApplicationFactory` tests against a disposable database (LocalDB by default, or `APEXPOST_TEST_CONNECTION`): security headers, API key 401/403, login rate limit 429, redirect to login, clerk 403 on void, correlation ID, audit entries, rollback of a failed sale, rowversion conflict → 409, stored procedures and seed volumes.

## Manual checks for the demo

- `ApexPost.Api.http` – success and failure request for each endpoint in v1 and v2, plus 401/403/invalid API key/valid token.
- Swagger UI → Authorize with a `/dev/token` token → "Try it out" on each endpoint.
- Courier client: airplane mode / stop the API → add scans → reconnect → automatic sync; watch the circuit open and close in the log and on Sync Status.
