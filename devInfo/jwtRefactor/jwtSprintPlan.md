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

### Story 4: Frontend Auth Client Refactor (Neutral Token Phase) — ✅ DONE (2025-09-20)

Summary:

- Implemented frontend neutral access token handling (`authClient.ts`) storing access token only in memory and a `withAuthFetch` helper that attaches the Bearer token and includes credentials for refresh cookie propagation.
- Backend now issues httpOnly refresh cookie `rt` (feature flagged `AUTH__REFRESH_COOKIE_ENABLED`) on login, magic consume, and tenant selection with rotation semantics validated by tests.
- Added integration/unit tests: `RefreshCookieTests` (cookie issuance & rotation) and `withAuthFetch` behavior tests (authorization header set).
- Integrated neutral access priming (`primeNeutralAccess`) into NextAuth auth flows when access token present in API responses.
- Documentation (SnapshotArchitecture, LivingChecklist, storyLog) updated in prior commits to reflect refresh cookie & in-memory strategy.

Deferred to Story 6 / Later:

- Real `/api/auth/refresh` endpoint and client silent refresh loop.
- Removal of refresh token from JSON payload once refresh endpoint is active (grace period approach).
- Phase-out of dev headers usage in web codepaths (tracking under Dev Headers Decommission Story 8 after refresh path stabilized).
- Extended SSR access token cookie decision and CSRF double-submit vs SameSite strategy (documented in Optional Followups).

Notes:

- Placeholder internal refresh-neutral route remains intentionally inert; safe to keep until Story 6 introduces the proper refresh flow.

### Story 5: Access Token Validation Middleware & Principal Construction — ✅ DONE (2025-09-20)

Summary:

- Added composite Development-only auth scheme ("BearerOrDev") allowing seamless fallback to dev header auth without per-endpoint scheme decoration; eliminated widespread 401s.
- JwtBearer validation pipeline enforces GUID `sub` and token version match (revocation on version bump/password change) returning 401 with problem+json when mismatched.
- Principal construction now consistently includes mapped role flags (roles_value claim mapping retained) supporting authorization policies.
- Tests: Full suite green (211 passed / 1 skipped) including policy, smoke, and version mismatch scenarios after composite scheme integration.

Deferred / Moved to Future Stories:

- Force logout admin endpoint (increments TokenVersion) → Story 7 (Logout & Revocation UX).
- Optional user TokenVersion cache (userId -> version) to reduce DB hits → Potential optimization (post-1.0 or Story 7 performance subtask).
- Tenant-required endpoint 400 vs 403 decision & standardized problem details shape → Fold into Story 6/7 API consistency pass.

Notes:

- Current direct DB lookup per request acceptable at present load; micro-optimization intentionally deferred.

### Story 5a: Local HTTPS Enablement & Secure Cookie Validation — ✅ DONE (2025-09-20)

Acceptance (fulfilled):

- Local dev can run API over HTTPS using trusted dev certificate (`dotnet dev-certs https --trust`).
- Makefile target `api-https` starts watcher bound to `https://localhost:5198` (same port as HTTP variant for easy switch) after killing any prior process.
- Refresh cookie `rt` sets `Secure` attribute strictly when `Request.IsHttps` (Development environment no longer forces Secure=true without HTTPS).
- Over plain HTTP in Development the cookie omits `Secure` so flows still work pre‑cert trust; over HTTPS the cookie includes `Secure` enabling end-to-end validation of browser delivery.
- Tests cover absence of Secure over HTTP and (simulated) presence when scheme indicates HTTPS.

Implementation Notes:

- Updated three refresh cookie issuance sites in `V1.cs` (login, magic consume, select-tenant) to set `Secure = http.Request.IsHttps` with Story 5a comment on first occurrence; removed prior environment-based override heuristic.
- Added Makefile target `api-https` (re-uses port 5198) with comment referencing required `dotnet dev-certs https --trust` one-time setup.
- Added integration test `RefreshCookieHttpsTests` verifying cookie lacks `Secure` over HTTP and attempts to assert it over simulated HTTPS via `X-Forwarded-Proto: https` header (Kestrel test server constraint noted; assertion guarded but present).
- Documentation updates: this plan (marked done), SnapshotArchitecture (What's New entry), LivingChecklist, and storyLog appended.

Follow-ups / Deferred:

- Potential enhancement: spin an actual HTTPS TestServer (custom host builder) to assert Secure deterministically instead of header simulation (optional, low priority).
- Consolidate duplicate cookie issuance blocks into helper (planned after refresh endpoint Story 6 to avoid churn).

### Story 5b: HTTPS E2E Secure Cookie Validation — ✅ DONE (2025-09-20)

Summary:

- Implemented real HTTPS E2E harness exercising auth flow and confirming secure refresh cookie attributes under TLS (Secure, HttpOnly, SameSite=Lax, Path=/, Expires future, rotation on subsequent auth event).
- Harness & results documented in `SnapshotArchitecture.md`; completion logged in `storyLog.md` and checklist entry updated.
- Prior simulated HTTPS integration test superseded by harness; retained only where still adding coverage.

Deferred / Followups:

- Browser (Playwright) explicit HttpOnly non-access test (optional) → Security Hardening bucket.
- Cross-site SameSite=None exploratory scenarios → Tied to potential future cross-origin embedding (post-refresh endpoint, likely Story 8 or optional followup).
- CSRF double-submit token strategy evaluation (if moving to SameSite=None for certain flows) → Align with refresh endpoint design in Story 6.

Notes:

- Current Lax strategy sufficient for first-party flows; escalation to None will require CSRF mitigation acceptance test.
  // Moved to consolidated Optional Followups section at end of document.

Risks & Mitigations:

- Flaky startup timing → add exponential backoff (max 30s) when polling `/health`.
- Port collisions → attempt up to 5 random ports before failing.
- Certificate trust variance in CI → use self-signed ephemeral cert with handler bypass limited to test harness code.

References:

- Detailed E2E plan: see section "E2E HTTPS Secure Cookie Validation Plan (Added 2025-09-20)" below.

Status:

- Planning phase committed; implementation steps queued (Makefile target `api-https-test`, fixture, test class).

### Story 6: General Refresh Endpoint & Silent Rotation — IN PROGRESS (2025-09-20)

Goal:

Establish a first-class `/api/auth/refresh` endpoint that (a) rotates the refresh token chain, (b) issues a fresh neutral access token (and optionally tenant-scoped token upon explicit param), and (c) transitions clients off JSON-provided refresh tokens toward cookie-only delivery.

Acceptance (MVP):

- Endpoint: `POST /api/auth/refresh` supports two input surfaces:
  1. Preferred: httpOnly cookie `rt=<refreshToken>` (required in production once rollout completes).
  2. Transitional: JSON `{ "refreshToken": "<token>" }` body (grace window; emits deprecation header `Deprecation: true` and `Sunset: <RFC1123 date>` after cutover flag).
- Successful call returns JSON:
  `{ user, memberships, access, refresh, tenantToken? }` (same shape as login/select-tenant) where:
  - `access` = new neutral access token reflecting current `TokenVersion`.
  - `refresh` = new refresh token (plaintext only during grace; after deprecation window, exclude `refresh.token` when cookie path active).
  - `tenantToken` (optional) returned only if client specifies `?tenant=<slug|id>` and the membership is valid; triggers rotation just like neutral flow.
- Old refresh token becomes invalid immediately (revoked_at timestamp set) prior to issuing the new one — single active chain guarantee.
- Reuse of old refresh returns 401 with problem+json `{ code: "refresh_reuse" }` (idempotent detection, logs security warning).
- Expired refresh (expires_at < now) returns 401 `{ code: "refresh_expired" }`.
- Revoked refresh returns 401 `{ code: "refresh_revoked" }` (distinct from reuse if previously flagged).
- TokenVersion bump (e.g., password change) before refresh ensures newly issued access token carries updated version claim.
- Secure cookie rotation: response sets new `Set-Cookie: rt=...; HttpOnly; SameSite=Lax; Path=/; Secure(if https)` replacing prior value.
- All 401 responses omit Set-Cookie for `rt`.
- Logging: structured log (Information) for success `auth.refresh.rotate` with user_id & refresh_token_id (internal id only, never plaintext); Warning for reuse attempt `auth.refresh.reuse_detected`.
- Tests (integration):
  - Success path (cookie) issues new access & refresh; old fails.
  - Success path (JSON body) allowed during grace; includes Deprecation header when flag set.
  - Reuse attempt yields 401 reuse code.
  - Expired token 401 expired code (simulate via seeded expired row or clock override).
  - Revoked token 401 revoked code.
  - Tenant param returns tenantToken with `tenant_id` claim; unauthorized tenant returns 403.
  - Missing token (no cookie & no body) returns 400 `{ code: "missing_refresh" }`.

Security / Hardening Considerations:

- CSRF: For initial Lax cookie strategy, refresh endpoint requires a refresh token (not guessable) + is same-site; still evaluate future double-submit token if moving to `SameSite=None` (deferred to Security Hardening bucket / Story 8 linkage).
- Abuse Mitigation: Add lightweight rate limiting (graceful 429) if refresh storms observed (optional in this story; placeholder in Observability Story 9).
- Replay: Reuse detection (already implemented via revoked_at check + reason) returns distinct error code; consider exponential backoff logging if repeated from same IP.

Rollout Flags:

- `AUTH__REFRESH_COOKIE_ENABLED` (existing) must be ON for cookie path.
- New: `AUTH__REFRESH_JSON_GRACE_ENABLED` controls inclusion & acceptance of `refresh.token` in JSON responses and request body. Default true; set false to enforce cookie-only.
- New: `AUTH__REFRESH_DEPRECATION_DATE` (RFC1123) used to populate `Sunset` header when grace active; absence omits header.

Deprecation Plan:

1. Phase 1 (current): Accept body + cookie; return `refresh.token` always.
2. Phase 2 (set deprecation date): Accept both; add `Deprecation` + `Sunset` headers to body-based responses & still include `refresh.token`.
3. Phase 3 (grace disabled): Reject body token with 400; omit `refresh.token` from response when cookie enabled.

Implementation Outline:

1. Data: Ensure refresh tokens already carry `RevokedAt`, `ExpiresAt` (present). No schema change expected.
2. Endpoint logic:
   - Extract token string from cookie or body; prefer cookie when both provided.
   - Lookup hashed token (Base64 SHA256) row.
   - Validate (not revoked, not expired) else return 401 code.
   - Begin transaction: revoke old (set revoked_at=now, reason='rotated'), insert new row, commit.
   - Issue new access (neutral or tenant) with current TokenVersion.
   - If grace disabled & cookie path used: omit plaintext refresh token from JSON; else include.
3. Cookie issuance: reuse existing `IssueRefreshCookie` helper with new token value.
4. Logging & metrics stubs (counters deferred to Story 9) but insert structured log events.
5. Tests covering above matrix.

Frontend Follow-up (not in this story’s backend scope):

- Add silent refresh loop invoking `/api/auth/refresh` shortly before access expiry using `withAuthFetch` + backoff; handle 401 reuse/expired by forcing re-login.
- Remove reliance on placeholder `/api/_auth/refresh-neutral` route.

Definition of Done:

- Endpoint passes all specified integration tests.
- Sprint plan updated (this section) & SnapshotArchitecture What's New includes entry.
- storyLog appended with start (and completion later) summary.
- LivingChecklist: add line "JWT Story 6: General refresh endpoint…" (unchecked initially, checked on completion).

#### Deprecation & Grace Phase Tasks (Added 2025-09-21)

Phased removal of plaintext refresh tokens from JSON; enforce cookie-only delivery.

1. Phase Tracking & Flags

- Phase 1 (current): `AUTH__REFRESH_JSON_GRACE_ENABLED=true` & no `AUTH__REFRESH_DEPRECATION_DATE` → accept body + cookie; include `refresh.token`.
- Phase 2: set `AUTH__REFRESH_DEPRECATION_DATE` → still accept both; add `Deprecation: true` + `Sunset:` headers on body usage.
- Phase 3: `AUTH__REFRESH_JSON_GRACE_ENABLED=false` → reject body (400 `refresh_body_disallowed`); omit plaintext `refresh.token` (cookie only).

2. Implement / verify structured error `refresh_body_disallowed` (Phase 3 enforcement integration test).
3. Integration test matrix for each phase (headers presence, payload omission, cookie rotation intact, missing token 400, body disallowed 400).
4. Frontend: remove reliance on JSON `refresh.token` once Phase 2 begins; console warn if still observed (temporary telemetry hook).
5. Documentation: SnapshotArchitecture & upgrade guide section describing phases + rollback toggles.
6. Observability: log count of body-based refresh requests after deprecation date (Phase 2) for adoption; optional metric (Story 9).
7. Cleanup: remove transient console warnings and legacy body parsing branch after stable Phase 3.

Status: Subsection scaffolded 2025-09-21; implementation pending refresh endpoint build-out.

### Story 7: Logout & Global Revocation — ✅ DONE (2025-09-21)

Goal:

Provide explicit user + global session termination endpoints leveraging existing refresh store and TokenVersion revocation model, enabling immediate invalidation of refresh chains and active access tokens (via version bump) while logging auditable events.

Endpoints / Flows:

1. `POST /api/auth/logout` (authenticated)

- Reads refresh token from cookie `rt` (preferred) or JSON body `{ refreshToken }` during same grace window as Story 6.
- Validates ownership (token user == principal user).
- Revokes the refresh token (sets `revoked_at`, `revoked_reason='logout'`). Optionally cascades to mark any descendant chain tokens if multi-hop (future: we rotate single chain, so just one entry).
- Clears refresh cookie (`rt=; Expires=past; Path=/; SameSite=Lax; HttpOnly; Secure(if https)`).
- Returns 204 No Content (or 200 with `{ status: 'ok' }`).

2. `POST /api/auth/logout/all` (authenticated)

- Revokes ALL neutral refresh tokens for the user (`UPDATE ... SET revoked_at=now, revoked_reason='logout_all' WHERE user_id=... AND revoked_at IS NULL`).
- Increments `TokenVersion` to invalidate all outstanding access tokens instantly.
- Clears cookie.
- Returns 204.

3. Admin/Support Forced Logout (optional subtask): `POST /api/admin/users/{id}/logout-all` (TenantAdmin or platform role) — may be deferred if no admin surface yet.

Acceptance Criteria:

- Logout (single) removes ability to refresh with that token: subsequent `POST /api/auth/refresh` using its cookie or plaintext results in 401 `refresh_reuse` or `refresh_revoked`.
- Logout does NOT bump TokenVersion (existing access token continues until expiry) — documented behavior.
- Logout All revokes every active refresh AND bumps TokenVersion (subsequent API calls with old access tokens receive 401 `token_version_mismatch`).
- Both endpoints idempotent: repeating the call after success returns 204 (no error) even if tokens already revoked.
- Unauthorized when:
  - No auth principal (401).
  - Provided body refresh token belongs to different user (401 or 403; choose 401 to avoid user enumeration).
- Missing refresh token on `logout` (no cookie, no body) -> 400 `missing_refresh`.
- Metrics/logs (stubs) emit structured events: `auth.logout.single` and `auth.logout.all` with user_id, count_revoked.
- Integration Tests cover: single logout -> refresh fails; logout all -> refresh & access fail appropriately; idempotent second call; mismatched user attempt; missing token.

Security & Privacy Notes:

- Do not leak whether a refresh token existed (generic 204 on already-revoked logout attempts).
- Global revocation uses TokenVersion bump to avoid enumerating still-valid access tokens (stateless invalidation).
- Ensure logout endpoints are protected by Bearer or Dev scheme (composite still active in Development).
- Consider adding optional `X-Session-Id` later if multiple device sessions require selective revocation (post-1.0).

Data Model / Implementation:

- Reuse existing RefreshTokens table; no migration required.
- Add helper in `IRefreshTokenService` (optional) for bulk revoke by user.
- For TokenVersion bump: single UPDATE on Users row (similar to password change logic without password update).

Error Codes (JSON bodies for non-204 cases):

- `missing_refresh` (400)
- `refresh_invalid` (401) – provided token not found / not owned
- `logout_body_disallowed` (400) when grace disabled and only body supplied

Completion Summary (2025-09-21):

- Implemented `/api/auth/logout` (single refresh revoke, cookie clear, explicit 400 `missing_refresh`) and `/api/auth/logout/all` (bulk revoke + TokenVersion bump) with idempotent 204 responses.
- Added `RevokeAllForUserAsync` and safe TokenVersion increment (record detach/replace) to avoid EF mutation issues.
- Structured error codes leveraged: `missing_refresh`, `refresh_invalid`, `refresh_reuse`, `refresh_expired`, `refresh_body_disallowed` (future Phase 3 use).
- Integration tests: single logout reuse invalidation, global logout version mismatch (401), missing token (400), idempotent repeat, ownership mismatch protections — all passing.
- Docs updated: `SnapshotArchitecture.md` (What's New), `devInfo/storyLog.md`, `LivingChecklist.md` ticked; sprint plan updated.
- Deferred: session listing UI, admin forced logout, observability counters (moved to Follow-ups / Story 9).

Follow-ups / Deferred:

- Session listing + selective device logout UI (post-1.0).
- Admin forced logout endpoint (pending admin surface design) if not delivered here.
- Observability counters (Story 9) to increment tokens_revoked on logout actions.

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

### Added Transition Tasks (Story 6 Post-Endpoint)

1. Frontend silent refresh loop: schedule pre-expiry call to `/api/auth/refresh` (e.g., at 60–90% of access TTL), jittered backoff on failures, force re-auth on consecutive 401 `refresh_expired|refresh_invalid` responses. Remove placeholder `_auth/refresh-neutral` route.
2. Deprecation headers activation: when `AUTH__REFRESH_DEPRECATION_DATE` set, ensure body-token refresh responses include `Deprecation: true` + `Sunset: <date>`; add integration test asserting presence and date parse.
3. Grace disable pathway test: simulate `AUTH__REFRESH_JSON_GRACE_ENABLED=false` and assert body-provided token rejected (400 `refresh_body_disallowed`) and plaintext `refresh.token` omitted from response payload.

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

## Optional Followups

- (Story 2) Dual-key (old/new) signing validation window to support seamless key rotation without immediate token invalidation.
- (Story 2) Add rolesLabel[] (human-readable flag names) alongside roles_value in neutral token memberships for DX clarity (currently deferred).
- (Story 2a) Expand TestAuthClient to mint explicit expired / nearly-expired tokens for boundary condition tests (clock skew, refresh edge cases).
- (Story 3) Extract shared Base64 SHA-256 hashing helper for refresh token hashing to prevent future divergence across endpoints/services.
- (Story 3) Introduce per-refresh-token device fingerprint + display name; surface active sessions list + remote revoke UI.
- (Story 4) Consider optional short-lived httpOnly access cookie (`at`) for SSR-only endpoints to reduce need for client in-memory forwarding.
- (Story 4) Implement CSRF double-submit token or SameSite=None + anti-CSRF header secret once cross-site embedding use-cases clarified.
- (Story 5) Add admin API endpoint to force immediate logout for a user (increments TokenVersion) with audit event.
- (Story 5) Lightweight in-memory or distributed cache of (userId -> TokenVersion) with short TTL (e.g., 30s) to reduce DB hits under very high auth load.
- (Story 5a) Automate dev HTTPS enablement in Makefile (target: `make trust-dev-certs`) and document fallback for environments where trust prompt fails.
  - Executed 2025-09-20: Added `trust-dev-certs` target and consolidated cookie issuance helper (`IssueRefreshCookie`).
- (Story 6) Support sliding refresh expiration (extend expires_at on active usage) with max absolute lifetime cap.
- (Story 6) Add reuse detection metric / alert (refresh token replay attempts) for security monitoring.
- (Story 7) Bulk logout by tenant (invalidate all user sessions belonging to a compromised tenant) — may require tenant-scoped refresh entries enumeration.
- (Story 8) Add explicit feature flag kill-switch for JWT path to temporarily re-enable Dev headers in staging if regression found (document emergency rollback).
- (Story 9) Add Prometheus/OpenTelemetry histogram for token validation latency + cache hit ratio (if version cache added).
- (Story 9) Implement structured security event log (JSON lines) shipped to SIEM with category `auth` (issue, revoke, mismatch, reuse_attempt).
- (Story 9a) Provide containerized Caddy alternative config (automatic certs) as an alternative to nginx for simpler local TLS.
- (Story 10) Publish upgrade guide snippet on rotating signing key with minimal downtime (manual dual issuance procedure pre multi-key support).
- (Story 11) Final sweep to remove any dead constants/flags (e.g., legacy invite role error codes) and prune obsolete test helpers replaced by TestAuthClient.
- (Story 5b) Browser-level (Playwright) validation of HttpOnly behavior (deferred).
- (Story 5b) Cross-site SameSite=None scenario investigation (deferred).
- (Story 5b) CSRF token double-submit validation (complements future refresh endpoint and SameSite=None changes).

## E2E HTTPS Secure Cookie Validation Plan (Added 2025-09-20)

Context:
Current integration tests run on ASP.NET Core TestServer (in-memory, no real TLS). Even when `Request.IsHttps` is forced (middleware or startup filter) and logs confirm `Request.IsHttps=true`, the emitted `Set-Cookie` header for the refresh cookie (`rt`) does not include `Secure`. This appears to be a TestServer limitation—Secure is only serialized for actual HTTPS transports (Kestrel + certificate) or certain hosting pipelines.

Goals:

1. Positively assert that in a real HTTPS scenario the refresh cookie includes `Secure; HttpOnly; SameSite=Lax` (and future attributes if added).
2. Ensure regression protection for cookie attributes across environments (Dev over HTTPS, staging, production).
3. Provide a portable harness developers can run locally after trusting dev certs (`make trust-dev-certs`).

Non-Goals:

- Browser automation of the entire login UX (handled by broader web E2E tests later) beyond what is required to capture cookies.
- Testing SameSite cross-site behavior (future CSRF story).

Approach Overview:
We layer an HTTPS E2E test tier above the unit/integration suite:

- Spin up the API with Kestrel HTTPS using dev cert (via `dotnet run` or a dedicated Makefile target `api-https` already partially in place; add headless mode).
- Use an HTTP client that disables cert validation only if the dev cert trust step was skipped (fallback) while encouraging proper trust.
- Execute: signup -> login (JSON flow) and capture `Set-Cookie` headers. Assert `rt` cookie contains required attributes.
- (Optional) Add negative case: plain HTTP request to same endpoint (if dual binding enabled) lacks `Secure`.

Test Layers:

1. Integration (current): Verifies absence of `Secure` over HTTP and structural cookie issuance. (Positive Secure test skipped with rationale.)
2. E2E HTTPS (new): Validates presence of `Secure` and (later) `SameSite` / `Path` / `Expires` semantics under real TLS.

Tooling Additions:

- New Makefile targets:
  - `api-https-test`: Runs the API on an ephemeral port with HTTPS only, minimal logging.
- New test project (or folder) `apps/api.e2e` (if separation desired) using xUnit or Playwright (Node) depending on broader E2E direction.
  - Lightweight .NET console test harness is sufficient if we only need HTTP semantics; adopt Playwright if we need browser cookie store validation.

Option A: Pure .NET HttpClient Harness
Pros: Fast, no browser dependency. Cons: Does not prove browser acceptance rules (but Secure flag presence is enough for server responsibility).

Option B: Playwright Browser Test (Recommended Medium Term)
Pros: Ensures cookie appears in browser devtools context only over HTTPS; can later extend to UI login flow. Cons: Slower, adds Node dependency.

Initial Implementation Recommendation: Option A (upgrade later if needed).

Planned Artifacts:

- `tests/e2e/AuthCookieHttpsTests.cs` (or new `api.e2e` project):
  - Fixture launches API process with `DOTNET_URLS=https://127.0.0.1:5199` (or dynamic free port) and waits for readiness (`/health`).
  - Test 1: `Signup_And_Login_Sets_Secure_RefreshCookie`.
  - Assertions: `rt` cookie string contains `Secure`, `HttpOnly`, `SameSite=Lax`, and `Path=/`.
  - Parse `Expires` -> ensure future UTC.
  - If environment variable `E2E_CHECK_HTTP_FALLBACK=true`, issue same login over `http://127.0.0.1:5199` (if dual) and assert absence of `Secure` (optional, only if dual protocol configured; skip otherwise).

Edge Cases / Additional Assertions:

- Ensure no duplicate `rt` cookies.
- Rotation scenario: perform second login or tenant selection, capture new cookie; assert value changed (if rotation policy continues issuing new refresh on re-auth) while attributes persist.

Environment & Config:

- Requires `dotnet dev-certs https --trust` beforehand (documented in README / Makefile help). Test harness should detect certificate validation failures and emit actionable guidance.
- CI: Use self-signed ephemeral cert generation step (non-trusted) plus custom HttpClient handler bypass only inside CI, never in production code.

Future Enhancements:

- Introduce Playwright flow validating that JavaScript cannot read the `rt` cookie (HttpOnly) while the browser sends it automatically on subsequent same-site requests.
- Add negative test ensuring cookie omitted or different when feature flag `AUTH__REFRESH_COOKIE_ENABLED=false`.
- Validate `SameSite=None` scenario if cross-site embedding becomes necessary (switching assertion based on config).

Open Questions:

- Should we centralize process orchestration (API spin-up) in a reusable test utility for other HTTPS-dependent tests (logout, refresh)? (Likely yes—create `E2EHostFixture`.)
- Port allocation strategy: fixed (simpler) vs dynamic (reduces collision in parallel CI). Suggest dynamic with retry count.

Action Items:

1. Add Makefile target `api-https-test` (HTTPS-only minimal logging mode).
2. Create `tests/e2e` (or `apps/api.e2e`) project & fixture launching API with HTTPS.
3. Implement first Secure cookie E2E test (Option A).
4. Wire into CI workflow (GitHub Action) gating merges when failing.
5. (Optional) Add Playwright later for browser-level confirmation.

Skip Rationale Removal Plan:
Once E2E test is green and reliable, we can:

- Keep current integration Secure test skipped (document linking to E2E) OR delete it to avoid redundancy.
- Reference E2E test in this plan and in `LivingChecklist` under security hardening.

Tracking:

- Add new story: "Story 5b: HTTPS E2E Secure Cookie Validation" referencing this section and mark dependency on Story 5a completion.

---

## (Appended 2025-09-21) Deferred Follow-up

- Deprecation headers and removal of plaintext `refresh.token` after grace.
