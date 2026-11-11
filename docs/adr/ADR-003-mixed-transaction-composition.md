# ADR-003: Mixed Basket Uses Composition

- **Status:** Accepted
- **Context:** One counter sale can combine retail products, shipping and money-order services. Those line types share transaction totals and lifecycle but have different supporting data and rules.
- **Decision:** `CounterTransaction` owns a collection of `TransactionLine` records tagged by `LineKind`. Shipment and money-order details remain separate domain concepts linked to the transaction. Totals, payment and Paid/Voided/Refunded transitions are controlled by the transaction aggregate and application use case.
- **Alternatives considered:** A deep inheritance tree with `RetailLine`, `ShippingLine` and `MoneyOrderLine`; one untyped JSON payload per line; separate transactions per service. Rejected because inheritance complicates shared totals, untyped payloads weaken validation, and separate transactions break the single-basket requirement.
- **Consequences:** New line kinds can be added with explicit pricing/tax rules. Transaction lines remain simple and the detailed service entities retain their own invariants. The SQL schema uses a child table with a constrained `Kind` value; a transaction and its lines/payment are committed atomically.
