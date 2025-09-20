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

## Architectural Decisions

- Signing Algorithm: HS256 initially (single symmetric key), path to RS256 later. Key stored in secure configuration (ENV var `AUTH__JWT__SIGNING_KEY`).
- Token Shapes:
  - Access Token (tenant-scoped): `{ sub, email, tenant_id, tenant_slug, roles_value, roles[], iat, exp, iss, aud, v }` where `v` = user token version (for revocation on password change / forced logout).
  - User (neutral) Token (after login/magic): `{ sub, email, memberships:[{tenant_id, tenant_slug, roles_value}], iat, exp }` (no roles array per membership unless needed; client selects tenant).
  - Refresh Token: opaque random 256-bit value, stored hashed (SHA-256 + pepper) in `app.refresh_tokens` table with status + expiry + fingerprint.
- Rotation Strategy: One refresh token per device/session (rotate on each refresh, previous becomes inactive). Access tokens non-revocable except via version bump or refresh blacklist.
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

### Story 1: Baseline JWT Infrastructure (Signing & Validation)

Acceptance:

- Add JwtBearer authentication alongside existing Dev scheme.
- Config binding for issuer, audience, signing key.
- Health check endpoint still public.
- Add integration smoke test generating a dummy token (using internal helper) and hitting a protected endpoint returns 200.
  Deliverables:
- New `JwtTokenService` (create access tokens, neutral tokens, validation helpers for tests).
- Program.cs wiring: `.AddAuthentication("JwtBearer")...` plus policy chain unaffected.
- Update Swagger: Bearer security scheme added.

### Story 2: User Neutral Login & Magic Consume Issue Neutral Token

Acceptance:

- /api/auth/login (password) and /api/auth/magic/consume now return a neutral JWT (no tenant_id) and refresh token (neutral) plus memberships array.
- Legacy JSON response preserved behind `includeLegacy=true` query param for 1 sprint (optional). Default returns tokens.
- Tests: login success returns valid JWT (decode + claims), invalid password 401, magic token consume issues tokens.
  Deliverables:
- Modify endpoints in `V1.cs`.
- Add password login if not present or adjust existing.
- Refresh token creation & persistence.

### Story 3: Tenant Selection → Tenant-Scoped Token Pair

Acceptance:

- New endpoint POST /api/auth/select-tenant { tenantId | tenantSlug, refreshToken } returns new access + refresh pair scoped to selected tenant (roles claims present).
- Validates membership existence; returns 403 if missing.
- Neutral refresh token can be converted; previous refresh is revoked with replaced_by_token_id chain.
- Tests: success path, unauthorized tenant, revoked prior refresh cannot be reused.

### Story 4: Frontend Auth Client Refactor (Neutral Token Phase)

Acceptance:

- Frontend stores neutral token + refresh (httpOnly cookie or memory—choose cookie for XSS mitigation) after login.
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

### Story 10: Documentation & Upgrade Guide

Acceptance:

- Upgrade doc: enabling JWT, generating signing key, rolling out alongside dev headers, rollback steps.
- SnapshotArchitecture updated with new component diagram (TokenService, RefreshToken table, flows).
- LivingChecklist updated (auth section). StoryLog entry appended.

### Story 11: Cleanup & Legacy Removal

Acceptance:

- Remove any leftover code paths referencing neutral legacy JSON-only responses.
- Remove unused environment flags or compatibility fallbacks.
- Final test suite run: API & Web green.
- Tag `jwt-auth-rollout-complete` annotated with summary.

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
1: M, 2: M, 3: M, 4: L, 5: S, 6: M, 7: S, 8: S, 9: S, 10: S, 11: XS
Total ~30–32 points (1–1.5 focused sprint depending on capacity).

## Open Questions (To Resolve Early)

- Cookie vs Authorization header: choose cookie (httpOnly, secure) for production; keep header variant for Swagger.
- Neutral token needed long-term? Potentially yes for multi-tenant UX; else collapse by always issuing tenant token with ability to switch via reissue.
- Multi-tenant session concurrency: Are parallel tenant contexts required? (If yes, keep neutral + multiple tenant tokens; out of scope now.)

## Next Action

Begin Story 1: add JwtBearer auth, token service skeleton, configuration keys (behind feature flag AUTH**JWT**ENABLED). Frontend untouched until Story 4.
