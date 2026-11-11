# Project Structure

```text
ApexPost/
├── ApexPost.sln
├── README.md, DESIGN.md, API-NOTES.md, VERSIONING.md, SECURITY.md, RESILIENCE.md, OFFLINE-DESIGN.md, TESTING.md, PERFORMANCE.md
├── ApexPost.Api.http              requests for every endpoint (v1, v2, security cases)
├── contracts/                     v1.json, v2.json (OpenAPI snapshots)
├── nswag.json, tools/             SDK generation and contract refresh
├── docs/adr/                      ADR-001 … ADR-019
├── sql/, database/                Week 3 SQL artefacts
├── src/
│   ├── ApexPost.Domain/                    entities, value objects, rules
│   ├── ApexPost.Application/               use cases, commands, interfaces
│   ├── ApexPost.Infrastructure/            EF Core 10, repositories, migrations, reports
│   ├── ApexPost.Web/                       MVC counter app (Weeks 2–3)
│   ├── ApexPost.Courier.Offline/           Part A: Couchbase Lite store, repository, outbox, listener
│   ├── ApexPost.Courier.Offline.Console/   Part A: console harness
│   ├── ApexPost.Api/                       Parts B–D: REST API
│   │   ├── Controllers/  (v1, v2, partner)
│   │   ├── Models/       (request/response DTOs, mapper)
│   │   ├── Services/     (IShipmentService, metadata store, OpenAPI transformers)
│   │   ├── Security/     (roles, ownership rule, partner key middleware)
│   │   ├── Middleware/   (global IExceptionHandler)
│   │   └── Data/         (API-owned DbContext + migration)
│   ├── ApexPost.Client/                    Part C6: NSwag-generated SDK (+ extensions)
│   ├── ApexPost.Client.Consumer/           Part C6: SDK console consumer
│   └── ApexPost.Courier/                   Part D-II: courier client
│       ├── Api/          (typed client, DTOs, JsonSerializerContext, errors)
│       ├── Auth/         (AuthHeaderHandler, token store, JWT payload reader)
│       ├── Resilience/   (pipeline, connection state)
│       ├── Sync/         (API uploader, connectivity monitor, sync coordinator)
│       └── Screens/      (Login, New Scan, Pending Scans, Sync Status)
└── tests/
    ├── ApexPost.UnitTests/
    ├── ApexPost.Courier.Tests/
    └── ApexPost.IntegrationTests/
```

## Dependency direction
- Domain has no project references.
- Application references Domain; Infrastructure implements Application contracts.
- Web and Api are composition roots; they reference Application, Domain and Infrastructure.
- Courier.Offline has no dependency on the API; Courier depends on Courier.Offline and talks to the API only over HTTP.
- Client is generated from the contract only; it has no reference to the API project.
