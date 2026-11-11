# ADR-011: Stored procedures for operational reports and stock adjustment

- **Status:** Accepted
- **Context:** Daily branch sales, low-stock reporting and stock adjustment are stable, reusable SQL operations. Stock adjustment must atomically update inventory and append its history row.
- **Decision:** Use `usp_DailyBranchSales` and `usp_LowStockReport` for reporting, and `usp_AdjustStock` for transactional inventory adjustment. Add shipping-volume and money-order audit procedures. Each uses `CREATE OR ALTER`, `SET NOCOUNT ON`, parameter validation and meaningful `THROW`; stock adjustment uses `XACT_ABORT`, `TRY/CATCH`, `XACT_STATE()` and a short transaction. EF calls use interpolated/parameterized APIs only.
- **Why not LINQ:** The reports are stable SQL result shapes shared with auditor/reporting workflows. Stock adjustment is a database-owned atomic write plus history operation; it needs one lock/transaction boundary at the data store. The choice is not based on an unsupported claim that stored procedures are always faster.
- **Alternatives:** LINQ for every report; concatenated dynamic SQL. Rejected for these specific stable report/atomic-write use cases and safety.
- **Consequences:** Procedure contracts need versioned scripts and integration tests. Application login receives EXECUTE only on the approved schema procedures plus least-privilege table DML.
