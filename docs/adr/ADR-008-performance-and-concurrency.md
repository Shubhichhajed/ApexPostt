# ADR-008: Bounded Concurrency, Async I/O and Cache (Week 2)

- **Status:** Partially superseded in Week 3 by ADR-014; async/report/cache decisions remain accepted.
- **Context:** The Week 2 app needed responsive async I/O and protection for shared JSON files/stock updates. Courier integrations were simulated and all concurrency controls were process-local.
- **Decision:** Propagate cancellation tokens, use `Task.WhenAll` for independent dashboard/report reads, `SemaphoreSlim` for same-process file/rate operations, a bounded courier concurrency limit and a `ValueTask` rate cache. Apply login/partner rate limits and explicit timeouts to simulated lookups.
- **Alternatives considered:** Synchronous `.Wait()`/`.Result`; unbounded external calls; a single global lock for all branches; caching every read without a workload. Rejected because they block threads, risk resource exhaustion, serialize unrelated branches or add stale state without evidence.
- **Consequences:** These controls bounded a single process but were not distributed locks. Week 3 removes the global stock lock and uses SQL Server rowversion plus transactions for cross-instance stock concurrency. The singleton rate cache now depends on `IDbContextFactory`, while the courier service is scoped because it uses a scoped shipment repository. Cache and index benefits still require measured SQL Server evidence.
