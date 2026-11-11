# ADR-016: Use-case atomicity and projected read-model DTOs

- **Status:** Accepted
- **Context:** Week 2 persistence methods committed at intermediate steps in a few workflows, and MVC list screens loaded broad entity collections. Week 3 requires one commit per use case and no-tracking DTO projections for read screens.
- **Decision:** Repositories stage changes only. `MoneyOrderService` stages the transaction, line and order before one final commit; when it completes a new sale, `CounterService.CompleteAsync` performs the single atomic `SaveChangesAsync`. Screen reads use `IPlatformQueryService` projections with `AsNoTracking`, and report reads use query services or stored procedures. Request DTOs do not expose persistence IDs/status/audit fields as write authority.
- **Alternatives:** Commit after each repository call; load all entities and filter in controllers; return `IQueryable` to Web. Rejected because intermediate commits can leave partial workflows, broad reads waste I/O, and provider queryables leak Infrastructure details.
- **Consequences:** A single use-case commit preserves transaction integrity. DTO projections keep read models narrow and prevent accidental over-posting. New screens must add a query method/DTO rather than calling `GetAllAsync` and filtering in the controller.
