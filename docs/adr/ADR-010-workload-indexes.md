# ADR-010: Workload-driven indexes

- **Status:** Accepted, pending measured confirmation
- **Context:** Required workloads are low-stock by branch, daily sales by branch/date, shipment search by zone/service, money-order audit and successful identity-verification lookup.
- **Decision:** Add composite/covering indexes with equality keys before range/order keys and only the columns needed to cover the target projections. Add a filtered index for open transactions. Candidate definitions are in migration `202611010002` and `sql/006_CandidateIndexes.sql`.
- **Alternatives:** Index every column; use one broad covering index; rely on clustered PK scans. Rejected because they increase write/storage overhead or do not match query predicates.
- **Evidence caveat:** Index key order is based on query shape, but actual plan, logical reads, CPU and storage comparisons must be captured on the target SQL Server. No benchmark is claimed until `PERFORMANCE.md` contains real `.sqlplan` and STATISTICS IO/TIME output.
- **Consequences:** Read paths have targeted candidates; retention is conditional on observed selectivity and write cost.
