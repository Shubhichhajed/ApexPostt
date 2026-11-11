# ApexPost API and Courier Client – Security

## 1. Authentication design (JWT bearer)

`ApexPost.Api` uses `AddAuthentication().AddJwtBearer()` and validates every part of the token:

| Check | Setting | Source |
|---|---|---|
| Issuer | `ValidateIssuer = true` | `Auth:Issuer` (appsettings.json) |
| Audience | `ValidateAudience = true` | `Auth:Audience` (appsettings.json) |
| Lifetime | `ValidateLifetime`, `RequireExpirationTime`, 30 s clock skew | token `exp` / `nbf` |
| Signing key | `ValidateIssuerSigningKey`, HMAC-SHA256, at least 32 bytes | `Auth:SigningKey` (**user-secrets only**) |

The API refuses to start if the key is missing or shorter than 32 bytes. Tokens carry a `courierId` claim (couriers) and a role claim (`Courier` or `Admin`).

**Development tokens.** `POST /dev/token` is mapped only when `ASPNETCORE_ENVIRONMENT=Development` and is excluded from the OpenAPI document:

```http
POST https://localhost:7001/dev/token
Content-Type: application/json

{ "role": "Courier", "courierId": "courier-demo-01" }
```

`{ "role": "Admin" }` gives an Admin token. Tokens last 60 minutes (`lifetimeMinutes` can shorten them, which is handy for showing the expired-token 401).

## 2. Authorization

| Endpoint | Rule | Wrong/missing token | Wrong role |
|---|---|---|---|
| `POST /api/v{n}/shipments/{tn}/scans`, `PATCH .../status` | `[Authorize(Roles = "Courier")]` | 401 | 403 |
| `DELETE /api/v{n}/shipments/{tn}` | `[Authorize(Roles = "Admin")]` | 401 | 403 |
| `POST` / `PUT` shipment | `[Authorize(Roles = "Admin")]` | 401 | 403 |
| `GET` shipment(s), scan history | `[Authorize]` (any authenticated user) | 401 | – |
| `GET /api/v2/partner/shipments/{tn}` | `X-Api-Key` (no JWT) | 401 | – |

- 401 and 403 are both returned as ProblemDetails (`OnChallenge` / `OnForbidden` events). An expired token also gets `WWW-Authenticate: Bearer error="invalid_token"`.
- **Ownership rule:** every scan in an upload must have `courierId` equal to the token's `courierId` claim, otherwise 403. A courier cannot upload on behalf of someone else even with a valid Courier token.

## 3. Partner API keys

- `GET /api/v2/partner/shipments/{trackingNumber}` is read-only and carries the `[RequirePartnerApiKey]` endpoint metadata.
- `PartnerApiKeyMiddleware` runs after routing, reads that metadata, and checks the `X-Api-Key` header. Only marked endpoints are affected.
- The expected key comes from `Partner:ApiKey` in user-secrets / environment variables; `appsettings.json` contains an empty placeholder. It is never hard-coded or committed.
- Keys are compared with `CryptographicOperations.FixedTimeEquals` over SHA-256 hashes (constant time, no length leak).
- Missing or wrong key → **401** ProblemDetails. No key configured on the server → **500** (fail closed: never let requests through by accident).
- The supplied key is never logged.

## 4. OAuth2 concepts (what a production system would use)

**Authorization Code + PKCE – courier app.** The app opens the identity provider's login page in the system browser. After the courier signs in, the provider redirects back with a one-time *authorization code*. The app exchanges the code for tokens and proves it started the flow by sending the *code verifier* whose hash (the *code challenge*) it sent at the start. PKCE matters because a mobile/desktop app is a *public client*: it cannot keep a client secret, and an intercepted code is useless without the verifier.

**Client Credentials – partner systems.** A partner's server authenticates as itself with its client ID and secret (or a certificate) and receives an access token for machine-to-machine calls. There is no user involved, and the token carries the partner's identity and scopes (for example `shipments.read`).

**Roles of the pieces.**
- *Identity provider (IdP)* – authenticates users/clients and issues tokens (Entra ID, Auth0, Keycloak, Duende IdentityServer…).
- *Access token (JWT)* – short-lived (minutes), sent as `Authorization: Bearer`, validated by the API using the IdP's signing keys; contains `iss`, `aud`, `exp`, subject, roles/scopes.
- *Refresh token* – longer-lived, sent only to the IdP to get a new access token without asking the user to log in again. It must be stored securely and is rotated/revocable.

**Why API keys are weaker than OAuth2 tokens.** An API key is a long-lived shared secret: it does not expire by itself, it identifies a partner but not a user, it has no scopes or audience, rotating it means coordinating with the partner, and if it leaks it works until someone notices. OAuth2 access tokens are short-lived, scoped, audience-bound, signed and issued by a central service that can revoke clients. That is why the partner route is read-only.

**Moving to a real identity provider.**
1. Register the API (audience) and two clients in the IdP: the courier app (public client, Authorization Code + PKCE, redirect URI) and each partner (confidential client, Client Credentials).
2. In the API replace the symmetric key with `options.Authority = "<idp url>"` and `options.Audience = "apexpost-api"`, so signing keys are downloaded from the IdP's metadata (and rotate automatically). Keep issuer/audience/lifetime validation.
3. Map IdP roles/scopes to the `Courier`/`Admin` policies and issue `courierId` as a custom claim.
4. Delete the `/dev/token` endpoint and the `Auth:SigningKey` secret.
5. In the courier app use a library such as `Duende.IdentityModel.OidcClient` (MAUI: `WebAuthenticator`) for the login flow and keep the refresh token in secure storage.
6. Move partners from API keys to Client Credentials.

## 5. Transport security (TLS, HTTPS redirection, HSTS)

- `UseHttpsRedirection()` runs in every environment; `UseHsts()` runs outside Development (HSTS in Development would pin `localhost` to HTTPS in the browser for a year).
- **Why never plain HTTP:** a bearer token or API key is a password-equivalent – whoever has it can call the API as you. Over HTTP anybody on the same Wi-Fi, a proxy or an ISP can read it and replay it. TLS encrypts the request and proves the server's identity, so the credential can't be read or sent to an impostor. HSTS tells browsers to never even try HTTP for the site again, which closes the first-request downgrade gap.
- The courier client refuses a non-HTTPS base URL (`ApexApiOptions.ResolveBaseUrl`).
- **Bearer scheme in OpenAPI:** the document transformer adds a `Bearer` (HTTP, JWT) security scheme and, in v2, a `PartnerApiKey` scheme. In Swagger UI press **Authorize**, paste a token from `/dev/token`, and "Try it out" sends it.

### Development certificate

Trust the ASP.NET Core development certificate (`dotnet dev-certs https --trust`) – that is the normal path and needs no code. For the Android emulator (which does not trust the host's dev cert) there is an opt-in escape hatch in `src/ApexPost.Courier/Program.cs`:

- compiled only inside `#if DEBUG` – a Release build does not contain it at all;
- off by default (`ApexApi:AllowUntrustedDevelopmentCertificate = false`);
- even when on, it accepts an invalid certificate only for `localhost`/loopback and the emulator host `10.0.2.2`; every other host is still fully validated.

Certificate validation is never disabled in Release builds.

## 6. Courier client token handling (D10)

- The access token is stored only in the protected token store (`ProtectedTokenStore`), the console equivalent of MAUI `SecureStorage`. It is encrypted with ASP.NET Core Data Protection; on Windows the key ring itself is protected with DPAPI for the current user. It is never written to settings/preferences, plain files, the console or logs.
- `AuthHeaderHandler` (a `DelegatingHandler`) attaches `Authorization: Bearer …` to each request. On a **401** it clears the stored token and raises `SessionExpired`; the app returns to the Login screen.
- Logging: the app logs status codes, paths and scan IDs only – never tokens, API keys or request bodies. `System.Net.Http.HttpClient` logging is set to `Warning`, so request headers are not logged.

## 7. Errors and logging on the API

- `GlobalExceptionHandler` (`IExceptionHandler`) maps known failures to ProblemDetails (404 not found, 409 conflict/concurrency, 400 bad input) and everything else to 500. The exception message and stack trace are shown only in Development; every response carries a `traceId`.
- `[ApiController]` returns `ValidationProblemDetails` (400) for invalid payloads; `UseStatusCodePages` gives empty 404/405/415 responses a ProblemDetails body.
- Personal data (recipient phone/address) and request bodies are not logged.

## 8. Threats and mitigations (summary)

| Threat | Mitigation |
|---|---|
| Stolen/forged token | Signature, issuer, audience and lifetime validation; short lifetime; HTTPS only |
| Courier uploads for another courier | courierId claim must match every scan (403) |
| Clerk/courier deletes shipments | Admin-only DELETE/POST/PUT (403) |
| Partner key brute force / timing attack | Long random key, constant-time comparison, read-only route, 401 without detail |
| Misconfiguration lets traffic through | API refuses to start without a signing key; partner route returns 500 if no key |
| Credentials sniffed on the network | HTTPS redirection, HSTS, client rejects `http://` base URLs |
| Token leaked from the device | Encrypted at rest, never logged, cleared on 401/logout |
| Internal details leaked in errors | ProblemDetails with generic 500 outside Development |
| Duplicate scans on retry | Scan ID is the primary key; server counts duplicates instead of inserting |
