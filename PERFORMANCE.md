# ApexPost Week 3 — Performance and query evidence

## Measurement integrity

No logical-read, CPU, elapsed-time, plan-shape, storage, or estimated/actual-row numbers are hard-coded in this report. Those measurements must be captured against the assessor's SQL Server instance and the seeded 200+ transaction dataset. This environment did not have the .NET SDK or a SQL Server instance available, so actual `.sqlplan` files and runtime measurements are intentionally not fabricated. The capture scripts and application diagnostics are included below.

## Workloads and candidate indexes

| Workload | Predicate/order | Candidate index | Why this key order / INCLUDE |
|---|---|---|---|
| Low stock by branch | `BranchCode = @branch AND Quantity <= @threshold` | `IX_BranchStock_Branch_Quantity (BranchCode, Quantity) INCLUDE (Sku)` | Equality key first, then range key; SKU covers the report projection. |
| Daily sales by branch/date | `BranchCode`, `Status`, `CompletedAt` range | `IX_CounterTransactions_Branch_Status_CompletedAt (BranchCode, Status, CompletedAt) INCLUDE (Total)` | Two equality predicates precede the date range; Total covers aggregate input. |
| Shipment search | `DestinationZone`, `ServiceLevel`, newest first | `IX_Shipments_Zone_Service_CreatedAt (DestinationZone, ServiceLevel, CreatedAt DESC) INCLUDE (BranchCode, WeightKg, Price)` | Equality keys first; descending timestamp supports recent-first retrieval. |
| Money-order auditor | branch equality, newest first | `IX_MoneyOrders_Branch_CreatedAt (BranchCode, CreatedAt DESC) INCLUDE (Amount, Status, CounterTransactionId)` | Supports branch-filtered chronological audit without lookups for the listed columns. |
| Successful verification lookup | branch equality, newest first | `IX_IdentityVerifications_Branch_VerifiedAt (BranchCode, VerifiedAt DESC) INCLUDE (Result, CounterTransactionId)` | Supports branch audit and verification outcome projection. |
| Stock adjustment history | branch equality, newest first | `IX_StockAdjustments_Branch_ChangedAt (BranchCode, ChangedAtUtc DESC) INCLUDE (Sku, Delta, ChangedBy)` | Supports branch manager history without lookups for the key audit fields. |
| Open transaction list | filtered `Status = 'Open'` | `IX_CounterTransactions_OpenBranch` filtered index | Excludes paid/voided/refunded history from the open-basket access path. |

All candidate indexes are deployed in migration `202611010002_AddWorkloadIndexes` and `sql/006_CandidateIndexes.sql`. Indexes consume storage and add write cost on every insert/update; retain them only when the measured workload improvement justifies that cost.

## Capture procedure

1. Create a dedicated `ApexPostDb` and run migrations/seed as described in `README.md`.
2. For a baseline, use a disposable copy of the database before migration `202611010002`, or drop the candidate indexes in that copy. Do not drop indexes on a shared/training database.
3. In SSMS enable **Include Actual Execution Plan** (`Ctrl+M`) and run `sql/008_PerformanceCapture.sql` with `SET STATISTICS IO/TIME ON`.
4. Save each actual plan as `.sqlplan` under `database/evidence/`. Capture the SSMS Messages output (logical reads and CPU/elapsed time) into the table below.
5. Apply candidate indexes, rerun the identical parameters and dataset, and save the after plans/output.
6. Use the index storage query at the end of `sql/008_PerformanceCapture.sql` and record index page usage. Use `sys.dm_db_index_usage_stats` to compare seeks/scans/updates after a representative write workload.
7. Investigate estimated-versus-actual row differences in the plan. Check statistics histogram and parameter distribution; `UPDATE STATISTICS ... WITH FULLSCAN` is included as a diagnostic, not an automatic production fix.

## Results — fill with observed SQL Server evidence

| Workload | Baseline logical reads | After logical reads | Baseline CPU ms | After CPU ms | Baseline plan | After plan | Index used MB / write cost |
|---|---:|---:|---:|---:|---|---|---|
| Low stock | Not measured yet | Not measured yet | Not measured yet | Not measured yet | Capture required | Capture required | Capture required |
| Daily sales | Not measured yet | Not measured yet | Not measured yet | Not measured yet | Capture required | Capture required | Capture required |
| Shipment search | Not measured yet | Not measured yet | Not measured yet | Not measured yet | Capture required | Capture required | Capture required |
| Money-order audit | Not measured yet | Not measured yet | Not measured yet | Not measured yet | Capture required | Capture required | Capture required |

## SARGability

The non-SARGable example is `YEAR(CompletedAt) = YEAR(@Date)`. The rewritten predicate uses a half-open UTC range: `CompletedAt >= @YearStart AND CompletedAt < @NextYear`. The exact plan and read difference must be captured from the target SQL Server; no improvement number is asserted here.

## Generated SQL from EF Core

`EfQueryDiagnostics.Capture` calls `ToQueryString()` for three important LINQ queries:

- Low-stock branch/quantity projection.
- Paid daily sales grouped by branch/date range.
- Shipment zone/service-level search ordered by creation date.

In Development, `Program.cs` writes the returned SQL to the `ApexPost.EFCoreQuerySamples` logger category. Save the actual emitted SQL from the run into `database/evidence/ToQueryString-Samples.md` after verifying the target provider and parameter values. EF command logging is enabled in Development; the report and dashboard query paths use `AsNoTracking` projections or parameterized stored-procedure results.

## Blocking, isolation and deadlock policy

`sql/005_BlockingDemo.sql` provides two SSMS sessions. Session A updates a stock row and holds the transaction for 15 seconds; Session B reads the same row under `READ COMMITTED`. With lock-based READ COMMITTED, the reader can wait for the writer's exclusive lock. If `READ_COMMITTED_SNAPSHOT` is enabled at database level, the reader may instead read a row version; record the actual database setting and observation.

Application write order is: `CounterTransactions` → `BranchStock` (sorted by SKU) → `Payments`/related rows, with short database transactions and no remote call inside the transaction. Stock updates use rowversion optimistic concurrency and return HTTP 409 on a stale write. Do not acquire stock rows in arbitrary order in multi-SKU sales; sort SKU keys before updating to reduce deadlock cycles.

## Query Store and parameter sensitivity

`sql/008_PerformanceCapture.sql` enables Query Store and includes a histogram inspection command. `usp_LowStockReport` uses `OPTION (RECOMPILE)` because its optional branch parameter creates materially different selectivity for one branch versus all branches. This trades compilation CPU for avoiding a poor reusable plan; compare with dynamic SQL or separate query branches only if the measured execution frequency justifies the added complexity.

## EF Core `ToQueryString()` output capture

In Development, `Program.cs` invokes `EfQueryDiagnostics.Capture` after migrations/seeding and writes the generated SQL for `LowStockByBranch`, `DailySalesByBranchAndDate` and `ShipmentSearchByZoneAndService` to the `ApexPost.EFCoreQuerySamples` logger category. Copy the three actual log outputs into this section (or save them as `.sql` files under `database/evidence/`) when running against SQL Server.

**Still to capture:** the generated SQL text, actual execution plans, logical reads, CPU time, storage cost and estimated-vs-actual row evidence. These are captured by running `sql/008_PerformanceCapture.sql` before and after `sql/006_CandidateIndexes.sql` on the seeded database; numbers are only recorded here once they have been measured.
