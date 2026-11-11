# ADR-005: Cookie Authentication Without ASP.NET Core Identity

- **Status:** Accepted for the continuous assessment
- **Context:** The Week 2 assignment prohibits ASP.NET Core Identity and requires a login page, staff claims, role authorization, branch policies and secure cookies. Week 3 must preserve that platform behavior while changing persistence.
- **Decision:** Use ASP.NET Core cookie authentication with `StaffId`, `Role` and `BranchCode` claims. Passwords use PBKDF2-SHA256 salted hashes. Cookies are HttpOnly, Secure and SameSite=Lax with bounded expiration; form POSTs use antiforgery. Demo passwords and partner keys are supplied through user-secrets/environment configuration.
- **Alternatives considered:** ASP.NET Core Identity; custom bearer JWT tokens; trusting a posted staff/branch/role value. Rejected because Identity is prohibited, JWT is unnecessary for this MVC workflow, and posted identity data is attacker-controlled.
- **Consequences:** The implementation fits the lab and keeps authorization claims explicit. Password reset, lockout persistence, MFA, production account provisioning and external identity-provider integration are not claimed; they require a separate security design before production use.
