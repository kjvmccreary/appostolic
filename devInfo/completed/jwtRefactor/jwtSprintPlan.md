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

### Story 6: General Refresh Endpoint & Silent Rotation — ✅ DONE (2025-09-21)

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

Status: Implemented 2025-09-21. Endpoint `/api/auth/refresh` added with cookie-first strategy, JSON body grace, deprecation headers (Deprecation/Sunset) support, rotation (revokes old then issues new), tenant param issuance, structured error codes (`missing_refresh`, `refresh_invalid`, `refresh_reuse`, `refresh_expired`, `refresh_body_disallowed`). Hashing centralized via `RefreshTokenHashing`. Tests cover: cookie rotation, body grace, reuse detection, expired token, revoked reuse, tenant token issuance, missing token, grace disabled body rejection. Remaining: frontend silent refresh loop (separate follow-up) and deprecation flag rollout sequence. Documentation & storyLog updated.

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

### Story 8: Silent Refresh, Plaintext Removal & Body Path Deprecation — ✅ DONE (2025-09-26)

Goals:

- Frontend performs automatic silent refresh using httpOnly `rt` cookie before access token expiry.
- Remove plaintext `refresh.token` from auth JSON responses (login, magic consume, select-tenant, refresh) when new flag disables exposure.
- Deprecate JSON body refresh path: allow during grace; once grace disabled, body-supplied token yields `refresh_body_disallowed`.
- Add minimal client retry-once strategy for 401 (expired access) to transparently refresh.

New Config Flags:

- `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` (bool, default true) — when false, server omits `refresh.token` field in responses.
- (Existing) `AUTH__REFRESH_JSON_GRACE_ENABLED` continues to govern body path; Story 8 may flip default to false post-adoption.

Acceptance Criteria:

1. When `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT=false`, responses contain `refresh: { id, expiresAt }` (or similar metadata if currently present) but NOT `token`.
2. Frontend decodes access token `exp` (without persisting) and schedules a refresh at `exp - 60s` (bounded: minimum 5s in future; clamp negative to immediate attempt).
3. Frontend `withAuthFetch` (or shared fetch wrapper) performs one retry on 401: triggers refresh call; if successful updates in-memory access and replays original request; if refresh fails (401/400) it surfaces original 401 and signals logout handler.
4. Refresh client call uses `POST /api/auth/refresh` with `credentials: 'include'` and does NOT send body unless grace flag is enabled AND fallback cookie missing.
5. When `AUTH__REFRESH_JSON_GRACE_ENABLED=false`, sending JSON `{ refreshToken }` returns 400 with `{ code: 'refresh_body_disallowed' }` (existing handler already mapped), test added.
6. Web unit tests: (a) scheduler sets timer within expected window; (b) 401 first attempt triggers refresh + succeeds; (c) refresh failure propagates original 401; (d) plaintext omission test asserts shape.
7. API integration test toggling `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT=false` asserts absence of `refresh.token` field.
8. Documentation updated (SnapshotArchitecture What’s New + LivingChecklist + storyLog) citing new flag and removal plan.

Out of Scope / Deferred:

- Metrics counters (`auth.refresh.success|failure`) — follow-up Story 9.
- CSRF double-submit token design (Story 10 / security hardening).
- Access token cookie variant.

Risks & Mitigations:

- Race conditions near expiry → schedule refresh slightly earlier (60s) and guard with in-flight promise to prevent stampede.
- Clock skew causing premature 401 — rely on backend short skew window; retry-once strategy covers edge.
- Legacy clients expecting plaintext refresh token → controlled by flag; can re-enable if rollback needed.

Follow-ups:

- Flip default of `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` to false in all envs post adoption.
- Disable grace (`AUTH__REFRESH_JSON_GRACE_ENABLED=false`) and remove body parsing code in a cleanup story.

Completion Summary (2025-09-26):

- Enforced cookie-only access to `/api/auth/refresh` when JSON grace is disabled, returning `refresh_body_disallowed` for any payloads while preserving existing CSRF and rate limit behavior.
- Updated all refresh-oriented integration suites to post empty bodies via a shared helper so rate limiting, session enumeration, logout, and sliding refresh flows align with the stricter contract.
- Verified frontend silent refresh already issues credentialed, bodyless requests; no code changes required beyond prior Story 8 updates.
- Full `dotnet test apps/api.tests/Appostolic.Api.Tests.csproj --no-build` run passes (292 ✔️ / 1 skipped), confirming backend coverage under the new guard.

### Story 9: Observability & Security Hardening — ✅ DONE (2025-09-22)

Goals:

Provide first wave of production-grade visibility over auth flows (issuance, rotation, revocation, suppression) and enforce lightweight safeguards (rate limiting hooks) without introducing performance regressions. All metrics/log names stable for future dashboards.

Acceptance (implemented subset for metrics/latency; tracing span attrs & full rate limit deferred):

1. Structured logs (no secrets) emitted for each auth event with consistent schema:

- `auth.refresh.rotate` { user_id, refresh_id_old, refresh_id_new, tenant_id? }
- `auth.refresh.reuse_denied` { user_id?, refresh_id?, reason }
- `auth.refresh.expired` { user_id?, refresh_id }
- `auth.refresh.plaintext_emitted` { user_id, refresh_id } (temporary; gated by flag; warn level)
- `auth.refresh.plaintext_suppressed` { user_id } — Retired in Story 31 once cookie-only adoption reached steady state.
- `auth.logout.single` { user_id, token_found }
- `auth.logout.all` { user_id, revoked_count }

2. Metrics registered (OpenTelemetry / .NET Meter `Appostolic.Auth`) using final dot notation instead of earlier underscore placeholders:

- Counters: `auth.tokens.issued`, `auth.refresh.rotations`, `auth.refresh.reuse_denied`, `auth.refresh.expired`, `auth.refresh.plaintext_emitted` (TEMP), `auth.logout.single`, `auth.logout.all`, plus new outcome counters: `auth.login.success`, `auth.login.failure`, `auth.refresh.success`, `auth.refresh.failure`, `auth.refresh.rate_limited`. (`auth.refresh.plaintext_suppressed` retired in Story 31.)
- Histograms: `auth.login.duration_ms`, `auth.refresh.duration_ms` (tag `outcome=success|failure`).

3. Tracing: Add span attributes on existing request spans (Activity Enrichment) for refresh/logout endpoints: `auth.user_id`, `auth.tenant_id`, `auth.refresh.rotate=true` etc.
4. Rate limiting (minimal): Stub path + counter (`auth.refresh.rate_limited`) in place; full token bucket & config flag wiring deferred (follow-up).
5. Update `SnapshotArchitecture.md` Observability section with new metric & log taxonomy + deprecation notice for plaintext metrics.
6. Update `LivingChecklist.md` marking Observability counters once merged.
7. Add storyLog entry on completion.

Non-Goals (Explicit):

- Full-fledged per-user session analytics dashboard (future).
- Adaptive anomaly detection — out-of-scope (manual dashboards only initially).

Implementation Tasks (completion status):

1. Introduce `AuthMetrics` static class exposing Meter + strongly-typed instruments. (DONE)
2. Endpoint instrumentation for login/refresh/logout (direct calls; helper abstraction skipped). (DONE)
3. Plaintext emission/suppression instrumentation. (DONE)
4. Reuse/expired failure instrumentation. (DONE)
5. Logout counters instrumentation. (DONE)
6. Rate limit counter stub (no full algorithm yet). (PARTIAL)
7. Documentation updates (SnapshotArchitecture, LivingChecklist, storyLog, this plan). (DONE)

Testing:

- Added `AuthMetricsTests` asserting presence/registration of new counters & histograms.

### Development Auth Mode Update (2025-09-21)

Dev header authentication (`x-dev-user` / `x-tenant`) is now strictly gated by explicit flag `AUTH__ALLOW_DEV_HEADERS=true` (removed implicit enablement in `Development` environment). Default local runs without setting this flag exercise real JWT flows end-to-end (login → neutral/tenant tokens → refresh rotation → logout), ensuring manual UI testing reflects production behavior. Integration tests that still rely on dev headers set the flag via `WebAppFactory` in-memory configuration; targeted tests (`DevHeadersDisabledTests`) assert 401 when the flag is false and validate pure JWT issuance still succeeds. This change hardens parity between dev and prod while retaining opt-in convenience for low-friction scripting.

- Existing refresh reuse/expired integration tests indirectly exercise failure counters; explicit delta assertions deferred to later observability harness.
- PII safety: counters/logs include only IDs or bounded reason strings (no raw tokens/emails); prior privacy logging tests cover absence of PII.

Risks:

- Log volume growth — mitigated by bounded reason taxonomy & absence of high-cardinality free-form tags.
- Rate limiting false positives — limiter disabled until configuration tuned; only counter increments currently.

Follow-ups (post Story 9):

- Implement full refresh rate limiting middleware + config toggles.
- Add per-user session enumeration instrumentation + session list endpoint.
- Remove plaintext emission/suppression counters after flag retirement (two release quiet period).
- Add tracing span attributes & exemplar Grafana dashboards (success ratio, p95 latency, failure reason breakdown, reuse anomalies).

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

### Story 12: Frontend Auth Fixture Alignment — ✅ DONE (2025-09-26)

Goal:

Ensure the web test suite mirrors the production JWT + refresh rotation flow, preventing regressions where mocked sessions diverge from cookie-backed reality (e.g., tenant selection blank states).

Acceptance (incremental):

- [x] Inventory current frontend auth fixtures, mocks, and gaps (documented in `devInfo/jwtRefactor/audit.md`).
- [x] Introduce shared session fixture module (`test/fixtures/authSession.ts`) and migrate session-dependent component tests to use it.
- [x] Add MSW handlers for `/api/tenant/select` and `/api/auth/refresh`, refactoring tenant-switcher tests to rely on handlers instead of manual fetch spies.
- [x] Author integration tests for `buildProxyHeaders` covering cookie precedence, refresh rotation, and neutral vs tenant token caching.
- [x] Replace ad-hoc fetch mocks in tenant settings forms with MSW-backed flows asserting 401/403 handling.
- [x] Document the new fixture approach in a frontend auth testing guide and link it from this sprint plan (see [Frontend Auth Testing Guide](../../docs/frontend-auth-testing.md)).

Notes:

- Progress captured in `devInfo/jwtRefactor/audit.md` and `docs/frontend-auth-testing.md` (new).
- Aligns with Story 8 deliverables by ensuring the frontend refresh loop has realistic coverage before enabling cookie-only refresh in production.

## Risk & Mitigation

| Risk                      | Impact                | Mitigation                                                |
| ------------------------- | --------------------- | --------------------------------------------------------- |
| Key leakage               | Token forgery         | Use long random signing key; document rotation procedure. |
| Forgotten revocation path | Stale access remains  | Enforce short access TTL + version bump strategy.         |
| Clock skew issues         | False expiry failures | Allow ±60s skew in validation parameters.                 |

## Deferred Follow-Ups Captured Post Story 8

These were identified after completing Story 8 (silent refresh & plaintext suppression) and are queued for upcoming stories (primarily Story 9 and beyond):

- Observability counters & structured events: `auth.refresh.rotation`, `auth.refresh.reuse_denied`, `auth.refresh.plaintext_emitted` (temporary), `auth.logout.single`, `auth.logout.all`. (`auth.refresh.plaintext_suppressed` retired in Story 31.)
- Feature flag retirement plan: schedule removal of `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` after 2 releases with zero plaintext emissions in logs/metrics; document rollback note until deletion.
- Session enumeration endpoint (`GET /api/auth/sessions`) returning active refresh token metadata (id, created_at, last_used_at?, expires_at) without plaintext for future session management UI.
  | Frontend token storage XSS | Token theft | Prefer httpOnly secure cookies (document local dev adjustments). |
  | Race conditions on refresh rotation | Reuse accepted | Enforce single active chain by revoking old before issuing new inside transaction. |

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

## Story 10: Documentation & Upgrade Guide — ✅ DONE (2025-09-22)

Goal: Formalize the JWT auth rollout for internal and external developers: how to enable, migrate, and roll back; clarify transitional flag behaviors; capture final architecture diagram for refresh/access flows, and provide operational guidance (key rotation, deprecation phases, observability usage).

Acceptance Checklist (all completed):

[x] Upgrade Guide section (new `docs/auth-upgrade.md`):

- Generating a secure signing key (Base64 256-bit) & environment variable placement.
- Enabling cookie-based refresh (`AUTH__REFRESH_COOKIE_ENABLED`), optional silent refresh timeline.
- Transitional flags table: (`AUTH__REFRESH_JSON_GRACE_ENABLED`, `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT`, `AUTH__REFRESH_DEPRECATION_DATE`, `AUTH__ALLOW_DEV_HEADERS`).
- Rollback procedure: re-enable dev headers, reset grace flags, purge refresh tokens if compromise.
- Key rotation: manual procedure (create new key, brief dual validation note deferred until multi-key support) + risk mitigation (short access TTL).
- Refresh reuse / expired troubleshooting (how to interpret error codes).
  [x] SnapshotArchitecture updates:
- Final simplified auth flow diagram (Login → Neutral Access + Refresh Cookie; Select Tenant; Refresh rotation; Logout/All).
- Removal of placeholder internal refresh route references; explicit note about dev header decommission next sprint (link to RDH plan).
  [x] LivingChecklist: add & tick “JWT Story 10 – Documentation & Upgrade Guide”.
  [x] storyLog: entry summarizing completion with key doc artifact references.
  [x] Add `docs/diagrams/auth-flow.mmd` (Mermaid) diagram asset + referenced in SnapshotArchitecture & guide.
  [x] Document metric taxonomy usage and interpretation of `auth.refresh.reuse_denied` vs `auth.refresh.expired` for support.
  [x] Add “Writing Authenticated Tests” paragraph (TestAuthClient) in upgrade guide.
  [x] Clarify cookie security rationale & forward CSRF considerations.
  [x] Forward reference to RDH sprint (dev header removal) and rationale for deferred deletion.

Non-Goals:

- Implementing multi-key validation (explicitly deferred).
- Expanding session listing endpoints (post‑1.0 security UX).

Risks / Mitigation:

- Risk: Docs drift as RDH removes flags → Mitigate by explicitly calling which parts remain temporary and referencing removal plan.
- Risk: Overly prescriptive key rotation before multi-key → Keep section labeled “Interim rotation approach”.

Completion Summary (2025-09-22): Auth upgrade guide (`docs/auth-upgrade.md`) created detailing key generation, transitional flags/phases, rollout/rollback, interim key rotation, error codes, metrics taxonomy, security rationale, and support troubleshooting. Added Mermaid auth flow diagram (`docs/diagrams/auth-flow.mmd`) and new Auth Flow section in `SnapshotArchitecture.md` replacing prior placeholder description. Removed obsolete placeholder route reference, cross-linked RDH decommission plan for dev headers. Updated `LivingChecklist.md` (Story 10 ticked) and appended storyLog entry. All acceptance items fulfilled; no remaining TODO placeholders. Story 11 cleanup now proceeds (dev header removal explicitly deferred to RDH sprint).

Definition of Done: All acceptance items checked; doc artifacts present; sprint plan & logs updated; no unchecked internal boxes.

## Story 11: Cleanup & Legacy Removal — ✅ DONE (2025-09-22)

Goal: Finalize JWT sprint scope by pruning artifacts introduced only for transition or experimentation, without encroaching on the Dev Header Decommission (RDH) responsibilities.

AC (Scoping STRICTLY to JWT sprint remnants):

[x] Remove stale comments referencing: placeholder `_auth/refresh-neutral` route (already removed) or legacy plaintext permanence assumptions.
[x] Delete any unused error codes, constants, or feature flag checks that became no-ops after Stories 8–9 (EXCLUDING dev header flag & related code — defer to RDH).
[x] Ensure `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` documented as transitional; DO NOT remove yet (pending adoption window) — marked with TODO and RDH reference in `V1.cs`.
[x] Verify no dead test helpers superseded by `TestAuthClient`; remove obsolete helpers (none found — confirmed via grep).
[x] Run grep for `TODO JWT-SHORT-LIVED` or similar markers; resolve or convert to explicit RDH / Post‑1.0 backlog references (none remaining outside plan docs).
[x] Ensure no inline hashing duplication survived (all use `RefreshTokenHashing`).
[x] Confirm metrics instrumentation has no dormant counters left commented out (none found; plaintext counters intentionally TEMP and documented).
[x] Tag repository `jwt-auth-rollout-complete` (annotated with summary & reference commit shas for key stories).
[x] Update `LivingChecklist.md` marking JWT sprint fully complete; add pointer to RDH sprint kickoff.
[x] Add storyLog entry summarizing cleanup scope & explicitly stating dev header removal deferred.

Important Constraints (DO NOT VIOLATE — coordination with RDH):

> NOTE: Do not remove dev header authentication handler, composite scheme wiring, or `AUTH__ALLOW_DEV_HEADERS` flag in Story 11. Those belong to RDH plan phased removal (test migration, deprecation middleware, removal, regression guard). Limiting this cleanup prevents story overlap and preserves a clear rollback boundary.

Explicit Deferrals to RDH:

- Dev header auth handler & composite scheme deletion.
- Flag `AUTH__ALLOW_DEV_HEADERS` removal.
- Regression guard test (`dev_headers_removed`).

Post-Story 11 State:

- Codebase stable with JWT flows; all remaining auth flags are either transitional (documented) or scheduled in RDH.
- Documentation cross-links established so developers know next sprint location for header removal.

Definition of Done:

- All AC items checked, constraints honored, tag pushed, storyLog updated. No accidental deletion of RDH-targeted assets.

Completion Summary (2025-09-22):

- Performed scoped cleanup: verified absence of stale placeholder route references, ensured transitional plaintext exposure flag (`AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT`) clearly commented for future removal (RDH follow-up), confirmed no obsolete auth test helpers beyond `TestAuthClient`, validated hashing centralization (no stray SHA256 usages), and confirmed metrics file free of commented prototype instruments. No code logic modified—comments and documentation only. Updated LivingChecklist (Story 11 line & next sprint pointer), appended storyLog entry, and created annotated tag `jwt-auth-rollout-complete` capturing sprint scope (Stories 1–11). Dev header removal and flag retirement explicitly deferred to upcoming RDH sprint to maintain rollback boundary.

Post-Story 11 State:

- JWT auth foundation (issuance, rotation, revocation, silent refresh, observability, documentation) complete and tagged.
- Transitional flags documented; only `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` (suppression) and `AUTH__ALLOW_DEV_HEADERS` (decommission) remain for RDH sprint decisions.
- Safe rollback: tag predecessor retains pre-cleanup state; no functional divergence introduced here.

Rollback (Story 11 changes only):

- Revert cleanup commit or checkout tag `jwt-auth-rollout-complete` predecessor; minimal risk because production auth path unaffected by deletions.

## Recent Adjacent Hardening (2025-09-21)

Outside the core JWT sprint stories, a small stabilization pass was completed to reduce future regression risk:

- Roles Assignment Endpoint Refactor: Unified duplicated transactional code paths for member role updates (eliminated provider‑specific branching) reducing risk of inconsistent audit writes and resolving intermittent EF InMemory test flakiness.
- Audit Trail Regression Guard: Added test ensuring a second identical roles update (noop) does not create a duplicate audit record (protects against accidental future double-write logic reintroduction).
- Async Warning Cleanup: Removed an unnecessary async lambda (CS1998) in the deprecated legacy member role change endpoint for a cleaner build signal surface.

These changes are orthogonal to JWT issuance/refresh/logout flows but contribute to overall auth & roles integrity. No sprint story numbers assigned (tracked in storyLog); included here for situational awareness. If further non-JWT hardening items accumulate, consider a lightweight "Auth & Roles Hardening" epilogue story before final cleanup (Story 11) or a post‑1.0 bucket entry.

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

## Appendix: Auth Observability Dashboards & OTel Collector Config (Added 2025-09-22)

This appendix provides ready-to-adopt Grafana panels (PromQL) and OpenTelemetry Collector snippets to operationalize the Story 9 metrics. Keep names in sync with `AuthMetrics` (`Appostolic.Auth` Meter). Adjust namespace/job labels per your deployment (examples assume Prometheus scrape with labels `job="api"`).

### Prometheus Recording Rules (Optional but Recommended)

```
groups:
- name: auth_recording.rules
  interval: 30s
  rules:
  - record: auth:login:success_ratio
    expr: rate(auth_login_success_total[5m]) / clamp_min(rate(auth_login_success_total[5m]) + rate(auth_login_failure_total[5m]), 1)
  - record: auth:refresh:success_ratio
    expr: rate(auth_refresh_success_total[5m]) / clamp_min(rate(auth_refresh_success_total[5m]) + rate(auth_refresh_failure_total[5m]), 1)
  - record: auth:refresh:reuse_rate
    expr: rate(auth_refresh_reuse_denied_total[5m])
  - record: auth:refresh:expired_rate
    expr: rate(auth_refresh_expired_total[5m])
  - record: auth:logout:all_rate
    expr: rate(auth_logout_all_total[5m])
```

If using native OTEL -> Prom remote write naming (dot metric names) you may instead see metrics already as counters named exactly `auth.login.success`. Mapping layer (e.g., the Collector) may translate dots to underscores. Adjust queries accordingly:

`auth_login_success` OR `auth_login_success_total` depending on exporter.

### Core Grafana Panels

1. Login Success Ratio
   - PromQL: `auth:login:success_ratio`
   - Thresholds: warn < 0.98, critical < 0.95
2. Refresh Success Ratio
   - PromQL: `auth:refresh:success_ratio`
   - Alert: sustained (<0.98) for 10m -> investigate credential / storage issues.
3. Refresh Failure Breakdown (Stacked Bar)
   - Query: `sum by (reason)(increase(auth_refresh_failure_total[5m]))`
   - Panel: Stacked bars over 1h window.
4. Reuse vs Expired Trend
   - Query A: `rate(auth_refresh_reuse_denied_total[5m])`
   - Query B: `rate(auth_refresh_expired_total[5m])`
   - Overlay to detect anomalous reuse spikes (potential replay attacks).
5. Login/Refresh Latency (p95 & p99)
   - Using histograms via Prometheus native histogram (if exported) or manual approx if only raw OTEL histograms → ensure Collector enables histogram export.
   - Example (OpenMetrics native buckets): `histogram_quantile(0.95, sum by (le)(rate(auth_login_duration_ms_bucket[5m])))`
6. Logout Activity
   - Query: `rate(auth_logout_single_total[5m])` & `rate(auth_logout_all_total[5m])`
   - Purpose: correlate surge in logout-all with possible security incident.
7. Plaintext Emission Monitor (TEMP)
   - Query: `increase(auth_refresh_plaintext_emitted_total[1h])`
   - Add annotation: if >0 after planned deprecation window.
8. Rate Limited Refreshes
   - Query: `rate(auth_refresh_rate_limited_total[5m])`
   - Alert if > 0 for 15m (unless limiter intentionally enabled) or > threshold (e.g., 0.05 req/s) when enabled.
9. Membership Distribution (Login Context)
   - Query: `sum by (memberships)(increase(auth_login_success_total[6h]))`
   - Helps size single vs multi-tenant user population.

### Suggested Alerts (Prometheus Alerting Rules)

```
groups:
- name: auth.alerts
  rules:
  - alert: AuthRefreshReuseSpike
    expr: rate(auth_refresh_reuse_denied_total[5m]) > 0.05
    for: 10m
    labels:
      severity: high
    annotations:
      summary: "Refresh token reuse spike"
      description: "Reuse denial rate {{ $value }} >0.05/s (possible replay attacks). Investigate security logs."
  - alert: AuthRefreshSuccessRatioLow
    expr: auth:refresh:success_ratio < 0.95
    for: 15m
    labels:
      severity: critical
    annotations:
      summary: "Refresh success ratio degraded"
      description: "Sustained low refresh success ratio (<95%). Check DB latency, token store health, feature flags."
  - alert: AuthLoginSuccessRatioLow
    expr: auth:login:success_ratio < 0.95
    for: 15m
    labels:
      severity: warning
    annotations:
      summary: "Login success ratio degraded"
      description: "Investigate credential failures, external dependencies (email provider), or brute force attempts."
  - alert: AuthPlaintextEmissionAfterDeprecation
    expr: increase(auth_refresh_plaintext_emitted_total[1h]) > 0
    for: 1h
    labels:
      severity: medium
    annotations:
      summary: "Unexpected plaintext refresh token emission"
      description: "Plaintext refresh tokens emitted post deprecation — verify flag rollback or client migration issues."
```

### OpenTelemetry Collector Example (Metrics Pipeline)

Minimal pipeline converting OTLP → Prometheus + logging. Adjust resource detection processors for your infra.

```yaml
receivers:
  otlp:
    protocols:
      http:
      grpc:

processors:
  batch: {}
  resourcedetection:
    detectors: [env, host]
    timeout: 2s
    overwrite: false

exporters:
  prometheus:
    endpoint: 0.0.0.0:9464
    const_labels:
      service: api
  logging:
    loglevel: info

service:
  pipelines:
    metrics:
      receivers: [otlp]
      processors: [resourcedetection, batch]
      exporters: [prometheus, logging]
```

If using the Prometheus remote_write collector exporter instead of native scraping:

```yaml
exporters:
  prometheusremotewrite:
    endpoint: https://prom.example.com/api/v1/write
    headers:
      X-Scope-OrgID: appostolic
    external_labels:
      service: api
```

### Tracing Enrichment (Planned)

Planned span attribute additions (future story):

| Endpoint                      | Attributes                                                                                 |
| ----------------------------- | ------------------------------------------------------------------------------------------ | ---- |
| /auth/login                   | `auth.user_id`, `auth.login.outcome`, `auth.login.reason?`                                 |
| /auth/refresh                 | `auth.user_id?`, `auth.refresh.outcome`, `auth.refresh.reason?`, `auth.refresh.tenant_id?` |
| /auth/logout,/auth/logout/all | `auth.user_id`, `auth.logout.scope=single                                                  | all` |

Collector tail-sampling (future) can drop non-error auth spans once baseline performance validated; retain a fixed percentage (e.g., 10%) plus all failures.

### Dashboard Implementation Checklist

- [ ] Import recording & alert rules into Prometheus
- [ ] Enable histogram export in Collector (if disabled) for latency quantiles
- [ ] Create Grafana folder "Auth"
- [ ] Add panels listed above with 7d time range defaults
- [ ] Wire alertmanager routes (auth.\* alerts to security on-call)
- [ ] Verify zero plaintext emissions after flag retirement (panel 7 flatline)
- [ ] Add annotations for major auth deployments / flag changes (refresh grace disable, plaintext flag removal)

### Operational Runbook Snippets

Incident: Refresh reuse spike

1. Check panel 4 (Reuse vs Expired) — confirm reuse elevated relative to baseline.
2. Correlate with deployments or suspicious IP ranges (if available) in logs.
3. Consider temporary rate limit enablement with stricter thresholds.
4. If compromise suspected, force global logout (increment TokenVersion for affected users/tenants) and rotate signing key if necessary.

Incident: Login success ratio drop

1. Examine failure reason breakdown (panel 3 for login — extend metric taxonomy if needed to differentiate reasons further).
2. High `invalid_credentials` may indicate brute force attempt — enable WAF / captcha.
3. Elevated `unknown_user` with low absolute login volume could be scripted enumeration — consider IP block.

Maintenance: Plaintext metric retirement

1. Confirm `auth_refresh_plaintext_emitted_total` remains zero for 2 full releases.
2. Remove emission code + counter + docs section; update dashboards to drop panel 7.
3. Add migration note to Story 11 / cleanup log.

---

End of Appendix

- Deprecation headers and removal of plaintext `refresh.token` after grace.
