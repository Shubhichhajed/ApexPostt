# Week 03 data-access flow

```mermaid
flowchart TD
  MVC[ASP.NET Core MVC] --> APP[Application services and DTOs]
  APP --> CONTRACTS[Repository / Unit of Work / query contracts]
  CONTRACTS --> EF[Infrastructure EF Core 10]
  EF --> DB[(SQL Server ApexPostDb)]
  EF --> OUTBOX[Outbox change listener]
  OUTBOX --> DISPATCH[Simulated async dispatcher]
  TERMINAL[Offline counter terminal] --> SYNC[Authenticated offline-sync endpoint]
  SYNC --> INBOX[Idempotent SyncInbox]
  INBOX --> RECON[Conflict reconciliation policy]
```

The application layer owns contracts and use cases. Infrastructure owns `ApexPostDbContext`, Fluent mappings, SQL Server repositories, stored-procedure calls, migrations, the audit store, the change-tracking interceptor and the offline inbox/outbox persistence. The web project composes these services.

The offline sync endpoint durably accepts and deduplicates operations; it does not blindly overwrite financial aggregates. Stock/payment/transaction conflicts use server-wins plus terminal refresh. Other aggregate conflicts are marked for manual review. A production terminal still needs its local durable store and a real transport adapter; this Week 03 slice supplies the server-side inbox/outbox contract and simulated dispatcher, not a complete MAUI offline client.
```
