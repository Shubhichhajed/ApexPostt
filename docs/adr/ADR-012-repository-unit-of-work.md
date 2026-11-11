# ADR-012: Specific repositories and Unit of Work boundaries

- **Status:** Accepted
- **Context:** Application use cases need persistence contracts without exposing EF Core, SQL, or `IQueryable`.
- **Decision:** Application owns specific repository contracts (`IBranchRepository`, `IProductRepository`, `IStockRepository`, `ICounterTransactionRepository`, `IStaffRepository`, `IShipmentRepository`, `IMoneyOrderRepository`, `IPaymentRepository`) and `IUnitOfWork`. Infrastructure implements them with one scoped `ApexPostDbContext`. Repository methods stage adds/updates/deletes only; application services call one `SaveChangesAsync` at a use-case commit boundary. The stock repository exposes a branch-scoped tracked query for rowversion-protected mutations.
- **Direct DbContext exception:** Infrastructure query services may use direct `AsNoTracking` projections or stored-procedure APIs because these are read models, not aggregate writes. No DbContext type crosses into Application or Web controllers.
- **Alternatives:** Generic repository returning IQueryable; one DbContext per repository; custom UoW wrapping every EF method. Rejected because these leak provider details, break shared transaction scope or add no value.
- **Consequences:** Scoped repositories/UoW/context share one request context. A single SaveChanges persists all staged writes atomically. Explicit multi-step operations use execution strategy plus transaction.
