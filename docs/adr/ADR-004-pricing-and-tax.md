# ADR-004: Shipping Strategy and Tax Policy

- **Status:** Accepted
- **Context:** Shipping rates vary by destination zone, shipment type, service level and maximum weight. Retail products have different tax categories. Pricing must be server-authoritative and use decimal arithmetic.
- **Decision:** Use `IShippingPricingStrategy` with a zone/rate implementation. Keep tax mapping as an explicit pattern-matching policy and centralize two-decimal monetary rounding. `MoneyOrderPolicy` and `LowStockRule` encapsulate other configurable business thresholds.
- **Alternatives considered:** Hard-code prices in controllers/forms; store price supplied by the browser; create a generalized rules engine or a large pricing inheritance hierarchy. Rejected because client prices are untrusted and a generic rules engine is unjustified for the current small set of rules.
- **Consequences:** Pricing and tax rules are testable without MVC and can be replaced behind interfaces. EF persists monetary values as `decimal(18,2)`. Rate-cache benefit and index impact must be measured rather than assumed; currency conversion is outside this week's scope.
