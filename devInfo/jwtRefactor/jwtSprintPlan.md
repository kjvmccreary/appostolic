# JWT Auth Refactor Sprint Plan

## Vision / Goal

Transition the platform from development-only header-based authentication (x-dev-user/x-tenant) to a production‑grade JWT-based authentication + authorization system with tenant‑scoped access tokens, refresh token rotation, and removal of the Dev header dependency for all non-development environments. Provide a clean migration path, robust test coverage, and updated documentation (SnapshotArchitecture, LivingChecklist, upgrade guide). Maintain existing role flags (bitmask) semantics inside tokens.

## Non-Goals (Explicit Exclusions)

- Building an external IdP integration (e.g., OAuth/OIDC with a third party) this sprint.
- SSO / SAML / SCIM provisioning.
- Multi-region token replication (single-region assumption for now).
- Fine-grained per-endpoint dynamic scopes beyond current role flag policies.

## High-Level Outcomes

1. Issue and validate signed JWT access tokens (short-lived) & refresh tokens (long-lived) with revocation.
2. Embed canonical roles bitmask + array of role names + tenant context in tenant-scoped tokens.
3. Provide user-scoped (no tenant) login token that lists memberships and allows selecting a tenant to get a tenant token.
4. Replace DevHeaderAuthHandler usage in production; retain dev-only shim optional.
5. Frontend migrates fetch/session to Authorization Bearer flow; removes reliance on x-dev-user/x-tenant.
6. Swagger supports Authorize with Bearer token (copy/paste from login/select endpoints).
7. Full test matrix: token issuance, refresh rotation, revocation, tenant switch, role enforcement, negative security tests.
8. Observability: structured logs for auth events, minimal metrics counters, optional trace attributes.
9. Backward compatibility period (short, behind feature flag) allowing Dev headers only in Development.
10. Documentation & rollback plan (ability to re-enable Dev headers quickly if needed).
11. Secure httpOnly cookie strategy for refresh (and optional access) tokens with environment-aware Secure/SameSite semantics validated via local HTTPS.
12. Optional nginx reverse proxy (local + production example) for TLS termination, compression, standardized security headers.

## Architectural Decisions

- Signing Algorithm: HS256 initially (single symmetric key), path to RS256 later. Key stored in secure configuration (ENV var `AUTH__JWT__SIGNING_KEY`).
- Token Shapes:
  - Access Token (tenant-scoped): `{ sub, email, tenant_id, tenant_slug, roles_value, roles[], iat, exp, iss, aud, v }` where `v` = user token version (for revocation on password change / forced logout).
  - User (neutral) Token (after login/magic): `{ sub, email, memberships:[{tenant_id, tenant_slug, roles_value}], iat, exp }` (no roles array per membership unless needed; client selects tenant).
  - Refresh Token: opaque random 256-bit value, stored hashed (SHA-256 + pepper) in `app.refresh_tokens` table with status + expiry + fingerprint.
- Rotation Strategy: One refresh token per device/session (rotate on each refresh, previous becomes inactive). Access tokens non-revocable except via version bump or refresh blacklist.
- Cookie Strategy: Refresh token stored in httpOnly `rt` cookie (Secure+SameSite=Lax in prod; Secure may be false in pure HTTP dev; SameSite=None if future cross-site embedding required). Access token returned in JSON and held only in memory (preferred) OR optionally also via short‑lived httpOnly cookie (`at`) for SSR endpoints (decision deferred to Story 4). CSRF mitigation approach (double-submit vs header secret) finalized in Story 4.
- Revocation Mechanism: Increment `users.token_version` on password change / explicit admin revoke; middleware denies access if token `v` < current version.
- Clock Skew: Accept ±60 seconds.
- Expiries: Access 15m, Refresh 30d (configurable).
- Tenant Switch: POST /api/auth/select-tenant => issues new tenant-scoped access + refresh pair (old tenant access token naturally expires; refresh optionally reused or rotated—choose rotation for clarity).
- Dev Mode: Preserve DevHeaderAuthHandler only when `ASPNETCORE_ENVIRONMENT=Development` and feature flag `AUTH_DEV_HEADERS_ENABLED=true`.

## Data Model Changes

- New table `app.refresh_tokens`:
  - id (uuid PK)
  - user_id (uuid FK users)
  - token_hash (varchar(64))
  - created_at timestamptz
  - expires_at timestamptz
  - revoked_at timestamptz null
  - replaced_by_token_id uuid null (chain linkage)
  - fingerprint varchar(200) null (optional device/browser hash)
  - tenant_id uuid null (nullable: neutral vs tenant-scoped refresh)
  - unique(token_hash)
- Alter `app.users` add column `token_version int NOT NULL DEFAULT 0`.

## Environment / Config Additions

- AUTH**JWT**ISSUER
- AUTH**JWT**AUDIENCE
- AUTH**JWT**SIGNING_KEY (32+ random bytes base64)
- AUTH**JWT**ACCESS_TTL_MINUTES (default 15)
- AUTH**JWT**REFRESH_TTL_DAYS (default 30)
- AUTH\_\_ALLOW_DEV_HEADERS (bool; true only in Development)

## Sprint Stories (Detailed)

### Story 1: Baseline JWT Infrastructure (Signing & Validation) — ✅ DONE

Acceptance:

- Add JwtBearer authentication alongside existing Dev scheme.
- Config binding for issuer, audience, signing key.
- Health check endpoint still public.
- Add integration smoke test generating a dummy token (using internal helper) and hitting a protected endpoint returns 200.
  Deliverables:
- New `JwtTokenService` (create access tokens, neutral tokens, validation helpers for tests).
- Program.cs wiring: `.AddAuthentication("JwtBearer")...` plus policy chain unaffected.
- Update Swagger: Bearer security scheme added.

### Story 2: User Neutral Login & Magic Consume Issue Neutral Token — ✅ DONE (2025-09-20)

Acceptance:

- /api/auth/login (password) and /api/auth/magic/consume now return a neutral JWT (no tenant_id) and refresh token (neutral) plus memberships array.
- Legacy JSON response preserved behind `includeLegacy=true` query param for 1 sprint (optional). Default returns tokens.
- If user has exactly one membership, response ALSO includes an auto-issued tenant-scoped access token object (and optionally its refresh token if we keep rotation consistent) to skip an immediate select-tenant round-trip (`tenantToken` property). Multi-tenant users do NOT receive a tenant token unless explicitly requested.
- Optional query parameter `tenant=` (slug or id) may be supplied; if provided and user is a member, the response includes `tenantToken` even for multi-tenant user. If ambiguous (`tenant=auto` with >1 memberships) return 409 with problem details.
- Tests: login success (neutral only), single-membership auto-tenant token presence, multi-membership + tenant param yields tenant token, invalid password 401, magic consume issues neutral token.
  Deliverables:
- Modify endpoints in `V1.cs`.
- Add password login if not present or adjust existing.
- Refresh token creation & persistence.

Implementation Notes (Completed):

- Added `RefreshToken` entity + `refresh_tokens` migration (`s6_01_auth_refresh_tokens`) storing SHA256 hash of opaque refresh token (no pepper yet; future rotation story will consider).
- Implemented `RefreshTokenService.IssueNeutralAsync` and extended `JwtTokenService` with `IssueTenantToken`.
- Updated `/api/auth/login` and `/api/auth/magic/consume` to return structured JSON `{ user, memberships, access, refresh, tenantToken? }` and legacy fallback via `?includeLegacy=true`.
- Auto-tenant token issuance when exactly one membership; conflict (409) when `tenant=auto` with >1 memberships; explicit `tenant=<slug|id>` selection works.
- Magic consume path provisions personal tenant + membership for new users before token issuance.
- Tests added: `LoginJwtNeutralTests`, `LoginTenantSelectionTests`, `MagicConsumeJwtTests` (structured + legacy shape) — all green.
- Documentation: `SnapshotArchitecture.md`, `LivingChecklist.md`, `storyLog.md` updated with Story 2 summary.
- Next stories will introduce rotation/reuse detection and test helper (Story 2a) — no cookie/httpOnly changes yet (planned Story 4/5a).

### Story 3: Tenant Selection → Tenant-Scoped Token Pair — ✅ DONE (2025-09-20)

Acceptance (fulfilled):

- POST /api/auth/select-tenant accepts body `{ tenant?: string, refreshToken: string }` where `tenant` may be a tenant slug or GUID (or omitted if only one membership—client can still supply explicitly for clarity).
- Validates provided neutral refresh token (unexpired, not revoked, purpose neutral) and the caller's membership in the target tenant.
- On success: returns `{ user, memberships, access, refresh, tenantToken }` where:
  - `access` = new neutral access token (user scope, refreshed claims & token_version)
  - `refresh` = new neutral refresh token (old one revoked & linked via `replaced_by_token_id` / RevokeAsync)
  - `tenantToken.access` = tenant-scoped access token embedding `tenant_id`, `tenant_slug`, `roles_value`, and roles[]
- Old refresh token is revoked before issuing new to enforce single active chain and prevent reuse.
- Reuse of revoked/expired refresh returns 401; selecting a tenant without membership returns 403.

Implementation Notes:

- Added endpoint mapping in `V1.cs` with internal record `SelectTenantDto { string? Tenant; string RefreshToken; }`.
- Fixed hashing mismatch discovered during testing: endpoint initially used existing hex hash helper; storage uses Base64(SHA256). Introduced inline Base64(SHA256) computation aligning with `RefreshTokenService` so lookups succeed.
- Batch membership + tenant slugs fetch performed with a single join to avoid N+1 when projecting memberships for response.
- Rotation flow: `ValidateNeutralAsync` for structural/expiry check, then explicit `RevokeAsync(oldId)` inside request scope prior to issuing new neutral (`IssueNeutralAsync`). Revocation sets `revoked_at`; (optional future) replaced_by linkage can be surfaced.
- Tests added (apps/api.tests): success rotation (new refresh differs; old unusable), invalid refresh 401, forbidden tenant 403, expired refresh 401, revoked reuse 401.
- All new tests green; existing auth suites unaffected.

Follow-ups:

- Story 6 will introduce general refresh endpoint (neutral + tenant flows) reusing the same hashing & rotation pattern.
- Consider extracting shared Base64 SHA256 hashing helper to avoid accidental divergence in future endpoints (refresh/logout).
- Future Story: deliver refresh token via secure httpOnly cookie once HTTPS local (Story 5a) established.

Risk Mitigations:

- Hash format discrepancy documented here to prevent regressions.
- Tests explicitly assert revoked refresh reuse returns 401 to guard rotation invariant.

### Story 2a: Test Ergonomics & Helper Shortcuts (NEW) — ✅ DONE (2025-09-20)

Acceptance:

- Introduce internal (Test/Development only) token mint endpoint OR assembly-internal `ITestTokenFactory` service exposed to test project that can directly create a tenant-scoped access+refresh pair given (userId, tenantId) bypassing login + selection. Guarded by compile symbol or `AUTH__TEST_HELPERS_ENABLED` env only in non-production.
- Provide a test helper class (`TestAuthClient`) in `apps/api.tests` wrapping: `GetTenantAccessTokenAsync(email, tenantSlug)` → auto user lookup/creation (if seeding supports) + direct token mint.
- Ensure helper NOT registered when `ASPNETCORE_ENVIRONMENT=Production` (integration test asserting 404 on helper endpoint or DI resolution failure).
- Update Story 2 tests to use helper for non-auth-flow scenarios (reduce boilerplate) while preserving dedicated tests for login/select flows.
- Documentation (plan + future upgrade doc section) includes “Writing Authenticated Tests” guidance referencing helper and discouraging raw multi-step flow except when explicitly testing it.

Deliverables:

- Internal service/endpoint source file with `#if DEBUG || TESTING` (or environment gate) guards + comments.
- `TestAuthClient` utility in test project.
- New tests: (1) helper issues valid tenant token; (2) helper unavailable in production environment simulation.

Notes:

- Does not replace select-tenant endpoint; ensures production semantics untouched.
- Minimizes friction that motivated earlier test complexity.

Acceptance:

- New endpoint POST /api/auth/select-tenant { tenantId | tenantSlug, refreshToken } returns new access + refresh pair scoped to selected tenant (roles claims present).
- Validates membership existence; returns 403 if missing.
- Neutral refresh token can be converted; previous refresh is revoked with replaced_by_token_id chain.
- Tests: success path, unauthorized tenant, revoked prior refresh cannot be reused.

### Story 4: Frontend Auth Client Refactor (Neutral Token Phase)

Acceptance:

- Frontend stores neutral token + refresh (httpOnly cookie) after login (cookie name `rt`, httpOnly, Secure in prod, SameSite=Lax). Neutral access token kept only in memory (not localStorage) for minimized XSS persistence.
- Tenant selection triggers call to /api/auth/select-tenant; sets tenant-scoped access token for subsequent API calls.
- Remove reliance on x-dev-user/x-tenant for runtime API fetch wrapper except when `AUTH__ALLOW_DEV_HEADERS && process.env.NODE_ENV === 'development'`.
- Role decoding uses claims roles_value directly; fallback removed.
- Tests updated (mocks for fetch adding Authorization header).

### Story 5: Access Token Validation Middleware & Principal Construction

Acceptance:

- JwtBearer handler maps roles_value claim to roles flags fulfilling policies (TenantAdmin etc.).
- Token version mismatch triggers 401 with problem+json payload.
- Missing tenant_id on an endpoint requiring tenant scope returns 400 or 403 (decide: 400 invalid context).
- Tests: role policy pass/fail, version mismatch.

### Story 5a: Local HTTPS Enablement & Secure Cookie Validation (NEW)

Acceptance:

- Kestrel configured for HTTPS locally (trust dev cert) enabling Secure cookie end-to-end validation.
- Quick start documented (`dotnet dev-certs https --trust`) and Makefile target (e.g., `make api-https`).
- Demonstrate: Secure cookie sent over HTTPS, omitted when attempting HTTP.
  Deliverables:
- appsettings.Development.json / Program.cs Kestrel config snippet.
- README / upgrade guide additions for local HTTPS.

### Story 6: Refresh Flow & Rotation

Acceptance:

- POST /api/auth/refresh { refreshToken } returns new access + refresh tokens; prior refresh revoked.
- Attempting to reuse old refresh returns 401.
- Expired refresh returns 401.
- Tests: rotation, reuse attempt, expiry simulation.

### Story 7: Logout & Global Revocation

Acceptance:

- POST /api/auth/logout { refreshToken } revokes chain (marks token revoked_at).
- POST /api/auth/logout/all revokes all active refresh tokens for user and increments token_version.
- Password change increments token_version automatically.
- Tests: logout invalidates subsequent refresh; logout all invalidates old access via version mismatch.

### Story 8: Dev Headers Decommission (Flag Gate)

Acceptance:

- If AUTH\_\_ALLOW_DEV_HEADERS=false, DevHeaderAuthHandler not registered; requests with x-dev-user fail (401) encouraging Bearer usage.
- Story adds doc for enabling headers locally.
- Tests ensuring handler absent when config false.

### Story 9: Observability & Security Hardening

Acceptance:

- Structured log for token issuance (no secrets) includes user_id, tenant_id (if scoped), token_id (refresh only), action (issue|refresh|revoke), reason.
- Metrics: counter tokens_issued, tokens_refreshed, tokens_revoked.
- Optional trace attributes for auth actions.
- Basic rate limiting (optional) on login, select-tenant, refresh (3–5 per second per IP). Document if deferred.

### Story 9a: Nginx Reverse Proxy & Security Headers (OPTIONAL)

Acceptance:

- Provide `infra/nginx/` sample config proxying API + web, enabling gzip/br compression, HSTS (prod), security headers (X-Frame-Options DENY, X-Content-Type-Options nosniff, Referrer-Policy strict-origin-when-cross-origin, Permissions-Policy minimal), request size limit, and disabled buffering for streaming endpoints.
- Local run instructions via docker-compose or documented command; Makefile target optional.
- Docs enumerating which headers rely on nginx vs Kestrel.
  Deliverables:
- nginx.conf (local & prod sample) + README snippet.
  Notes:
- Optional if platform ingress already supplies equivalent features (document detection checklist).

### Story 10: Documentation & Upgrade Guide

Acceptance:

- Upgrade doc: enabling JWT, generating signing key, rolling out alongside dev headers, rollback steps.
- SnapshotArchitecture updated with new component diagram (TokenService, RefreshToken table, flows).
- LivingChecklist updated (auth section). StoryLog entry appended.
- Include secure cookie rollout instructions, local HTTPS enablement, CSRF mitigation rationale, and optional nginx integration notes.

### Story 11: Cleanup & Legacy Removal

Acceptance:

- Remove any leftover code paths referencing neutral legacy JSON-only responses.
- Remove unused environment flags or compatibility fallbacks.
- Final test suite run: API & Web green.
- Tag `jwt-auth-rollout-complete` annotated with summary.
- Confirm optional nginx story either merged or explicitly deferred with rationale in StoryLog.

## Risk & Mitigation

| Risk                                | Impact                | Mitigation                                                                         |
| ----------------------------------- | --------------------- | ---------------------------------------------------------------------------------- |
| Key leakage                         | Token forgery         | Use long random signing key; document rotation procedure.                          |
| Forgotten revocation path           | Stale access remains  | Enforce short access TTL + version bump strategy.                                  |
| Clock skew issues                   | False expiry failures | Allow ±60s skew in validation parameters.                                          |
| Frontend token storage XSS          | Token theft           | Prefer httpOnly secure cookies (document local dev adjustments).                   |
| Race conditions on refresh rotation | Reuse accepted        | Enforce single active chain by revoking old before issuing new inside transaction. |

## Test Matrix (Representative)

- Auth success/failure (login, magic consume)
- Neutral token decode
- Tenant selection success / membership missing / unauthorized
- Policy enforcement (TenantAdmin only endpoint) with each role set
- Refresh rotation / reuse invalidation / expiry
- Logout single / logout all
- Password change → version bump invalidation
- Dev headers allowed vs disallowed
- Performance: measure added latency (aim < 2ms token validation overhead)

## Rollback Strategy

1. Keep Dev headers enabled until JWT endpoints verified in staging.
2. If critical failure: set AUTH\_\_ALLOW_DEV_HEADERS=true and frontend env to re-enable header path; existing code continues working.
3. Refresh tokens can be purged with `DELETE FROM app.refresh_tokens` (forces re-login) if compromised.
4. Key rotation: set new signing key, accept both old/new for 15m grace (future enhancement: dual validation list) — documented but not implemented multi-key support this sprint.

## Implementation Order Rationale

Backend infra (Stories 1–3) must precede frontend refactor (Story 4). Validation (Story 5) & refresh (Story 6) must precede logout/revocation (Story 7). Dev header removal waits until after stable end-to-end flows (Story 8). Observability (9) secures before documentation & final cleanup (10–11).

## Story Pointing (Relative)

(Indicative: XS=1, S=2, M=3, L=5, XL=8)
1: M, 2: M, 3: M, 4: L, 5: S, 5a: XS, 6: M, 7: S, 8: S, 9: S, 9a: S (optional), 10: S, 11: XS
Total (w/out 9a) ~32–34 points; with 9a ~34–36 (still feasible with capacity).

## Open Questions (To Resolve Early)

- Cookie vs Authorization header: choose cookie (httpOnly, secure) for production; keep header variant for Swagger.
- Neutral token needed long-term? Potentially yes for multi-tenant UX; else collapse by always issuing tenant token with ability to switch via reissue.
- Multi-tenant session concurrency: Are parallel tenant contexts required? (If yes, keep neutral + multiple tenant tokens; out of scope now.)
- CSRF mitigation technique: double-submit cookie vs custom header secret? (Decide Story 4.)
- If nginx deferred, enumerate ingress feature parity checklist (compression, HSTS, security headers) to ensure no silent gap.

## Next Action

Start Story 3 (tenant selection endpoint issuing tenant-scoped access + refresh pair with membership validation and refresh rotation prep), then plan Story 6 (refresh rotation implementation) before moving into Stories 4 & 5a for frontend refactor + secure cookie handling.
