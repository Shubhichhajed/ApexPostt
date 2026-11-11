# ADR-013: Domain and persistence model separation

- **Status:** Accepted
- **Context:** Domain identity verification should express the business concept, while SQL storage must enforce a bounded masked document representation.
- **Decision:** Keep rich Week 2 domain entities in `ApexPost.Domain`; map them with Fluent API. For identity verification, use a separate Infrastructure-only `IdentityVerificationRecord` and an explicit mapper in `EfIdentityVerificationRepository`. Only the masked value is copied to persistence. Rowversion and standard audit columns are shadow properties where the domain does not need persistence metadata.
- **Alternatives:** Put EF attributes on Domain; persist every domain object as a separate persistence DTO; duplicate all aggregates. Rejected because domain purity and maintainability matter more than a mechanical DTO for every type.
- **Consequences:** Identity mapping is explicit and testable. Other aggregates map directly where their domain shape fits the relational model.
