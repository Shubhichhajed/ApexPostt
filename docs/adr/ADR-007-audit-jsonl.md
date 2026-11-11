# ADR-007: MVC Action Audit Boundary (Week 2 JSONL Adapter)

- **Status:** Superseded in Week 3 by ADR-015; the MVC filter contract remains accepted.
- **Context:** Important MVC actions need actor, role, branch, action, result, duration, time and correlation ID. Passwords, API keys, document numbers and payment data must never enter audit logs.
- **Decision:** Use a global `IAsyncActionFilter` because it has MVC controller/action, route values, action arguments and result context. In Week 2, `IAuditStore` appended sanitized records as JSON Lines. The filter allowlisted route metadata and never serialized request bodies.
- **Alternatives considered:** Audit only in controllers; log entire HTTP request bodies; use middleware for all action-specific fields. Rejected because controller coverage is easy to miss, bodies contain secrets, and middleware cannot see MVC action results/arguments.
- **Consequences:** The filter contract survives Week 3 unchanged, while `EfAuditStore` now writes to SQL `AuditEntries` using an independent DbContext so audit failure cannot roll back a valid sale. The JSONL adapter is no longer active; audit retention, restricted access and operational monitoring remain deployment responsibilities.
