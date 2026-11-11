# ADR-019: Courier client – console host, typed HttpClient, resilience and token storage

- **Status:** Accepted
- **Context:** Part D-II asks for a .NET MAUI courier app. Following the trainer's guidance the client is delivered as a .NET console app, but it must still show IHttpClientFactory, an auth DelegatingHandler, Polly retry/circuit-breaker/timeouts, source-generated JSON, secure token storage and connectivity-triggered sync.
- **Decision:**
  - Generic Host console app (`ApexPost.Courier`) reusing the Part A library unchanged; the fake uploader is replaced by `ApiShipmentUploader` through the same `IShipmentUploader` interface.
  - `AddHttpClient<ApexApiClient>()` + `AuthHeaderHandler` + `AddResilienceHandler` (total timeout → retry → circuit breaker → attempt timeout). Only network errors, 408 and 5xx are retried, only for idempotent requests; the scan upload is marked idempotent because of ADR-018.
  - Token kept in `ProtectedTokenStore` (ASP.NET Core Data Protection, DPAPI-protected keys on Windows) – the console counterpart of MAUI SecureStorage. A 401 clears it and returns to Login.
  - `ConnectivityMonitor` = OS `NetworkChange` event + `/health` probe, standing in for MAUI `Connectivity.ConnectivityChanged`; it triggers sync in batches of 20.
  - JSON uses a `JsonSerializerContext` (camelCase, case-insensitive, string enums) shared by all calls.
- **Alternatives:** Build the MAUI app (not required for this cohort); `new HttpClient()` per call (socket exhaustion, no handler pipeline); hand-written retry loops (no circuit breaker, harder to test); storing the token in a plain settings file (readable by anything on the machine).
- **Consequences:** The same services would move into a MAUI project with only the UI and the two platform adapters (`ITokenStore`, connectivity) swapped. The resilience behaviour is covered by unit tests with a stubbed HTTP handler.
