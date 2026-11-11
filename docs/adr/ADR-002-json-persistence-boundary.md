# ADR-002: JSON Persistence Behind Repository Interfaces (Week 2)

- **Status:** Superseded in Week 3 by ADR-009 and ADR-012; retained as the historical Week 2 decision.
- **Context:** Week 2 explicitly prohibited a database and ORM while requiring future database replacement without changing domain behavior. The pilot dataset was small and single-process.
- **Decision:** For Week 2, define repository contracts in Application and implement asynchronous JSON-file adapters in Infrastructure. Use cancellation, per-path semaphores, temporary-file writes and atomic replacement. Corrupt data must fail visibly rather than silently resetting business records.
- **Alternatives considered:** Put file access in controllers/domain; serialize the entire platform to one file; use a database despite the Week 2 constraint. Rejected because they either violate dependency direction, increase lost-update risk, or break the lab constraints.
- **Consequences in Week 2:** The adapter was portable and easy to inspect, but file locks were process-local and multi-file business workflows were not atomic. Week 3 replaces this adapter with SQL Server/EF Core while retaining Application contracts and domain tests; JSON repositories are no longer registered in the active application.
