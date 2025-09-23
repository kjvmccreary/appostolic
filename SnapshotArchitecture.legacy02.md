## Appostolic — Architecture Snapshot

Generated: 2025-09-23 (Auth Test Deterministic Seeding complete; full suite green)
Purpose: Fast context bootstrapping for new chats. Point‑in‑time only (no history). Evolution lives in `devInfo/storyLog.md` + git.

### 1. High‑Level Overview

Monorepo (pnpm + .NET):

- API (.NET 8 Minimal APIs, EF Core 8/PostgreSQL, OpenTelemetry metrics/spans – initial wave)
- Web (Next.js 14 App Router, React 18, TS, MUI Premium)
- Workers (notifications, render – scaffolds)
- Mobile (Expo TS scaffold)
- Shared packages: models, sdk, ui, prompts, docgen, video-scenes
- Infra: docker compose (Postgres, Redis, MinIO, Mailhog, Qdrant), devcontainer, GH workflows
  Focus: Auth revamp (JWT + refresh rotation), Roles flags consolidation, Dev Header decommission (RDH), early Observability.

### 2. Runtime Components

- HTTP: Kestrel (dev); nginx reverse proxy story upcoming
- DB: PostgreSQL (EF migrations; use `make migrate`)
- Object Storage: MinIO (avatars, tenant branding)
- Cache / ephemeral: Redis (light usage today)
- Vector/Search: Qdrant (future integration point)
- Email: SMTP (Mailhog) + SendGrid path
- Background: Hosted services now; future queue workers (notifications/render)

### 3. Auth Model (Current)

Tokens: Neutral Access, Tenant Access, Refresh (hashed Base64(SHA256), rotated).
Implemented:

- Refresh rotation & reuse detection
- TokenVersion revocation (password change + logout all)
- Refresh via secure httpOnly cookie (grace JSON body path flag‑gated)
- Structured error codes (`refresh_invalid`, `refresh_reuse`, `missing_refresh`, `dev_headers_deprecated` …)
- Logout (single + all) endpoints
- Silent refresh loop on web (pre‑expiry schedule, single‑flight)

Dev Header Decommission (RDH lifecycle):

1. Migration (tests moved to real JWT) ✅
2. Guard test (blocks reintroduction) ✅
3. Deprecation middleware (`dev_headers_deprecated`) ✅ CURRENT
4. Physical removal (Story 4): remove handler + composite scheme + flag; add `dev_headers_removed`
5. Hard guard & cleanup: retire metric, static scan, rollback tag
6. Monitoring window (zero usage confirmation)

### 4. Authorization / Roles

- Bit flags: TenantAdmin(1)|Approver(2)|Creator(4)|Learner(8)
- Legacy single role & UI fallbacks removed; flags authoritative
- Legacy‑only payload now normalized to `NO_FLAGS`

### 5. Key Feature Flags

| Flag                                  | Purpose                               | State                     |
| ------------------------------------- | ------------------------------------- | ------------------------- |
| AUTH\_\_ALLOW_DEV_HEADERS             | Permit deprecated dev headers         | FALSE (middleware denies) |
| AUTH\_\_REFRESH_JSON_GRACE_ENABLED    | Allow transitional JSON body refresh  | TEMP                      |
| AUTH\_\_REFRESH_JSON_EXPOSE_PLAINTEXT | Emit plaintext refresh token field    | TEMP (default false)      |
| AUTH\_\_REFRESH_DEPRECATION_DATE      | Adds deprecation headers to body path | Optional                  |
| AUTH\_\_TEST_HELPERS_ENABLED          | Enable gated mint endpoints           | Non‑Prod only             |

Retirement targets: dev headers flag (Story 4), plaintext exposure flag + TEMP metrics post rollout.

### 6. Observability (Auth Wave 1)

Meter: Appostolic.Auth

- Counters: login success/failure, refresh success/failure, rotation, reuse_denied, TEMP plaintext_emitted/plaintext_suppressed, logout single/all
- Histograms: latency (login, refresh)
- Middleware counter: auth.dev_headers.deprecated_requests
  Planned: span enrichment, dashboards, retire TEMP counters after removal phases.

### 7. Condensed Directory Map

```
apps/
  api/          # Minimal API layers
  api.tests/    # Integration + regression suites
  api.e2e/      # Higher-level E2E harness
  web/          # Next.js frontend
  notifications-worker/
  render-worker/
packages/       # models, sdk, ui, prompts, docgen, video-scenes
infra/          # devcontainer, docker, github workflows
devInfo/        # sprint plans, storyLog, LivingChecklist
```

### 8. Data Model (Selected)

- users (TokenVersion)
- refresh_tokens (hash, metadata jsonb, revoked flags)
- tenants, memberships (roles bitmask; legacy role removed)
  Other evolving domains: see migrations for canonical schema.

### 9. Request Lifecycle (API)

Kestrel → DevHeadersDeprecation middleware → JWT auth → validators → handlers → JSON `{ code?: string }` → metrics/logs.
Error strategy: stable machine `code` asserted in tests; status matches semantics.

### 10. Frontend Auth Client

- In‑memory access token only (no localStorage)
- Silent refresh 60s before expiry (single-flight)
- 401 retry-once after refresh race
- Adapts to absence of plaintext refresh token field

### 11. Security Snapshot

- Refresh token plaintext never persisted
- Prod requires configured signing key (dev ephemeral fallback only)
- Progressive removal of dev-only auth paths
- Upcoming: CSRF review if SameSite changes; session enumeration endpoint

### 12. Near-Term Stories

- Story 4: Remove dev header handler & composite scheme; introduce `dev_headers_removed`
- Post: retire TEMP metrics & plaintext exposure flag; tighten refresh body grace
- Observability dashboards + span attributes enrichment
- Potential `/api/auth/session` listing endpoint (backlog)

### 13. Maintenance Guidance

At each story completion:

1. Update this snapshot (target < 250 lines)
2. Refresh “Near-Term Stories” & flags table
3. Remove retired metrics/flags
4. Put narrative detail in `storyLog`

### 14. Quick Facts

Stack: .NET 8, EF Core 8, Next.js 14, React 18, TS, MUI, OpenTelemetry
Auth: Neutral + tenant JWT, hashed refresh rotation, TokenVersion revocation
Roles: Flags only (TenantAdmin|Approver|Creator|Learner)
Migrations: `apps/api/Migrations`

<!-- End of snapshot (refresh at end of each story) -->

### 15. Test Strategy (Update 2025-09-23)

Integration tests no longer perform multi-step password + login + select-tenant flows except in the small set of suites explicitly validating those auth endpoints. All other domain/integration tests now obtain JWTs via a deterministic seeding helper (TestAuthSeeder) that creates user, tenant, membership, and issues neutral or tenant-scoped access tokens directly. Benefits: (1) removes brittle coupling to auth flow shape, (2) reduces test runtime, (3) eliminates 403 regressions tied to omitted select-tenant, (4) stabilizes data setup with per-test unique fragments. Shared helper `UniqueId` (Frag/Slug/Email) standardizes short unique identifiers across tests (replacing scattered inline Guid slicing). A minimal set of end-to-end auth tests (login, select-tenant, refresh, logout, cookie/HTTPS, plaintext suppression, metrics) remain to exercise the real pipeline. Full API test suite passing post‑refactor: 239 passed / 1 skipped (34s arm64 local). E2E harness (api.e2e) still executes real server for cookie issuance & secure claim validation.

    - Added Makefile target `api-https` leveraging trusted dev certificate (`dotnet dev-certs https --trust`) to run API over HTTPS locally for true Secure cookie validation. Refresh cookie (`rt`) issuance logic updated (login, magic consume, select-tenant) to set `Secure = http.Request.IsHttps` (removed previous environment heuristic); over HTTP in Development cookie is not Secure, over HTTPS it is. New test `RefreshCookieHttpsTests` asserts absence of Secure over HTTP and simulated presence with `X-Forwarded-Proto: https`. Simplifies semantics ahead of general refresh endpoint (Story 6) and avoids false Secure flag expectations during local dev without TLS.
    - Follow-up (2025-09-20): Consolidated refresh cookie issuance into `IssueRefreshCookie` helper; added `trust-dev-certs` Makefile target; HTTPS test now uses HTTPS base address for deterministic `Request.IsHttps`.

- Auth/JWT — Story 5 Access Token Revocation (2025-09-20)
  - Added `TokenVersion` (int, default 0) column on users; every issued access token carries claim `v` (stringified int). `OnTokenValidated` event now queries the user’s current `TokenVersion`; if token claim `v` < stored value, authentication fails with `token_version_mismatch`. Password change endpoint increments `TokenVersion` atomically after successful verification, invalidating all previously issued access tokens while leaving refresh tokens to obtain a new access on next refresh flow. This avoids server-side token blacklists and enables instant revocation on credential compromise. Added claim mapping resilience: validation falls back to `ClaimTypes.NameIdentifier` when raw `sub` claim is mapped away; change-password endpoint falls back to `ClaimTypes.Email`. Integration test `AccessTokenVersionTests` confirms old token invalidation after password change. Future: admin-initiated version bump endpoint; potential caching of per-user version for high RPS.
- Auth — JWT Baseline (Story 1) introduced (2025-09-20)
- Auth/JWT — Story 5 (Validation Middleware & Principal) marked complete (2025-09-20) — composite scheme + GUID sub + token version checks (see section below for TokenVersion revocation details).
- Auth/JWT — Story 5b HTTPS Secure Refresh Cookie E2E Harness complete (2025-09-20) — deterministic Secure attribute validation under real TLS.
  - Auth/JWT — Story 6 Refresh Endpoint (COMPLETED 2025-09-21)
    - Implemented `/api/auth/refresh` (cookie-first, transitional JSON body behind `AUTH__REFRESH_JSON_GRACE_ENABLED`) rotating neutral refresh tokens (revocation + issuance) and returning new neutral access token plus optional tenant-scoped token when `?tenant=` provided and membership matches. Structured 401 codes: `refresh_invalid`, `refresh_reuse`, `refresh_expired`; 400 `missing_refresh` and `refresh_body_disallowed`. Deprecation headers (`Deprecation: true`, `Sunset: <date>`) emitted when body token used and `AUTH__REFRESH_DEPRECATION_DATE` configured. In-memory EF provider incompatibility with transactions resolved by removing explicit transaction wrapping for rotation (sequential revoke/issue is acceptable given low contention). Tests `RefreshEndpointTests` now all passing (7/7) including rotation, tenant issuance, reuse detection, revoked reuse, missing token 400, expired, and grace body path. SnapshotArchitecture, LivingChecklist, and storyLog updated.
    - Centralized hashing: Added `RefreshTokenHashing` helper (Base64(SHA256(token UTF8))) consumed by `RefreshTokenService` and all endpoints (login, magic consume, select-tenant, refresh, logout) eliminating duplicated inline hashing code blocks and preventing future drift.
  - Auth/JWT — Story 7 Logout & Global Revocation (COMPLETED 2025-09-21)
    - Implemented endpoints:
      - `POST /api/auth/logout` — Revokes a single neutral refresh token (from cookie `rt` or JSON body `refreshToken` during grace). If a JSON body is present but missing `refreshToken`, returns 400 `{ code: "missing_refresh" }` (tests enforce). Clears cookie when present. Idempotent 204 otherwise (including already revoked/missing token scenarios). Structured log: `auth.logout.single user=<id> tokenFound=<bool>`.
      - `POST /api/auth/logout/all` — Bulk revokes all active neutral refresh tokens for the user and increments `User.TokenVersion` (record detach + replacement) to invalidate existing access tokens immediately. Always 204; logs `auth.logout.all` (or `user_missing`). Clears cookie if present.
    - Error codes: `missing_refresh` (400) on single logout when body supplied without token; reuse/invalid continue to surface via refresh endpoint (`refresh_reuse|refresh_invalid`).
    - Tests (`LogoutTests`) cover: single logout reuse detection, global logout access token invalidation, missing token 400, idempotent second logout. All passing.
    - Rationale: Provides explicit single‑device logout and full-session revocation using lightweight TokenVersion bump—no per access token storage required.
    - Deferred follow-ups: session listing & selective device logout, admin forced logout, deprecation headers for body path, removal of plaintext `refresh.token` post grace.
  - Auth/JWT — Story 8 Silent Refresh & Plaintext Refresh Token Suppression (COMPLETED 2025-09-21)
    - Backend flag `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` introduced. When false (default), API auth responses (login, magic consume, select-tenant, refresh) omit the plaintext `refresh.token` field while still returning its hash-derived metadata (id, created, expires) if present; the httpOnly cookie `rt` becomes the exclusive delivery channel. When true (temporary transitional aid), plaintext continues to be emitted (mirrors prior shape) enabling gradual frontend rollout.
    - Refresh endpoint (`/api/auth/refresh`) further gates plaintext emission: it only returns a plaintext refresh token when BOTH the expose flag is true AND either the JSON grace body path is used (`AUTH__REFRESH_JSON_GRACE_ENABLED`) or the secure cookie feature is disabled. This guarantees we never redundantly emit a plaintext token during the steady-state cookie-first flow once clients have migrated.
    - Unified object shaping: Replaced earlier anonymous conditional inline constructions with explicit local object composition (`var response = new { ... }`) followed by conditional mutation to append `refresh = new { token = <plaintext>, ... }` only when permitted, avoiding compilation issues from divergent anonymous types and simplifying future field additions.
    - Frontend: Implemented silent refresh loop in `apps/web/src/lib/authClient.ts` invoking real backend `/api/auth/refresh` (removing placeholder `_auth/refresh-neutral`). Schedules a refresh 60s before access token expiry (configurable skew constant), single-flights concurrent refresh attempts, and provides `startAutoRefresh`, `stopAutoRefresh`, and `forceRefresh` helpers. Adds 401 retry-once logic to `withAuthFetch` to transparently recover from narrowly expired access tokens (race between request send and expiry) without duplicating user actions.
    - Tests: Added `RefreshPlaintextExposedFlagTests` integration suite validating presence/absence of `refresh.token` across login & refresh under both flag states. Extended frontend unit tests (`authClient.test.ts`) to cover retry-once logic and scheduling behavior (mocked timers + forced expiry). All existing auth suites remain green.
    - Security rationale: Eliminates routine exposure of long-lived refresh secrets to JavaScript, reducing XSS blast radius. Cookie (`HttpOnly; SameSite=Lax; Secure over HTTPS`) confines token to browser credential channel; access token remains short-lived and in-memory only. Transitional flag permits incremental safe rollout and rollback lever if clients unexpectedly depend on plaintext field.
    - Operational considerations: Metrics/observability hooks for refresh rotations and suppressed/plaintext emission counts deferred to Observability hardening story. CSRF review (double submit or SameSite strategy) flagged for later if `SameSite=None` becomes necessary (e.g., cross-site embedding requirements).
    - Follow-ups:
      - Remove `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` after deprecation window once all clients verified.
      - Potential: Add `/api/auth/session` enumerator to expose active refresh token metadata (no plaintext) for future session management UI.
      - Observability counters: `auth.refresh.rotation`, `auth.refresh.reuse_denied`, `auth.refresh.plaintext_emitted` (temporary), `auth.refresh.plaintext_suppressed`.
      - Evaluate CSRF protection adjustments if moving to `SameSite=None` for any cross-origin flows.
    - Auth/JWT — Story 9 Auth Observability Metrics (2025-09-22)
      - Added first-wave OpenTelemetry metrics (Meter `Appostolic.Auth`) for login, refresh, token issuance, rotation, reuse/expired denial, plaintext emission/suppression (temporary), logout, and latency histograms. Chosen stable dot notation (`auth.login.success`, `auth.refresh.failure`, etc.) instead of earlier underscore placeholders documented in sprint plan. Endpoint `V1.cs` instrumented for /auth/login and /auth/refresh success/failure (bounded reason taxonomies) plus logout endpoints. Added rate‑limited counter increment path gated by existing refresh rate limiter flag. New test `AuthMetricsTests` asserts presence of new counters and histograms. Plaintext emission/suppression counters marked TEMP and will be removed post flag retirement. Documentation (this file, sprint plan, LivingChecklist, storyLog) updated; dashboards & span attribute enrichment deferred.
  - Added initial JWT access token infrastructure with `AuthJwtOptions` (issuer, audience, base64 signing key, TTLs, skew) and `JwtTokenService` (HS256). Authentication now registers `JwtBearerDefaults.AuthenticationScheme` plus retains the existing dev header scheme (conditional) for development ergonomics. A protected smoke endpoint `/auth-smoke/ping` validates end-to-end token issuance & validation, covered by new integration test `AuthJwtSmokeTests` issuing a neutral token. Swagger now includes a Bearer security definition alongside the DevHeaders scheme. Ephemeral signing key generation occurs only in Development when no key is configured; Production requires explicit `Auth:Jwt:SigningKey` (base64) and will fail fast if absent. Post-configure pattern applies validation parameters (issuer, audience, key, clock skew) without prematurely building a service provider. Subsequent stories will layer tenant claims, refresh tokens (secure httpOnly cookie), rotation & revocation via token_version, secure cookie flags (SameSite=Lax/Strict + secure), local HTTPS (Story 5a), nginx reverse proxy (Story 9a), and observability (counters for issuance/validation/failures).
- After dropping legacy `role` columns and enforcing bitmask constraints, the invite creation endpoint now surfaces the generic `{ code: "NO_FLAGS" }` validation error when a request supplies only the deprecated single `role` field (and no `roles` / `rolesValue` flags). The earlier transition-specific `LEGACY_ROLE_DEPRECATED` error is now reserved only for the still-deprecated member single-role change endpoint (documented by its regression test) until that path is removed in a later story. Updated regression test `Invite_with_legacy_role_only_is_rejected_with_NO_FLAGS` reflects this invariant; full API test suite passes (193/193) post-clean rebuild.
- Rationale: With the legacy column physically removed, treating a legacy-only payload as simply “missing flags” simplifies client handling and avoids implying a reversible transitional path.
- Follow-up: remove the member single-role change legacy acceptance path and consolidate on a single error (`NO_FLAGS`) across all IAM write paths; publish rollback script/tag (`roles-removal-complete`).
- Auth/JWT — Story 2 Neutral + Tenant Access Tokens & Refresh Persistence (2025-09-20)
  - Introduced persisted refresh tokens (`refresh_tokens` table with hashed SHA256 token_hash, jsonb metadata, FK users, indexes on (user, created_at) and unique token_hash) and structured auth responses for `/api/auth/login` and `/api/auth/magic/consume`: `{ user, memberships, access, refresh, tenantToken? }`. Added `RefreshTokenService.IssueNeutralAsync` and `JwtTokenService.IssueTenantToken`. Single-membership users automatically receive a tenant-scoped token (`tenantToken.access`), while multi-membership selection is explicit via `?tenant=<slug|id>`; conflicting `?tenant=auto` when >1 membership returns 409. Magic consume now provisions a personal tenant & membership for new users, then returns the same structured token set. Legacy compatibility retained with `?includeLegacy=true` returning `{ user, memberships }` only.
  - Tests: `LoginJwtNeutralTests` (neutral + refresh + auto tenant), `LoginTenantSelectionTests` (multi-tenant selection & conflict), `MagicConsumeJwtTests` (structured & legacy shapes) all passing. Migration `20250920144932_s6_01_auth_refresh_tokens` applied successfully.
  - Rationale: Establishes secure hashed refresh storage and ergonomic tenant context while preserving backward compatibility; foundation for rotation, revocation, and secure cookie delivery in upcoming stories.
  - Follow-ups: Story 2a test token factory; Story 3 refresh rotation & reuse detection; add revocation strategy (token_version or hash invalidation); secure httpOnly cookie + HTTPS (Stories 4/5a); telemetry counters; negative tests for expired/consumed tokens.
  - Auth/JWT — Story 2a Test Token Factory Helper (2025-09-20)
    - Added gated internal test helper endpoint `POST /api/test/mint-tenant-token` (maps only when `AUTH__TEST_HELPERS_ENABLED=true` AND not Production) to mint a neutral access + refresh token (and optional tenant-scoped token) for an arbitrary email, auto-provisioning personal tenant/membership when absent. Supports optional `{ tenant: <slugOrId>, autoTenant: bool }` selection semantics mirroring login auto-tenant issuance.
    - Introduced `TestAuthClient` in `apps/api.tests` encapsulating helper usage; new tests `TestTokenFactoryTests` cover single-membership auto issuance, multi-membership explicit selection (partial slug mismatch → no tenant token), and disabled flag returns 404 via derived factory override (ensuring production safety). WebAppFactory now injects test helper config via in-memory configuration (no reliance on process-wide environment mutation for determinism).
    - Rationale: Eliminates multi-step auth flow boilerplate in non-auth-focused integration tests (reduces flakiness/time) while preserving production surface area (endpoint absent in Production by configuration gate). Provides scaffold for future direct issuance helpers (e.g., select-tenant, refresh) if needed.
    - Follow-ups: Replace ad-hoc auth setup in existing integration tests incrementally with `TestAuthClient` where flows aren’t under test; proceed to Story 3 (tenant selection endpoint) and Story 6 (refresh rotation) before cookie delivery (Stories 4/5a).
    - Auth/JWT — Story 3 Tenant Selection & Refresh Rotation (2025-09-20)
      - Added `POST /api/auth/select-tenant` converting a neutral refresh token + tenant identifier (slug or id) into a tenant-scoped access token while also rotating the neutral refresh token (old revoked, new issued). Response shape aligns with login: `{ user, memberships, access, refresh, tenantToken }` with `tenantToken.access` holding tenant claims (`tenant_id`, `tenant_slug`, roles flags). Membership validation returns 403 on non-membership; invalid/expired/revoked refresh returns 401. Implementation revokes old refresh before issuing new to enforce single active chain. Fixed hashing mismatch (endpoint initially used hex hashing; storage uses Base64(SHA256))—tests caught 401s, leading to alignment with `RefreshTokenService` hashing (Base64 SHA256). Added integration tests: success rotation (old unusable), invalid token 401, forbidden tenant 403, expired refresh 401, revoked reuse 401. Sets foundation for general refresh endpoint (Story 6) and upcoming secure cookie delivery.
- Web — Flags-only cleanup (2025-09-20)
  - Removed deprecated `TenantAwareTopBar` stub and transitional legacy fallback test suite. `roles.ts` no longer exports `LegacyRole` or performs any legacy name fallback/expansion (only canonical flags via array, numeric bitmask, or numeric string; comma-separated canonical flags accepted temporarily). Added an ESLint `no-restricted-properties` rule preventing accidental use of `membership.role`. Frontend now depends solely on explicit flags for gating and labeling.
  - Follow-up: remove comma-separated string tolerance in a later hardening pass once telemetry confirms absence.

- IAM — Legacy invite `role` write path deprecated (Story 4 refLeg-04) (2025-09-19)
  - The invite creation endpoint `POST /api/tenants/{tenantId}/invites` now rejects any request providing the legacy single `role` field. Clients MUST supply granular roles via either `roles: ["TenantAdmin", ...]` or `rolesValue: int` (bitmask of flags). A BadRequest is returned with `{ code: "LEGACY_ROLE_DEPRECATED" }` when `role` is present. This locks in the flags-first contract ahead of physically dropping the legacy `role` column.
  - Legacy `Membership.Role` column and member role change endpoint still accept the legacy role name during the transitional window; convergence + seeds ensure `Roles` bitmask stays authoritative. A follow-up story will extend the deprecation to the member role change endpoint and then remove the legacy column + mapping.
  - Response payload for invites no longer echoes a legacy `role`; it returns `{ email, roles: "FlagsString", rolesValue, expiresAt }` (string representation of flags for readability + numeric for machine use).
  - Added regression tests (`LegacyRoleWritePathDeprecationTests`) asserting: (1) invite with legacy `role` is rejected with `LEGACY_ROLE_DEPRECATED`; (2) member role change with legacy `role` is CURRENTLY accepted (documenting transitional behavior) so we can intentionally flip expectation in a future commit without silent breakage.
  - Email template for invites now lists the composite roles flags (e.g., `TenantAdmin, Creator`) instead of a single legacy role name.

- Auth/Data — Role change preserves flags bitmask (2025-09-19)
  - The member role mutation endpoint (`PUT /api/tenants/{tenantId}/members/{userId}`) previously recreated the `Membership` record without copying the `Roles` flags, risking `roles=0` after a legacy `MembershipRole` change. All replacement paths now assign `Roles = DeriveFlagsFromLegacy(newRole)`, keeping the bitmask authoritative. Test seeding (`WebAppFactory`) updated to include full flags for the default owner membership; an integration test asserts role change Owner→Editor yields `Creator|Learner` flags (non-zero). Positions us to optionally add a DB constraint (`roles <> 0`) and ultimately remove legacy `Role` column.

- Auth/Data — Backfill zero roles memberships to full flags (2025-09-19)
  - Data-only migration `s5_02_membership_roles_backfill_zero_to_all` updates any lingering `app.memberships.roles = 0` rows to `15` (TenantAdmin|Approver|Creator|Learner) established during the legacy→flags transition. Idempotent (`roles=0` predicate) and non-reversible (Down no-op) to prevent reintroducing invalid state. Rationale: guarantee all memberships have a non-zero bitmask before disabling the temporary web legacy fallback and proceeding to drop the legacy `role` column.

- Auth/API — Auth endpoints now emit numeric roles bitmask (2025-09-19)
  - `/api/auth/login` and magic token consume (signup+auto-login path) now serialize membership roles flags as an integer (`roles = (int)m.Roles`) instead of the enum flags string. This guarantees the frontend roles helper (which decodes numeric bitmasks or arrays) receives a canonical machine-friendly representation, eliminating reliance on the temporary legacy role fallback. Tests added to assert presence and numeric type.

- Nav — Admin gating tightening (2025-09-18)
  - UI Admin menu now requires explicit presence of `TenantAdmin` in the selected membership’s roles flags (`isAdmin && roles.includes('TenantAdmin')`). Composite non-admin flag sets (e.g., Approver+Creator+Learner — bitmask 14) no longer qualify even if upstream boolean derivation is broadened. Regression test added to lock behavior. Rationale: eliminate privilege inflation risk during transitional legacy fallback period.

- Auth/Web — Numeric roles bitmask support (2025-09-18)
  - `membership.roles` may now arrive as an integer bitmask (or numeric string) matching API `[Flags] Roles` enum. Web helper `getFlagRoles` interprets numeric/ string values directly (`1` => TenantAdmin, `15` => all). A value of `0` yields empty roles without invoking legacy fallback (explicit empty). Prevents admin users represented solely by a numeric bitmask from appearing as Learner-only.

- Auth / Web — Removal of legacy MembershipRole fallback; flags-only authorization (2025-09-18)
  - Fully removed UI and helper fallbacks that previously considered the legacy `role` string (`Owner`/`Admin`/`Editor`/`Viewer`). Authorization and gating now rely solely on the `roles[]` flags bitmask (`TenantAdmin`, `Approver`, `Creator`, `Learner`).
  - Deleted legacy expansion logic (`deriveFlagsFromLegacy`, `PREFER_LEGACY_FOR_ADMIN`, single-tenant safety) from `apps/web/src/lib/roles.ts` and simplified `computeBooleansForTenant` to interpret only provided flags.
  - Simplified server/page guards (`roleGuard.ts`) to remove dual-mode branching; TopBar/Admin pages (Members, Invites, Audits, Notifications, Settings) now gate exclusively via `{ isAdmin }` derived from flags.
  - Updated tests to supply explicit `roles: ['TenantAdmin']` where admin access is expected; removed assertions relying on legacy Owner/Admin acceptance. All web tests green post-migration.
  - Revert point commit retained for emergency rollback (`REVERT POINT: pre removal of legacy role fallback`).
  - Rationale: Eliminate inconsistent gating and hidden privilege retention caused by OR-ing legacy and flag sources of truth; reduce cognitive load and future maintenance surface ahead of 1.0.

- Auth — API RoleAuthorization prefers Roles flags (2025-09-18)
  - Updated the authorization handler to treat Roles flags as the source of truth and only fall back to the legacy MembershipRole when Roles == None. This fixes a field issue where a tenant originator could remain effectively admin after demotion because legacy role and flags were previously OR-ed together.
  - Outcome: Admin-only endpoints now deny access appropriately after roles demotion; UI already layers additional safety (single-tenant safeguard + legacy-aligned TopBar suppress). API tests PASS (180/180) post-change.

- Nav — Single-tenant admin gating safeguard (2025-09-18)
  - To address a field report where a non-admin user with exactly one tenant membership saw the Admin menu, the shared roles helper now includes a single-tenant safety: when there is exactly one membership and its legacy role is non-admin, the derived TenantAdmin flag is suppressed for UI gating. This prevents accidental elevation when backend flags are inconsistent.
  - An optional env switch `NEXT_PUBLIC_PREFER_LEGACY_ROLES=true` further prefers the legacy role over flags when they conflict, adding a belt-and-suspenders protection during the transition. A dev-only console warning surfaces mismatches for quick diagnosis.
  - Tests updated: the flags-based admin visibility test now uses a multi-tenant session to validate the intended behavior while respecting the single-tenant safeguard; full web suite PASS.

- User Profile — Avatar pipeline simplification & absolute URLs (2025-09-17)
- Web — Org Settings (Tenant Settings) scaffold (2025-09-18)
  - Page `/studio/admin/settings` now renders a server-gated (TenantAdmin) UI:
    - Branding logo upload client (`TenantLogoUpload`) wired to `/api-proxy/tenants/logo` with validation and cache-busted preview.
    - Organization details form (`TenantSettingsForm`) for display name, contact (email/website), and social links; submits minimal merge patch to `/api-proxy/tenants/settings`.
  - Server selects effective tenant via membership match (slug/id) and gates using `computeBooleansForTenant` shared helper.
  - Tests added for the form; overall web suite remains green using Node 20.
  - Quick win (2025-09-18): Added Remove Logo action to `TenantLogoUpload` calling `DELETE /api-proxy/tenants/logo`, including local-blob clear without network, progress state, and accessible status messages. Added unit tests for POST upload, DELETE remove, and local clear.
  - Tests: Expanded `/studio/admin/settings/page.test.tsx` to include legacy role acceptance (Owner, case-insensitive) and tenantId→slug resolution in `session.tenant`.

  - Upload endpoint now preserves the original image format (PNG/JPEG/WebP) rather than forcing WebP. Minimal transforms remain (AutoOrient; optional center-crop to near-square; optional downscale to max 512px). When mutated, re-encodes using the original format encoder with sane quality defaults; otherwise passes through original bytes.
  - Storage keys include the correct extension (e.g., `users/{id}/avatar.png|jpg|webp`) and the response metadata includes `{ url, key, mime, width, height }` with the URL now absolute (`scheme://host/...`) to avoid dev-server relative path confusion.

- Web — Org Settings parity with Profile (Guardrails + Bio) (2025-09-18)
  - Added tenant‑level Guardrails & Preferences and Bio sections to `/studio/admin/settings`.
  - New components: `TenantGuardrailsForm` (denominations, alignment, favorites, notes, lesson format) and `TenantBioEditor` (Markdown with preview). Both submit minimal merge patches to `/api-proxy/tenants/settings`.
  - Server page now loads denomination presets from `/api-proxy/metadata/denominations` and initial values from `GET /api/tenants/settings`.
  - Tests added for both components; full web suite PASS.

- User Profile — Avatar upload UX: Clear confirmation (2025-09-18)
  - Added a "Clear" action to `AvatarUpload` that asks for confirmation via the shared `ConfirmDialog` and clears a just-selected local image without making a network call. This mirrors the new Tenant logo removal UX where applicable, while server-side delete remains deferred for avatars.
  - Accessibility: inline status message uses `role="status"`; errors use `role="alert"`. Object URLs are revoked to prevent leaks.
  - Tests: Updated `AvatarUpload.test.tsx` to cover confirmation flow (scoped dialog query) and ensure no network/event fires when clearing a local selection. Full web suite PASS.

- Auth/JWT — RDH Story 3 Deprecation Middleware (2025-09-22)
  - Added early-pipeline `DevHeadersDeprecationMiddleware` that rejects any request containing deprecated development headers (`x-dev-user`, `x-tenant`) when `AUTH__ALLOW_DEV_HEADERS` is false (now forced default) with 401 `{ code: "dev_headers_deprecated" }` and increments metric counter `auth.dev_headers.deprecated_requests` (tag: path). Negative-path regression tests updated to assert structured code. Provides a soft-block observability phase before physical removal of the dev header auth handler & composite scheme (Story 4). No downstream auth handlers executed for rejected requests, ensuring zero accidental reliance on legacy path.

## What’s new

<!-- NOTE: Above 'What’s new' section consolidated & deduplicated 2025-09-22 to remove repeated blocks inadvertently added during Story 3 edits. -->

## What’s new

- IAM — Final legacy role cleanup & test alignment (2025-09-20)
  - Auth/JWT — Dev Header Decommission Sprint (Phase A) In Progress (2025-09-22)
    - Added superadmin claim support to test-only mint endpoint (`POST /api/test/mint-tenant-token`) via `SuperAdmin` flag injecting `superadmin=true` into issued access tokens (neutral or tenant). Updated `TestAuthClient` and `AuthTestClient` helpers; migrated notifications production endpoints integration tests off development headers to pure Bearer JWT authentication (4/4 tests passing). Residual explicit `Dev` scheme references removed from those paths. Next phases will migrate remaining tests, add a guard prohibiting `x-dev-user`/`x-tenant` usage, and finally remove the Dev header authentication handler + composite scheme.
    - RDH Story 2 Phase A: Migrated `UserProfileEndpointsTests` off legacy `AuthTestClient.UseTenantAsync` mint helper to real password + `/api/auth/login` + `/api/auth/select-tenant` flow (password seeded via `IPasswordHasher`). Guard test now reports reduced remaining usages; next target `UserAvatarEndpointsTests`.
    - RDH Story 2 Phase A: Migrated invites test suite (`InvitesEndpointsTests`, `InvitesAcceptTests`, `InvitesRolesFlagsTests`, `LegacyRoleWritePathDeprecationTests`) off legacy mint helper to real auth flow; fixed DELETE invite endpoint to fall back to `ClaimTypes.NameIdentifier` when `sub` is absent (parity with listing endpoint) resolving 400 revoke regression under migrated tokens.
  - RDH Story 2 Phase D: Test harness audit complete; added guard test `DevHeadersUsageGuardTests` scanning only `apps/api.tests` (excluding intentional negative-path regression suites) to fail CI on any reintroduction of `x-dev-user` / `x-tenant`. Provides fail-fast safety preceding deprecation middleware (Story 3) and handler removal (Story 4).
  - Auth/JWT — RDH Sprint Plan Initialized (2025-09-22)
    - Added `devInfo/jwtRefactor/rdhSprintPlan.md` outlining the Dev Header Decommission sprint (RDH). Objective: fully remove development header authentication (`x-dev-user`, `x-tenant`) and associated composite scheme in favor of a single JWT-based auth path across all environments and tests. Plan includes phased test migration, deprecation middleware, physical removal, regression guard, and rollback tag strategy. See plan file for detailed stories, risks, and acceptance criteria.
  - IAM — Roles assignment endpoint duplication fix (2025-09-21)
    - Simplified `/api/tenants/{tenantId}/memberships/{userId}/roles` handler from three overlapping replacement/audit branches to a single provider-aware path. Previous logic executed twice under EF InMemory (no explicit transactions): first non-transaction branch then fallback branch that began a transaction and executed `set_config`, a relational-only call, causing test-only HTTP 500s. New implementation wraps replacement + audit in a transaction only when `SupportsExplicitTransactions()`; otherwise performs a single remove/add + audit. Guards raw SQL with try/catch for defensive safety. Result: four failing integration tests now pass; full suite green (223 passed / 1 skipped).
  - Auth/JWT — Composite Dev+Bearer Policy Scheme (Development) (2025-09-20)
    - Added a Development-only authentication policy scheme "BearerOrDev" that automatically selects the custom Dev header scheme when an `x-dev-user` header is present, otherwise falling back to standard JWT Bearer. This replaces the need to enumerate `AuthenticationSchemes="Dev,Bearer"` on each endpoint group and resolved widespread 401 Unauthorized failures in tests that relied on dev headers after introducing stricter JWT subject (GUID) validation. Implementation: policy scheme with `ForwardDefaultSelector` + `PostConfigure` override of `AuthenticationOptions.DefaultAuthenticateScheme` / `DefaultChallengeScheme` in Development. Result: API test suite returned to green (211 passed, 1 skipped) without per-endpoint duplication. Auth smoke test updated to issue GUID `sub` to satisfy `OnTokenValidated` GUID requirement. Rationale: Centralizes dev ergonomics while keeping Production behavior untouched (Bearer only) and reduces future auth drift risk.
  - Auth/JWT — Story 5a Local HTTPS & Secure Refresh Cookie Validation (2025-09-20)
    - Added Makefile target `api-https` leveraging trusted dev certificate (`dotnet dev-certs https --trust`) to run API over HTTPS locally for true Secure cookie validation. Refresh cookie (`rt`) issuance logic updated (login, magic consume, select-tenant) to set `Secure = http.Request.IsHttps` (removed previous environment heuristic); over HTTP in Development cookie is not Secure, over HTTPS it is. New test `RefreshCookieHttpsTests` asserts absence of Secure over HTTP and simulated presence with `X-Forwarded-Proto: https`. Simplifies semantics ahead of general refresh endpoint (Story 6) and avoids false Secure flag expectations during local dev without TLS.
    - Follow-up (2025-09-20): Consolidated refresh cookie issuance into `IssueRefreshCookie` helper; added `trust-dev-certs` Makefile target; HTTPS test now uses HTTPS base address for deterministic `Request.IsHttps`.
  - Auth/JWT — Story 5 Access Token Revocation (2025-09-20)
    - Added `TokenVersion` (int, default 0) column on users; every issued access token carries claim `v` (stringified int). `OnTokenValidated` event now queries the user’s current `TokenVersion`; if token claim `v` < stored value, authentication fails with `token_version_mismatch`. Password change endpoint increments `TokenVersion` atomically after successful verification, invalidating all previously issued access tokens while leaving refresh tokens to obtain a new access on next refresh flow. This avoids server-side token blacklists and enables instant revocation on credential compromise. Added claim mapping resilience: validation falls back to `ClaimTypes.NameIdentifier` when raw `sub` claim is mapped away; change-password endpoint falls back to `ClaimTypes.Email`. Integration test `AccessTokenVersionTests` confirms old token invalidation after password change. Future: admin-initiated version bump endpoint; potential caching of per-user version for high RPS.
  - Auth — JWT Baseline (Story 1) introduced (2025-09-20)
  - Auth/JWT — Story 5 (Validation Middleware & Principal) marked complete (2025-09-20) — composite scheme + GUID sub + token version checks (see section below for TokenVersion revocation details).
  - Auth/JWT — Story 5b HTTPS Secure Refresh Cookie E2E Harness complete (2025-09-20) — deterministic Secure attribute validation under real TLS.
    - Auth/JWT — Story 6 Refresh Endpoint (COMPLETED 2025-09-21)
      - Implemented `/api/auth/refresh` (cookie-first, transitional JSON body behind `AUTH__REFRESH_JSON_GRACE_ENABLED`) rotating neutral refresh tokens (revocation + issuance) and returning new neutral access token plus optional tenant-scoped token when `?tenant=` provided and membership matches. Structured 401 codes: `refresh_invalid`, `refresh_reuse`, `refresh_expired`; 400 `missing_refresh` and `refresh_body_disallowed`. Deprecation headers (`Deprecation: true`, `Sunset: <date>`) emitted when body token used and `AUTH__REFRESH_DEPRECATION_DATE` configured. In-memory EF provider incompatibility with transactions resolved by removing explicit transaction wrapping for rotation (sequential revoke/issue is acceptable given low contention). Tests `RefreshEndpointTests` now all passing (7/7) including rotation, tenant issuance, reuse detection, revoked reuse, missing token 400, expired, and grace body path. SnapshotArchitecture, LivingChecklist, and storyLog updated.
      - Centralized hashing: Added `RefreshTokenHashing` helper (Base64(SHA256(token UTF8))) consumed by `RefreshTokenService` and all endpoints (login, magic consume, select-tenant, refresh, logout) eliminating duplicated inline hashing code blocks and preventing future drift.
    - Auth/JWT — Story 7 Logout & Global Revocation (COMPLETED 2025-09-21)
      - Implemented endpoints:
        - `POST /api/auth/logout` — Revokes a single neutral refresh token (from cookie `rt` or JSON body `refreshToken` during grace). If a JSON body is present but missing `refreshToken`, returns 400 `{ code: "missing_refresh" }` (tests enforce). Clears cookie when present. Idempotent 204 otherwise (including already revoked/missing token scenarios). Structured log: `auth.logout.single user=<id> tokenFound=<bool>`.
        - `POST /api/auth/logout/all` — Bulk revokes all active neutral refresh tokens for the user and increments `User.TokenVersion` (record detach + replacement) to invalidate existing access tokens immediately. Always 204; logs `auth.logout.all` (or `user_missing`). Clears cookie if present.
      - Error codes: `missing_refresh` (400) on single logout when body supplied without token; reuse/invalid continue to surface via refresh endpoint (`refresh_reuse|refresh_invalid`).
      - Tests (`LogoutTests`) cover: single logout reuse detection, global logout access token invalidation, missing token 400, idempotent second logout. All passing.
      - Rationale: Provides explicit single‑device logout and full-session revocation using lightweight TokenVersion bump—no per access token storage required.
      - Deferred follow-ups: session listing & selective device logout, admin forced logout, deprecation headers for body path, removal of plaintext `refresh.token` post grace.
    - Auth/JWT — Story 8 Silent Refresh & Plaintext Refresh Token Suppression (COMPLETED 2025-09-21)
      - Backend flag `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` introduced. When false (default), API auth responses (login, magic consume, select-tenant, refresh) omit the plaintext `refresh.token` field while still returning its hash-derived metadata (id, created, expires) if present; the httpOnly cookie `rt` becomes the exclusive delivery channel. When true (temporary transitional aid), plaintext continues to be emitted (mirrors prior shape) enabling gradual frontend rollout.
      - Refresh endpoint (`/api/auth/refresh`) further gates plaintext emission: it only returns a plaintext refresh token when BOTH the expose flag is true AND either the JSON grace body path is used (`AUTH__REFRESH_JSON_GRACE_ENABLED`) or the secure cookie feature is disabled. This guarantees we never redundantly emit a plaintext token during the steady-state cookie-first flow once clients have migrated.
      - Unified object shaping: Replaced earlier anonymous conditional inline constructions with explicit local object composition (`var response = new { ... }`) followed by conditional mutation to append `refresh = new { token = <plaintext>, ... }` only when permitted, avoiding compilation issues from divergent anonymous types and simplifying future field additions.
      - Frontend: Implemented silent refresh loop in `apps/web/src/lib/authClient.ts` invoking real backend `/api/auth/refresh` (removing placeholder `_auth/refresh-neutral`). Schedules a refresh 60s before access token expiry (configurable skew constant), single-flights concurrent refresh attempts, and provides `startAutoRefresh`, `stopAutoRefresh`, and `forceRefresh` helpers. Adds 401 retry-once logic to `withAuthFetch` to transparently recover from narrowly expired access tokens (race between request send and expiry) without duplicating user actions.
      - Tests: Added `RefreshPlaintextExposedFlagTests` integration suite validating presence/absence of `refresh.token` across login & refresh under both flag states. Extended frontend unit tests (`authClient.test.ts`) to cover retry-once logic and scheduling behavior (mocked timers + forced expiry). All existing auth suites remain green.
      - Security rationale: Eliminates routine exposure of long-lived refresh secrets to JavaScript, reducing XSS blast radius. Cookie (`HttpOnly; SameSite=Lax; Secure over HTTPS`) confines token to browser credential channel; access token remains short-lived and in-memory only. Transitional flag permits incremental safe rollout and rollback lever if clients unexpectedly depend on plaintext field.
      - Operational considerations: Metrics/observability hooks for refresh rotations and suppressed/plaintext emission counts deferred to Observability hardening story. CSRF review (double submit or SameSite strategy) flagged for later if `SameSite=None` becomes necessary (e.g., cross-site embedding requirements).
      - Follow-ups:
        - Remove `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` after deprecation window once all clients verified.
        - Potential: Add `/api/auth/session` enumerator to expose active refresh token metadata (no plaintext) for future session management UI.
        - Observability counters: `auth.refresh.rotation`, `auth.refresh.reuse_denied`, `auth.refresh.plaintext_emitted` (temporary), `auth.refresh.plaintext_suppressed`.
        - Evaluate CSRF protection adjustments if moving to `SameSite=None` for any cross-origin flows.
      - Auth/JWT — Story 9 Auth Observability Metrics (2025-09-22)
        - Added first-wave OpenTelemetry metrics (Meter `Appostolic.Auth`) for login, refresh, token issuance, rotation, reuse/expired denial, plaintext emission/suppression (temporary), logout, and latency histograms. Chosen stable dot notation (`auth.login.success`, `auth.refresh.failure`, etc.) instead of earlier underscore placeholders documented in sprint plan. Endpoint `V1.cs` instrumented for /auth/login and /auth/refresh success/failure (bounded reason taxonomies) plus logout endpoints. Added rate‑limited counter increment path gated by existing refresh rate limiter flag. New test `AuthMetricsTests` asserts presence of new counters and histograms. Plaintext emission/suppression counters marked TEMP and will be removed post flag retirement. Documentation (this file, sprint plan, LivingChecklist, storyLog) updated; dashboards & span attribute enrichment deferred.
    - Added initial JWT access token infrastructure with `AuthJwtOptions` (issuer, audience, base64 signing key, TTLs, skew) and `JwtTokenService` (HS256). Authentication now registers `JwtBearerDefaults.AuthenticationScheme` plus retains the existing dev header scheme (conditional) for development ergonomics. A protected smoke endpoint `/auth-smoke/ping` validates end-to-end token issuance & validation, covered by new integration test `AuthJwtSmokeTests` issuing a neutral token. Swagger now includes a Bearer security definition alongside the DevHeaders scheme. Ephemeral signing key generation occurs only in Development when no key is configured; Production requires explicit `Auth:Jwt:SigningKey` (base64) and will fail fast if absent. Post-configure pattern applies validation parameters (issuer, audience, key, clock skew) without prematurely building a service provider. Subsequent stories will layer tenant claims, refresh tokens (secure httpOnly cookie), rotation & revocation via token_version, secure cookie flags (SameSite=Lax/Strict + secure), local HTTPS (Story 5a), nginx reverse proxy (Story 9a), and observability (counters for issuance/validation/failures).
  - After dropping legacy `role` columns and enforcing bitmask constraints, the invite creation endpoint now surfaces the generic `{ code: "NO_FLAGS" }` validation error when a request supplies only the deprecated single `role` field (and no `roles` / `rolesValue` flags). The earlier transition-specific `LEGACY_ROLE_DEPRECATED` error is now reserved only for the still-deprecated member single-role change endpoint (documented by its regression test) until that path is removed in a later story. Updated regression test `Invite_with_legacy_role_only_is_rejected_with_NO_FLAGS` reflects this invariant; full API test suite passes (193/193) post-clean rebuild.
  - Rationale: With the legacy column physically removed, treating a legacy-only payload as simply “missing flags” simplifies client handling and avoids implying a reversible transitional path.
  - Follow-up: remove the member single-role change legacy acceptance path and consolidate on a single error (`NO_FLAGS`) across all IAM write paths; publish rollback script/tag (`roles-removal-complete`).
- Auth/JWT — Story 2 Neutral + Tenant Access Tokens & Refresh Persistence (2025-09-20)
  - Introduced persisted refresh tokens (`refresh_tokens` table with hashed SHA256 token_hash, jsonb metadata, FK users, indexes on (user, created_at) and unique token_hash) and structured auth responses for `/api/auth/login` and `/api/auth/magic/consume`: `{ user, memberships, access, refresh, tenantToken? }`. Added `RefreshTokenService.IssueNeutralAsync` and `JwtTokenService.IssueTenantToken`. Single-membership users automatically receive a tenant-scoped token (`tenantToken.access`), while multi-membership selection is explicit via `?tenant=<slug|id>`; conflicting `?tenant=auto` when >1 membership returns 409. Magic consume now provisions a personal tenant & membership for new users, then returns the same structured token set. Legacy compatibility retained with `?includeLegacy=true` returning `{ user, memberships }` only.
  - Tests: `LoginJwtNeutralTests` (neutral + refresh + auto tenant), `LoginTenantSelectionTests` (multi-tenant selection & conflict), `MagicConsumeJwtTests` (structured & legacy shapes) all passing. Migration `20250920144932_s6_01_auth_refresh_tokens` applied successfully.
  - Rationale: Establishes secure hashed refresh storage and ergonomic tenant context while preserving backward compatibility; foundation for rotation, revocation, and secure cookie delivery in upcoming stories.
  - Follow-ups: Story 2a test token factory; Story 3 refresh rotation & reuse detection; add revocation strategy (token_version or hash invalidation); secure httpOnly cookie + HTTPS (Stories 4/5a); telemetry counters; negative tests for expired/consumed tokens.
  - Auth/JWT — Story 2a Test Token Factory Helper (2025-09-20)
    - Added gated internal test helper endpoint `POST /api/test/mint-tenant-token` (maps only when `AUTH__TEST_HELPERS_ENABLED=true` AND not Production) to mint a neutral access + refresh token (and optional tenant-scoped token) for an arbitrary email, auto-provisioning personal tenant/membership when absent. Supports optional `{ tenant: <slugOrId>, autoTenant: bool }` selection semantics mirroring login auto-tenant issuance.
    - Introduced `TestAuthClient` in `apps/api.tests` encapsulating helper usage; new tests `TestTokenFactoryTests` cover single-membership auto issuance, multi-membership explicit selection (partial slug mismatch → no tenant token), and disabled flag returns 404 via derived factory override (ensuring production safety). WebAppFactory now injects test helper config via in-memory configuration (no reliance on process-wide environment mutation for determinism).
    - Rationale: Eliminates multi-step auth flow boilerplate in non-auth-focused integration tests (reduces flakiness/time) while preserving production surface area (endpoint absent in Production by configuration gate). Provides scaffold for future direct issuance helpers (e.g., select-tenant, refresh) if needed.
    - Follow-ups: Replace ad-hoc auth setup in existing integration tests incrementally with `TestAuthClient` where flows aren’t under test; proceed to Story 3 (tenant selection endpoint) and Story 6 (refresh rotation) before cookie delivery (Stories 4/5a).
    - Auth/JWT — Story 3 Tenant Selection & Refresh Rotation (2025-09-20)
      - Added `POST /api/auth/select-tenant` converting a neutral refresh token + tenant identifier (slug or id) into a tenant-scoped access token while also rotating the neutral refresh token (old revoked, new issued). Response shape aligns with login: `{ user, memberships, access, refresh, tenantToken }` with `tenantToken.access` holding tenant claims (`tenant_id`, `tenant_slug`, roles flags). Membership validation returns 403 on non-membership; invalid/expired/revoked refresh returns 401. Implementation revokes old refresh before issuing new to enforce single active chain. Fixed hashing mismatch (endpoint initially used hex hashing; storage uses Base64(SHA256))—tests caught 401s, leading to alignment with `RefreshTokenService` hashing (Base64 SHA256). Added integration tests: success rotation (old unusable), invalid token 401, forbidden tenant 403, expired refresh 401, revoked reuse 401. Sets foundation for general refresh endpoint (Story 6) and upcoming secure cookie delivery.
- Web — Flags-only cleanup (2025-09-20)
  - Removed deprecated `TenantAwareTopBar` stub and transitional legacy fallback test suite. `roles.ts` no longer exports `LegacyRole` or performs any legacy name fallback/expansion (only canonical flags via array, numeric bitmask, or numeric string; comma-separated canonical flags accepted temporarily). Added an ESLint `no-restricted-properties` rule preventing accidental use of `membership.role`. Frontend now depends solely on explicit flags for gating and labeling.
  - Follow-up: remove comma-separated string tolerance in a later hardening pass once telemetry confirms absence.

- IAM — Legacy invite `role` write path deprecated (Story 4 refLeg-04) (2025-09-19)
  - The invite creation endpoint `POST /api/tenants/{tenantId}/invites` now rejects any request providing the legacy single `role` field. Clients MUST supply granular roles via either `roles: ["TenantAdmin", ...]` or `rolesValue: int` (bitmask of flags). A BadRequest is returned with `{ code: "LEGACY_ROLE_DEPRECATED" }` when `role` is present. This locks in the flags-first contract ahead of physically dropping the legacy `role` column.
  - Legacy `Membership.Role` column and member role change endpoint still accept the legacy role name during the transitional window; convergence + seeds ensure `Roles` bitmask stays authoritative. A follow-up story will extend the deprecation to the member role change endpoint and then remove the legacy column + mapping.
  - Response payload for invites no longer echoes a legacy `role`; it returns `{ email, roles: "FlagsString", rolesValue, expiresAt }` (string representation of flags for readability + numeric for machine use).
  - Added regression tests (`LegacyRoleWritePathDeprecationTests`) asserting: (1) invite with legacy `role` is rejected with `LEGACY_ROLE_DEPRECATED`; (2) member role change with legacy `role` is CURRENTLY accepted (documenting transitional behavior) so we can intentionally flip expectation in a future commit without silent breakage.
  - Email template for invites now lists the composite roles flags (e.g., `TenantAdmin, Creator`) instead of a single legacy role name.

- Auth/Data — Role change preserves flags bitmask (2025-09-19)
  - The member role mutation endpoint (`PUT /api/tenants/{tenantId}/members/{userId}`) previously recreated the `Membership` record without copying the `Roles` flags, risking `roles=0` after a legacy `MembershipRole` change. All replacement paths now assign `Roles = DeriveFlagsFromLegacy(newRole)`, keeping the bitmask authoritative. Test seeding (`WebAppFactory`) updated to include full flags for the default owner membership; an integration test asserts role change Owner→Editor yields `Creator|Learner` flags (non-zero). Positions us to optionally add a DB constraint (`roles <> 0`) and ultimately remove legacy `Role` column.

- Auth/Data — Backfill zero roles memberships to full flags (2025-09-19)
  - Data-only migration `s5_02_membership_roles_backfill_zero_to_all` updates any lingering `app.memberships.roles = 0` rows to `15` (TenantAdmin|Approver|Creator|Learner) established during the legacy→flags transition. Idempotent (`roles=0` predicate) and non-reversible (Down no-op) to prevent reintroducing invalid state. Rationale: guarantee all memberships have a non-zero bitmask before disabling the temporary web legacy fallback and proceeding to drop the legacy `role` column.

- Auth/API — Auth endpoints now emit numeric roles bitmask (2025-09-19)
  - `/api/auth/login` and magic token consume (signup+auto-login path) now serialize membership roles flags as an integer (`roles = (int)m.Roles`) instead of the enum flags string. This guarantees the frontend roles helper (which decodes numeric bitmasks or arrays) receives a canonical machine-friendly representation, eliminating reliance on the temporary legacy role fallback. Tests added to assert presence and numeric type.

- Nav — Admin gating tightening (2025-09-18)
  - UI Admin menu now requires explicit presence of `TenantAdmin` in the selected membership’s roles flags (`isAdmin && roles.includes('TenantAdmin')`). Composite non-admin flag sets (e.g., Approver+Creator+Learner — bitmask 14) no longer qualify even if upstream boolean derivation is broadened. Regression test added to lock behavior. Rationale: eliminate privilege inflation risk during transitional legacy fallback period.

- Auth/Web — Numeric roles bitmask support (2025-09-18)
  - `membership.roles` may now arrive as an integer bitmask (or numeric string) matching API `[Flags] Roles` enum. Web helper `getFlagRoles` interprets numeric/ string values directly (`1` => TenantAdmin, `15` => all). A value of `0` yields empty roles without invoking legacy fallback (explicit empty). Prevents admin users represented solely by a numeric bitmask from appearing as Learner-only.

- Auth / Web — Removal of legacy MembershipRole fallback; flags-only authorization (2025-09-18)
  - Fully removed UI and helper fallbacks that previously considered the legacy `role` string (`Owner`/`Admin`/`Editor`/`Viewer`). Authorization and gating now rely solely on the `roles[]` flags bitmask (`TenantAdmin`, `Approver`, `Creator`, `Learner`).
  - Deleted legacy expansion logic (`deriveFlagsFromLegacy`, `PREFER_LEGACY_FOR_ADMIN`, single-tenant safety) from `apps/web/src/lib/roles.ts` and simplified `computeBooleansForTenant` to interpret only provided flags.
  - Simplified server/page guards (`roleGuard.ts`) to remove dual-mode branching; TopBar/Admin pages (Members, Invites, Audits, Notifications, Settings) now gate exclusively via `{ isAdmin }` derived from flags.
  - Updated tests to supply explicit `roles: ['TenantAdmin']` where admin access is expected; removed assertions relying on legacy Owner/Admin acceptance. All web tests green post-migration.
  - Revert point commit retained for emergency rollback (`REVERT POINT: pre removal of legacy role fallback`).
  - Rationale: Eliminate inconsistent gating and hidden privilege retention caused by OR-ing legacy and flag sources of truth; reduce cognitive load and future maintenance surface ahead of 1.0.

- Auth — API RoleAuthorization prefers Roles flags (2025-09-18)
  - Updated the authorization handler to treat Roles flags as the source of truth and only fall back to the legacy MembershipRole when Roles == None. This fixes a field issue where a tenant originator could remain effectively admin after demotion because legacy role and flags were previously OR-ed together.
  - Outcome: Admin-only endpoints now deny access appropriately after roles demotion; UI already layers additional safety (single-tenant safeguard + legacy-aligned TopBar suppress). API tests PASS (180/180) post-change.

- Nav — Single-tenant admin gating safeguard (2025-09-18)
  - To address a field report where a non-admin user with exactly one tenant membership saw the Admin menu, the shared roles helper now includes a single-tenant safety: when there is exactly one membership and its legacy role is non-admin, the derived TenantAdmin flag is suppressed for UI gating. This prevents accidental elevation when backend flags are inconsistent.
  - An optional env switch `NEXT_PUBLIC_PREFER_LEGACY_ROLES=true` further prefers the legacy role over flags when they conflict, adding a belt-and-suspenders protection during the transition. A dev-only console warning surfaces mismatches for quick diagnosis.
  - Tests updated: the flags-based admin visibility test now uses a multi-tenant session to validate the intended behavior while respecting the single-tenant safeguard; full web suite PASS.

- User Profile — Avatar pipeline simplification & absolute URLs (2025-09-17)
- Web — Org Settings (Tenant Settings) scaffold (2025-09-18)
  - Page `/studio/admin/settings` now renders a server-gated (TenantAdmin) UI:
    - Branding logo upload client (`TenantLogoUpload`) wired to `/api-proxy/tenants/logo` with validation and cache-busted preview.
    - Organization details form (`TenantSettingsForm`) for display name, contact (email/website), and social links; submits minimal merge patch to `/api-proxy/tenants/settings`.
  - Server selects effective tenant via membership match (slug/id) and gates using `computeBooleansForTenant` shared helper.
  - Tests added for the form; overall web suite remains green using Node 20.
  - Quick win (2025-09-18): Added Remove Logo action to `TenantLogoUpload` calling `DELETE /api-proxy/tenants/logo`, including local-blob clear without network, progress state, and accessible status messages. Added unit tests for POST upload, DELETE remove, and local clear.
  - Tests: Expanded `/studio/admin/settings/page.test.tsx` to include legacy role acceptance (Owner, case-insensitive) and tenantId→slug resolution in `session.tenant`.

  - Upload endpoint now preserves the original image format (PNG/JPEG/WebP) rather than forcing WebP. Minimal transforms remain (AutoOrient; optional center-crop to near-square; optional downscale to max 512px). When mutated, re-encodes using the original format encoder with sane quality defaults; otherwise passes through original bytes.
  - Storage keys include the correct extension (e.g., `users/{id}/avatar.png|jpg|webp`) and the response metadata includes `{ url, key, mime, width, height }` with the URL now absolute (`scheme://host/...`) to avoid dev-server relative path confusion.

- Web — Org Settings parity with Profile (Guardrails + Bio) (2025-09-18)
  - Added tenant‑level Guardrails & Preferences and Bio sections to `/studio/admin/settings`.
  - New components: `TenantGuardrailsForm` (denominations, alignment, favorites, notes, lesson format) and `TenantBioEditor` (Markdown with preview). Both submit minimal merge patches to `/api-proxy/tenants/settings`.
  - Server page now loads denomination presets from `/api-proxy/metadata/denominations` and initial values from `GET /api/tenants/settings`.
  - Tests added for both components; full web suite PASS.

- User Profile — Avatar upload UX: Clear confirmation (2025-09-18)
  - Added a "Clear" action to `AvatarUpload` that asks for confirmation via the shared `ConfirmDialog` and clears a just-selected local image without making a network call. This mirrors the new Tenant logo removal UX where applicable, while server-side delete remains deferred for avatars.
  - Accessibility: inline status message uses `role="status"`; errors use `role="alert"`. Object URLs are revoked to prevent leaks.
  - Tests: Updated `AvatarUpload.test.tsx` to cover confirmation flow (scoped dialog query) and ensure no network/event fires when clearing a local selection. Full web suite PASS.

- Auth/JWT — RDH Story 3 Deprecation Middleware (2025-09-22)
  - Added early-pipeline `DevHeadersDeprecationMiddleware` that rejects any request containing deprecated development headers (`x-dev-user`, `x-tenant`) when `AUTH__ALLOW_DEV_HEADERS` is false (now forced default) with 401 `{ code: "dev_headers_deprecated" }` and increments metric counter `auth.dev_headers.deprecated_requests` (tag: path). Negative-path regression tests updated to assert structured code. Provides a soft-block observability phase before physical removal of the dev header auth handler & composite scheme (Story 4). No downstream auth handlers executed for rejected requests, ensuring zero accidental reliance on legacy path.

# Appostolic — Architecture Snapshot (2025-09-22)

This document describes the structure, runtime, and conventions of the Appostolic monorepo. It’s organized to group related topics together for easier navigation and future updates.

## What’s new

- IAM — Final legacy role cleanup & test alignment (2025-09-20)
  - Auth/JWT — Dev Header Decommission Sprint (Phase A) In Progress (2025-09-22)
    - Added superadmin claim support to test-only mint endpoint (`POST /api/test/mint-tenant-token`) via `SuperAdmin` flag injecting `superadmin=true` into issued access tokens (neutral or tenant). Updated `TestAuthClient` and `AuthTestClient` helpers; migrated notifications production endpoints integration tests off development headers to pure Bearer JWT authentication (4/4 tests passing). Residual explicit `Dev` scheme references removed from those paths. Next phases will migrate remaining tests, add a guard prohibiting `x-dev-user`/`x-tenant` usage, and finally remove the Dev header authentication handler + composite scheme.
    - RDH Story 2 Phase A: Migrated `UserProfileEndpointsTests` off legacy `AuthTestClient.UseTenantAsync` mint helper to real password + `/api/auth/login` + `/api/auth/select-tenant` flow (password seeded via `IPasswordHasher`). Guard test now reports reduced remaining usages; next target `UserAvatarEndpointsTests`.
    - RDH Story 2 Phase A: Migrated invites test suite (`InvitesEndpointsTests`, `InvitesAcceptTests`, `InvitesRolesFlagsTests`, `LegacyRoleWritePathDeprecationTests`) off legacy mint helper to real auth flow; fixed DELETE invite endpoint to fall back to `ClaimTypes.NameIdentifier` when `sub` is absent (parity with listing endpoint) resolving 400 revoke regression under migrated tokens.
  - RDH Story 2 Phase D: Test harness audit complete; added guard test `DevHeadersUsageGuardTests` scanning only `apps/api.tests` (excluding intentional negative-path regression suites) to fail CI on any reintroduction of `x-dev-user` / `x-tenant`. Provides fail-fast safety preceding deprecation middleware (Story 3) and handler removal (Story 4).
  - Auth/JWT — RDH Sprint Plan Initialized (2025-09-22)
    - Added `devInfo/jwtRefactor/rdhSprintPlan.md` outlining the Dev Header Decommission sprint (RDH). Objective: fully remove development header authentication (`x-dev-user`, `x-tenant`) and associated composite scheme in favor of a single JWT-based auth path across all environments and tests. Plan includes phased test migration, deprecation middleware, physical removal, regression guard, and rollback tag strategy. See plan file for detailed stories, risks, and acceptance criteria.
  - IAM — Roles assignment endpoint duplication fix (2025-09-21)
    - Simplified `/api/tenants/{tenantId}/memberships/{userId}/roles` handler from three overlapping replacement/audit branches to a single provider-aware path. Previous logic executed twice under EF InMemory (no explicit transactions): first non-transaction branch then fallback branch that began a transaction and executed `set_config`, a relational-only call, causing test-only HTTP 500s. New implementation wraps replacement + audit in a transaction only when `SupportsExplicitTransactions()`; otherwise performs a single remove/add + audit. Guards raw SQL with try/catch for defensive safety. Result: four failing integration tests now pass; full suite green (223 passed / 1 skipped).
  - Auth/JWT — Composite Dev+Bearer Policy Scheme (Development) (2025-09-20)
    - Added a Development-only authentication policy scheme "BearerOrDev" that automatically selects the custom Dev header scheme when an `x-dev-user` header is present, otherwise falling back to standard JWT Bearer. This replaces the need to enumerate `AuthenticationSchemes="Dev,Bearer"` on each endpoint group and resolved widespread 401 Unauthorized failures in tests that relied on dev headers after introducing stricter JWT subject (GUID) validation. Implementation: policy scheme with `ForwardDefaultSelector` + `PostConfigure` override of `AuthenticationOptions.DefaultAuthenticateScheme` / `DefaultChallengeScheme` in Development. Result: API test suite returned to green (211 passed, 1 skipped) without per-endpoint duplication. Auth smoke test updated to issue GUID `sub` to satisfy `OnTokenValidated` GUID requirement. Rationale: Centralizes dev ergonomics while keeping Production behavior untouched (Bearer only) and reduces future auth drift risk.
  - Auth/JWT — Story 5a Local HTTPS & Secure Refresh Cookie Validation (2025-09-20)
    - Added Makefile target `api-https` leveraging trusted dev certificate (`dotnet dev-certs https --trust`) to run API over HTTPS locally for true Secure cookie validation. Refresh cookie (`rt`) issuance logic updated (login, magic consume, select-tenant) to set `Secure = http.Request.IsHttps` (removed previous environment heuristic); over HTTP in Development cookie is not Secure, over HTTPS it is. New test `RefreshCookieHttpsTests` asserts absence of Secure over HTTP and simulated presence with `X-Forwarded-Proto: https`. Simplifies semantics ahead of general refresh endpoint (Story 6) and avoids false Secure flag expectations during local dev without TLS.
    - Follow-up (2025-09-20): Consolidated refresh cookie issuance into `IssueRefreshCookie` helper; added `trust-dev-certs` Makefile target; HTTPS test now uses HTTPS base address for deterministic `Request.IsHttps`.
  - Auth/JWT — Story 5 Access Token Revocation (2025-09-20)
    - Added `TokenVersion` (int, default 0) column on users; every issued access token carries claim `v` (stringified int). `OnTokenValidated` event now queries the user’s current `TokenVersion`; if token claim `v` < stored value, authentication fails with `token_version_mismatch`. Password change endpoint increments `TokenVersion` atomically after successful verification, invalidating all previously issued access tokens while leaving refresh tokens to obtain a new access on next refresh flow. This avoids server-side token blacklists and enables instant revocation on credential compromise. Added claim mapping resilience: validation falls back to `ClaimTypes.NameIdentifier` when raw `sub` claim is mapped away; change-password endpoint falls back to `ClaimTypes.Email`. Integration test `AccessTokenVersionTests` confirms old token invalidation after password change. Future: admin-initiated version bump endpoint; potential caching of per-user version for high RPS.
  - Auth — JWT Baseline (Story 1) introduced (2025-09-20)
  - Auth/JWT — Story 5 (Validation Middleware & Principal) marked complete (2025-09-20) — composite scheme + GUID sub + token version checks (see section below for TokenVersion revocation details).
  - Auth/JWT — Story 5b HTTPS Secure Refresh Cookie E2E Harness complete (2025-09-20) — deterministic Secure attribute validation under real TLS.
    - Auth/JWT — Story 6 Refresh Endpoint (COMPLETED 2025-09-21)
      - Implemented `/api/auth/refresh` (cookie-first, transitional JSON body behind `AUTH__REFRESH_JSON_GRACE_ENABLED`) rotating neutral refresh tokens (revocation + issuance) and returning new neutral access token plus optional tenant-scoped token when `?tenant=` provided and membership matches. Structured 401 codes: `refresh_invalid`, `refresh_reuse`, `refresh_expired`; 400 `missing_refresh` and `refresh_body_disallowed`. Deprecation headers (`Deprecation: true`, `Sunset: <date>`) emitted when body token used and `AUTH__REFRESH_DEPRECATION_DATE` configured. In-memory EF provider incompatibility with transactions resolved by removing explicit transaction wrapping for rotation (sequential revoke/issue is acceptable given low contention). Tests `RefreshEndpointTests` now all passing (7/7) including rotation, tenant issuance, reuse detection, revoked reuse, missing token 400, expired, and grace body path. SnapshotArchitecture, LivingChecklist, and storyLog updated.
      - Centralized hashing: Added `RefreshTokenHashing` helper (Base64(SHA256(token UTF8))) consumed by `RefreshTokenService` and all endpoints (login, magic consume, select-tenant, refresh, logout) eliminating duplicated inline hashing code blocks and preventing future drift.
    - Auth/JWT — Story 7 Logout & Global Revocation (COMPLETED 2025-09-21)
      - Implemented endpoints:
        - `POST /api/auth/logout` — Revokes a single neutral refresh token (from cookie `rt` or JSON body `refreshToken` during grace). If a JSON body is present but missing `refreshToken`, returns 400 `{ code: "missing_refresh" }` (tests enforce). Clears cookie when present. Idempotent 204 otherwise (including already revoked/missing token scenarios). Structured log: `auth.logout.single user=<id> tokenFound=<bool>`.
        - `POST /api/auth/logout/all` — Bulk revokes all active neutral refresh tokens for the user and increments `User.TokenVersion` (record detach + replacement) to invalidate existing access tokens immediately. Always 204; logs `auth.logout.all` (or `user_missing`). Clears cookie if present.
      - Error codes: `missing_refresh` (400) on single logout when body supplied without token; reuse/invalid continue to surface via refresh endpoint (`refresh_reuse|refresh_invalid`).
      - Tests (`LogoutTests`) cover: single logout reuse detection, global logout access token invalidation, missing token 400, idempotent second logout. All passing.
      - Rationale: Provides explicit single‑device logout and full-session revocation using lightweight TokenVersion bump—no per access token storage required.
      - Deferred follow-ups: session listing & selective device logout, admin forced logout, deprecation headers for body path, removal of plaintext `refresh.token` post grace.
    - Auth/JWT — Story 8 Silent Refresh & Plaintext Refresh Token Suppression (COMPLETED 2025-09-21)
      - Backend flag `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` introduced. When false (default), API auth responses (login, magic consume, select-tenant, refresh) omit the plaintext `refresh.token` field while still returning its hash-derived metadata (id, created, expires) if present; the httpOnly cookie `rt` becomes the exclusive delivery channel. When true (temporary transitional aid), plaintext continues to be emitted (mirrors prior shape) enabling gradual frontend rollout.
      - Refresh endpoint (`/api/auth/refresh`) further gates plaintext emission: it only returns a plaintext refresh token when BOTH the expose flag is true AND either the JSON grace body path is used (`AUTH__REFRESH_JSON_GRACE_ENABLED`) or the secure cookie feature is disabled. This guarantees we never redundantly emit a plaintext token during the steady-state cookie-first flow once clients have migrated.
      - Unified object shaping: Replaced earlier anonymous conditional inline constructions with explicit local object composition (`var response = new { ... }`) followed by conditional mutation to append `refresh = new { token = <plaintext>, ... }` only when permitted, avoiding compilation issues from divergent anonymous types and simplifying future field additions.
      - Frontend: Implemented silent refresh loop in `apps/web/src/lib/authClient.ts` invoking real backend `/api/auth/refresh` (removing placeholder `_auth/refresh-neutral`). Schedules a refresh 60s before access token expiry (configurable skew constant), single-flights concurrent refresh attempts, and provides `startAutoRefresh`, `stopAutoRefresh`, and `forceRefresh` helpers. Adds 401 retry-once logic to `withAuthFetch` to transparently recover from narrowly expired access tokens (race between request send and expiry) without duplicating user actions.
      - Tests: Added `RefreshPlaintextExposedFlagTests` integration suite validating presence/absence of `refresh.token` across login & refresh under both flag states. Extended frontend unit tests (`authClient.test.ts`) to cover retry-once logic and scheduling behavior (mocked timers + forced expiry). All existing auth suites remain green.
      - Security rationale: Eliminates routine exposure of long-lived refresh secrets to JavaScript, reducing XSS blast radius. Cookie (`HttpOnly; SameSite=Lax; Secure over HTTPS`) confines token to browser credential channel; access token remains short-lived and in-memory only. Transitional flag permits incremental safe rollout and rollback lever if clients unexpectedly depend on plaintext field.
      - Operational considerations: Metrics/observability hooks for refresh rotations and suppressed/plaintext emission counts deferred to Observability hardening story. CSRF review (double submit or SameSite strategy) flagged for later if `SameSite=None` becomes necessary (e.g., cross-site embedding requirements).
      - Follow-ups:
        - Remove `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` after deprecation window once all clients verified.
        - Potential: Add `/api/auth/session` enumerator to expose active refresh token metadata (no plaintext) for future session management UI.
        - Observability counters: `auth.refresh.rotation`, `auth.refresh.reuse_denied`, `auth.refresh.plaintext_emitted` (temporary), `auth.refresh.plaintext_suppressed`.
        - Evaluate CSRF protection adjustments if moving to `SameSite=None` for any cross-origin flows.
      - Auth/JWT — Story 9 Auth Observability Metrics (2025-09-22)
        - Added first-wave OpenTelemetry metrics (Meter `Appostolic.Auth`) for login, refresh, token issuance, rotation, reuse/expired denial, plaintext emission/suppression (temporary), logout, and latency histograms. Chosen stable dot notation (`auth.login.success`, `auth.refresh.failure`, etc.) instead of earlier underscore placeholders documented in sprint plan. Endpoint `V1.cs` instrumented for /auth/login and /auth/refresh success/failure (bounded reason taxonomies) plus logout endpoints. Added rate‑limited counter increment path gated by existing refresh rate limiter flag. New test `AuthMetricsTests` asserts presence of new counters and histograms. Plaintext emission/suppression counters marked TEMP and will be removed post flag retirement. Documentation (this file, sprint plan, LivingChecklist, storyLog) updated; dashboards & span attribute enrichment deferred.
    - Added initial JWT access token infrastructure with `AuthJwtOptions` (issuer, audience, base64 signing key, TTLs, skew) and `JwtTokenService` (HS256). Authentication now registers `JwtBearerDefaults.AuthenticationScheme` plus retains the existing dev header scheme (conditional) for development ergonomics. A protected smoke endpoint `/auth-smoke/ping` validates end-to-end token issuance & validation, covered by new integration test `AuthJwtSmokeTests` issuing a neutral token. Swagger now includes a Bearer security definition alongside the DevHeaders scheme. Ephemeral signing key generation occurs only in Development when no key is configured; Production requires explicit `Auth:Jwt:SigningKey` (base64) and will fail fast if absent. Post-configure pattern applies validation parameters (issuer, audience, key, clock skew) without prematurely building a service provider. Subsequent stories will layer tenant claims, refresh tokens (secure httpOnly cookie), rotation & revocation via token_version, secure cookie flags (SameSite=Lax/Strict + secure), local HTTPS (Story 5a), nginx reverse proxy (Story 9a), and observability (counters for issuance/validation/failures).
  - After dropping legacy `role` columns and enforcing bitmask constraints, the invite creation endpoint now surfaces the generic `{ code: "NO_FLAGS" }` validation error when a request supplies only the deprecated single `role` field (and no `roles` / `rolesValue` flags). The earlier transition-specific `LEGACY_ROLE_DEPRECATED` error is now reserved only for the still-deprecated member single-role change endpoint (documented by its regression test) until that path is removed in a later story. Updated regression test `Invite_with_legacy_role_only_is_rejected_with_NO_FLAGS` reflects this invariant; full API test suite passes (193/193) post-clean rebuild.
  - Rationale: With the legacy column physically removed, treating a legacy-only payload as simply “missing flags” simplifies client handling and avoids implying a reversible transitional path.
  - Follow-up: remove the member single-role change legacy acceptance path and consolidate on a single error (`NO_FLAGS`) across all IAM write paths; publish rollback script/tag (`roles-removal-complete`).
- Auth/JWT — Story 2 Neutral + Tenant Access Tokens & Refresh Persistence (2025-09-20)
  - Introduced persisted refresh tokens (`refresh_tokens` table with hashed SHA256 token_hash, jsonb metadata, FK users, indexes on (user, created_at) and unique token_hash) and structured auth responses for `/api/auth/login` and `/api/auth/magic/consume`: `{ user, memberships, access, refresh, tenantToken? }`. Added `RefreshTokenService.IssueNeutralAsync` and `JwtTokenService.IssueTenantToken`. Single-membership users automatically receive a tenant-scoped token (`tenantToken.access`), while multi-membership selection is explicit via `?tenant=<slug|id>`; conflicting `?tenant=auto` when >1 membership returns 409. Magic consume now provisions a personal tenant & membership for new users, then returns the same structured token set. Legacy compatibility retained with `?includeLegacy=true` returning `{ user, memberships }` only.
  - Tests: `LoginJwtNeutralTests` (neutral + refresh + auto tenant), `LoginTenantSelectionTests` (multi-tenant selection & conflict), `MagicConsumeJwtTests` (structured & legacy shapes) all passing. Migration `20250920144932_s6_01_auth_refresh_tokens` applied successfully.
  - Rationale: Establishes secure hashed refresh storage and ergonomic tenant context while preserving backward compatibility; foundation for rotation, revocation, and secure cookie delivery in upcoming stories.
  - Follow-ups: Story 2a test token factory; Story 3 refresh rotation & reuse detection; add revocation strategy (token_version or hash invalidation); secure httpOnly cookie + HTTPS (Stories 4/5a); telemetry counters; negative tests for expired/consumed tokens.
  - Auth/JWT — Story 2a Test Token Factory Helper (2025-09-20)
    - Added gated internal test helper endpoint `POST /api/test/mint-tenant-token` (maps only when `AUTH__TEST_HELPERS_ENABLED=true` AND not Production) to mint a neutral access + refresh token (and optional tenant-scoped token) for an arbitrary email, auto-provisioning personal tenant/membership when absent. Supports optional `{ tenant: <slugOrId>, autoTenant: bool }` selection semantics mirroring login auto-tenant issuance.
    - Introduced `TestAuthClient` in `apps/api.tests` encapsulating helper usage; new tests `TestTokenFactoryTests` cover single-membership auto issuance, multi-membership explicit selection (partial slug mismatch → no tenant token), and disabled flag returns 404 via derived factory override (ensuring production safety). WebAppFactory now injects test helper config via in-memory configuration (no reliance on process-wide environment mutation for determinism).
    - Rationale: Eliminates multi-step auth flow boilerplate in non-auth-focused integration tests (reduces flakiness/time) while preserving production surface area (endpoint absent in Production by configuration gate). Provides scaffold for future direct issuance helpers (e.g., select-tenant, refresh) if needed.
    - Follow-ups: Replace ad-hoc auth setup in existing integration tests incrementally with `TestAuthClient` where flows aren’t under test; proceed to Story 3 (tenant selection endpoint) and Story 6 (refresh rotation) before cookie delivery (Stories 4/5a).
    - Auth/JWT — Story 3 Tenant Selection & Refresh Rotation (2025-09-20)
      - Added `POST /api/auth/select-tenant` converting a neutral refresh token + tenant identifier (slug or id) into a tenant-scoped access token while also rotating the neutral refresh token (old revoked, new issued). Response shape aligns with login: `{ user, memberships, access, refresh, tenantToken }` with `tenantToken.access` holding tenant claims (`tenant_id`, `tenant_slug`, roles flags). Membership validation returns 403 on non-membership; invalid/expired/revoked refresh returns 401. Implementation revokes old refresh before issuing new to enforce single active chain. Fixed hashing mismatch (endpoint initially used hex hashing; storage uses Base64(SHA256))—tests caught 401s, leading to alignment with `RefreshTokenService` hashing (Base64 SHA256). Added integration tests: success rotation (old unusable), invalid token 401, forbidden tenant 403, expired refresh 401, revoked reuse 401. Sets foundation for general refresh endpoint (Story 6) and upcoming secure cookie delivery.
- Web — Flags-only cleanup (2025-09-20)
  - Removed deprecated `TenantAwareTopBar` stub and transitional legacy fallback test suite. `roles.ts` no longer exports `LegacyRole` or performs any legacy name fallback/expansion (only canonical flags via array, numeric bitmask, or numeric string; comma-separated canonical flags accepted temporarily). Added an ESLint `no-restricted-properties` rule preventing accidental use of `membership.role`. Frontend now depends solely on explicit flags for gating and labeling.
  - Follow-up: remove comma-separated string tolerance in a later hardening pass once telemetry confirms absence.

- IAM — Legacy invite `role` write path deprecated (Story 4 refLeg-04) (2025-09-19)
  - The invite creation endpoint `POST /api/tenants/{tenantId}/invites` now rejects any request providing the legacy single `role` field. Clients MUST supply granular roles via either `roles: ["TenantAdmin", ...]` or `rolesValue: int` (bitmask of flags). A BadRequest is returned with `{ code: "LEGACY_ROLE_DEPRECATED" }` when `role` is present. This locks in the flags-first contract ahead of physically dropping the legacy `role` column.
  - Legacy `Membership.Role` column and member role change endpoint still accept the legacy role name during the transitional window; convergence + seeds ensure `Roles` bitmask stays authoritative. A follow-up story will extend the deprecation to the member role change endpoint and then remove the legacy column + mapping.
  - Response payload for invites no longer echoes a legacy `role`; it returns `{ email, roles: "FlagsString", rolesValue, expiresAt }` (string representation of flags for readability + numeric for machine use).
  - Added regression tests (`LegacyRoleWritePathDeprecationTests`) asserting: (1) invite with legacy `role` is rejected with `LEGACY_ROLE_DEPRECATED`; (2) member role change with legacy `role` is CURRENTLY accepted (documenting transitional behavior) so we can intentionally flip expectation in a future commit without silent breakage.
  - Email template for invites now lists the composite roles flags (e.g., `TenantAdmin, Creator`) instead of a single legacy role name.

- Auth/Data — Role change preserves flags bitmask (2025-09-19)
  - The member role mutation endpoint (`PUT /api/tenants/{tenantId}/members/{userId}`) previously recreated the `Membership` record without copying the `Roles` flags, risking `roles=0` after a legacy `MembershipRole` change. All replacement paths now assign `Roles = DeriveFlagsFromLegacy(newRole)`, keeping the bitmask authoritative. Test seeding (`WebAppFactory`) updated to include full flags for the default owner membership; an integration test asserts role change Owner→Editor yields `Creator|Learner` flags (non-zero). Positions us to optionally add a DB constraint (`roles <> 0`) and ultimately remove legacy `Role` column.

- Auth/Data — Backfill zero roles memberships to full flags (2025-09-19)
  - Data-only migration `s5_02_membership_roles_backfill_zero_to_all` updates any lingering `app.memberships.roles = 0` rows to `15` (TenantAdmin|Approver|Creator|Learner) established during the legacy→flags transition. Idempotent (`roles=0` predicate) and non-reversible (Down no-op) to prevent reintroducing invalid state. Rationale: guarantee all memberships have a non-zero bitmask before disabling the temporary web legacy fallback and proceeding to drop the legacy `role` column.

- Auth/API — Auth endpoints now emit numeric roles bitmask (2025-09-19)
  - `/api/auth/login` and magic token consume (signup+auto-login path) now serialize membership roles flags as an integer (`roles = (int)m.Roles`) instead of the enum flags string. This guarantees the frontend roles helper (which decodes numeric bitmasks or arrays) receives a canonical machine-friendly representation, eliminating reliance on the temporary legacy role fallback. Tests added to assert presence and numeric type.

- Nav — Admin gating tightening (2025-09-18)
  - UI Admin menu now requires explicit presence of `TenantAdmin` in the selected membership’s roles flags (`isAdmin && roles.includes('TenantAdmin')`). Composite non-admin flag sets (e.g., Approver+Creator+Learner — bitmask 14) no longer qualify even if upstream boolean derivation is broadened. Regression test added to lock behavior. Rationale: eliminate privilege inflation risk during transitional legacy fallback period.

- Auth/Web — Numeric roles bitmask support (2025-09-18)
  - `membership.roles` may now arrive as an integer bitmask (or numeric string) matching API `[Flags] Roles` enum. Web helper `getFlagRoles` interprets numeric/ string values directly (`1` => TenantAdmin, `15` => all). A value of `0` yields empty roles without invoking legacy fallback (explicit empty). Prevents admin users represented solely by a numeric bitmask from appearing as Learner-only.

- Auth / Web — Removal of legacy MembershipRole fallback; flags-only authorization (2025-09-18)
  - Fully removed UI and helper fallbacks that previously considered the legacy `role` string (`Owner`/`Admin`/`Editor`/`Viewer`). Authorization and gating now rely solely on the `roles[]` flags bitmask (`TenantAdmin`, `Approver`, `Creator`, `Learner`).
  - Deleted legacy expansion logic (`deriveFlagsFromLegacy`, `PREFER_LEGACY_FOR_ADMIN`, single-tenant safety) from `apps/web/src/lib/roles.ts` and simplified `computeBooleansForTenant` to interpret only provided flags.
  - Simplified server/page guards (`roleGuard.ts`) to remove dual-mode branching; TopBar/Admin pages (Members, Invites, Audits, Notifications, Settings) now gate exclusively via `{ isAdmin }` derived from flags.
  - Updated tests to supply explicit `roles: ['TenantAdmin']` where admin access is expected; removed assertions relying on legacy Owner/Admin acceptance. All web tests green post-migration.
  - Revert point commit retained for emergency rollback (`REVERT POINT: pre removal of legacy role fallback`).
  - Rationale: Eliminate inconsistent gating and hidden privilege retention caused by OR-ing legacy and flag sources of truth; reduce cognitive load and future maintenance surface ahead of 1.0.

- Auth — API RoleAuthorization prefers Roles flags (2025-09-18)
  - Updated the authorization handler to treat Roles flags as the source of truth and only fall back to the legacy MembershipRole when Roles == None. This fixes a field issue where a tenant originator could remain effectively admin after demotion because legacy role and flags were previously OR-ed together.
  - Outcome: Admin-only endpoints now deny access appropriately after roles demotion; UI already layers additional safety (single-tenant safeguard + legacy-aligned TopBar suppress). API tests PASS (180/180) post-change.

- Nav — Single-tenant admin gating safeguard (2025-09-18)
  - To address a field report where a non-admin user with exactly one tenant membership saw the Admin menu, the shared roles helper now includes a single-tenant safety: when there is exactly one membership and its legacy role is non-admin, the derived TenantAdmin flag is suppressed for UI gating. This prevents accidental elevation when backend flags are inconsistent.
  - An optional env switch `NEXT_PUBLIC_PREFER_LEGACY_ROLES=true` further prefers the legacy role over flags when they conflict, adding a belt-and-suspenders protection during the transition. A dev-only console warning surfaces mismatches for quick diagnosis.
  - Tests updated: the flags-based admin visibility test now uses a multi-tenant session to validate the intended behavior while respecting the single-tenant safeguard; full web suite PASS.

- User Profile — Avatar pipeline simplification & absolute URLs (2025-09-17)
- Web — Org Settings (Tenant Settings) scaffold (2025-09-18)
  - Page `/studio/admin/settings` now renders a server-gated (TenantAdmin) UI: - Branding logo upload client (`TenantLogoUpload`) wired to `/api-proxy/tenants/logo` with validation and cache-busted preview. - Organization details form (`TenantSettingsForm`) for display name, contact (email/website), and social links; submits minimal merge patch to `/api-proxy/tenants/settings`.
    │ │ ├─ Infrastructure/
    │ │ │ ├─ AppDbContext.cs
    │ │ │ └─ Configurations/
    │ │ │ └─ _.cs
    │ │ ├─ Migrations/
    │ │ │ └─ _.cs
    │ │ ├─ tools/
    │ │ │ └─ seed/ # Idempotent seed for dev user/tenants
    │ │ └─ Properties/launchSettings.json
    │ ├─ web/
    ﻿# Appostolic — Architecture Snapshot (2025-09-22)

    This document captures an at-a-point-in-time view of the system’s architecture, recent evolutionary changes, and guiding conventions. It was deduplicated on 2025‑09‑22 to remove large repeated "What’s new" sections that accumulated during rapid Story 3 edits.

    ## What’s new (condensed, latest first)
    - Added early-pipeline `DevHeadersDeprecationMiddleware` rejecting any request containing deprecated development headers (`x-dev-user`, `x-tenant`) with 401 `{ code: "dev_headers_deprecated" }` and increments counter metric `auth.dev_headers.deprecated_requests` (tag: path). Negative-path regression tests updated accordingly. Soft‑block observability phase before physical removal (Story 4).
      Auth/JWT — RDH Story 3 Deprecation Middleware (2025-09-22)
    - Early-pipeline `DevHeadersDeprecationMiddleware` rejects any request containing deprecated development headers (`x-dev-user`, `x-tenant`) with 401 `{ code: "dev_headers_deprecated" }`; increments metric `auth.dev_headers.deprecated_requests` (tag: path). Soft‑block observability phase before physical removal.
    - First-wave OpenTelemetry counters/histograms: login, refresh (success/failure reasons), token issuance & rotation, reuse/expired denial, plaintext emission suppression (TEMP), logout, latency distributions. Tests assert presence; dashboards deferred.
      Auth/JWT — Story 9 Auth Observability Metrics (2025-09-22)
    - First-wave OpenTelemetry counters & histograms (login, refresh success/failure reasons, rotation, reuse denial, plaintext emission suppression, logout, latency). TEMP plaintext emission/suppression counters slated for removal post flag retirement.
    - Added single + global logout endpoints (global bumps TokenVersion). Introduced silent refresh loop on web; flag‑driven suppression of plaintext refresh token field (cookie-only steady state). Transitional expose & grace flags documented for rollback.
      Auth/JWT — Stories 7–8 Logout, Silent Refresh, Plaintext Suppression (2025-09-21)
    - Single + global logout (TokenVersion bump), silent refresh loop (web), transitional plaintext refresh token expose flag → suppression steady state.
    - `/api/auth/refresh` rotates neutral refresh token (hash stored Base64(SHA256)), issues new access (+ optional tenant access). Structured error codes and deprecation headers for body path during grace window.
      Auth/JWT — Story 6 Refresh Endpoint (2025-09-21)
    - `/api/auth/refresh` rotates neutral refresh token (Base64(SHA256) hash stored); structured errors & deprecation headers for legacy body path under grace flag.
    - `TokenVersion` claim `v` revocation strategy; secure cookie semantics validated under real HTTPS local cert. Helper consolidated for refresh cookie issuance.
      Auth/JWT — Access Token Revocation & HTTPS Cookie Harness (2025-09-20)
    - `TokenVersion` claim revocation strategy; local HTTPS harness + unified refresh cookie helper.
    - Development-only `BearerOrDev` policy scheme picks Dev header or Bearer automatically; reduces per-endpoint scheme lists and isolates Prod to Bearer only.
      Auth/JWT — Composite Dev+Bearer Policy Scheme (2025-09-20)
    - Development-only `BearerOrDev` selector (targeted for removal Story 4) simplifying per-endpoint schemes.
    - Removed duplicate transaction paths causing EF InMemory double execution & relational-only SQL call; unified provider-aware implementation (suite green).
      IAM — Roles Assignment Endpoint Simplification (2025-09-21)
    - Removed duplicate transactional branches; unified provider-aware logic.
    - Removed legacy role fallbacks across API/UI; enforced flags bitmask as source of truth; backfilled zero roles; updated invites & member mutations; admin gating tightened.
      Roles & Legacy Cleanup (Sept 18–20)
    - Removed legacy role fallbacks; enforced flags bitmask; updated invites/members.
    - Added gated `/api/test/mint-tenant-token` plus `TestAuthClient` for issuing structured tokens in tests without dev headers.
      Test Helpers & Token Factory (2025-09-20)
    - Gated test token mint endpoint + `TestAuthClient` for simplified non-auth test setup.

    ### Dev Headers Deprecation Lifecycle (Concise)
    1. Migration (Stories 1–2): Migrate tests/helpers from `x-dev-user` / `x-tenant` to real JWT login + tenant selection.
    2. Guard (Phase D): CI test (`DevHeadersUsageGuardTests`) blocks reintroduction except explicit negative-path suites.
    3. Deprecation Mode (Story 3 – current): Middleware denies headers with `dev_headers_deprecated` + metric for observability.
    4. Physical Removal (Story 4 – next): Delete `DevHeaderAuthHandler`, composite `BearerOrDev`, feature flag `AUTH__ALLOW_DEV_HEADERS`; negative tests expect `dev_headers_removed`.
    5. Hard Guard & Cleanup (Stories 5–6): Remove temporary metric/middleware (or convert to noop), enforce static/code scanning, finalize docs, create rollback tag `before-dev-header-removal`.
    6. Post-Removal Monitoring: Optional alerting on spikes; retire metric when consistently zero.

    <!-- NOTE: File deduplicated 2025-09-22 (RDH Story 3 documentation hygiene). Historical granular entries trimmed; prior duplicates intentionally removed for clarity. -->

    ## Tech stack (high level)
    - Backend: .NET 8 (Minimal APIs), EF Core 8 (PostgreSQL), OpenTelemetry, FluentValidation
    - Frontend: Next.js 14 (App Router), React 18, TypeScript, MUI (Premium), Vitest
    - Mobile: Expo / React Native (TypeScript)
    - Workers: Notifications, Rendering (queue-driven, out-of-process)
    - Infra (dev): Docker Compose (Postgres, Redis, MinIO, Mailhog, Qdrant), Make targets, pnpm monorepo
    - Observability: Structured logging, OTLP metrics/spans (early metrics stories), future tracing enrichment
    - Auth: JWT (neutral + tenant tokens, refresh rotation, TokenVersion revocation), secure httpOnly cookie refresh delivery, structured error codes

    ## Conventions & Principles (abridged)
    - Server-first authorization; UI derives capability flags from issued claims & memberships.
    - Deterministic tests; ephemeral dev conveniences (e.g., dev headers) are temporary and removed pre‑1.0.
    - Explicit feature flags with documented deprecation/rollback path.
    - Structured error payloads `{ code: string }` to enable stable frontend branching & regression tests.
    - Hash sensitive tokens at rest (Base64(SHA256)) — never store or re-emit raw refresh tokens after suppression flag steady state.

    ## Current Auth Flow (summary)

    Login → (neutral access + refresh) → optional tenant selection (tenant-scoped access + rotated neutral refresh) → silent refresh loop (cookie) → logout single or all (TokenVersion bump) → revocation invalidates prior access tokens via version mismatch guard.

    ## Pending / Near-Term (selected)
    - Story 4: Remove dev header handler, composite scheme, flag; introduce `dev_headers_removed` code.
    - Retire temporary plaintext refresh metrics & emission once exposure flag off in all envs.
    - Auth session introspection endpoint (enumerate active refresh token metadata) — backlog.
    - Observability hardening: Add span attributes & dashboards for auth flows.

    ## Historical Detail

    Verbose historical "What’s new" change logs prior to 2025‑09‑22 have been trimmed from this snapshot for brevity. Refer to `devInfo/storyLog.md` or Git history for granular past entries if needed.

    │ │ └─ src/index.ts
    │ ├─ models/
    │ │ └─ src/\*.ts
    │ ├─ prompts/
    │ ├─ ui/
    ﻿# Appostolic — Architecture Snapshot (2025-09-22)

    This document captures an at-a-point-in-time view of the system’s architecture, recent evolutionary changes, and guiding conventions. It was deduplicated on 2025‑09‑22 to remove large repeated "What’s new" sections that accumulated during rapid Story 3 edits.

    ## What’s new (condensed, latest first)
    - Auth/JWT — RDH Story 3 Deprecation Middleware (2025-09-22)
      - Added early-pipeline `DevHeadersDeprecationMiddleware` rejecting any request containing deprecated development headers (`x-dev-user`, `x-tenant`) with 401 `{ code: "dev_headers_deprecated" }` and increments counter metric `auth.dev_headers.deprecated_requests` (tag: path). Negative-path regression tests updated accordingly. Soft‑block observability phase before physical removal (Story 4).
    - Auth/JWT — Story 9 Auth Observability Metrics (2025-09-22)
      - First-wave OpenTelemetry counters/histograms: login, refresh (success/failure reasons), token issuance & rotation, reuse/expired denial, plaintext emission suppression (TEMP), logout, latency distributions. Tests assert presence; dashboards deferred.
    - Auth/JWT — Stories 7–8 Logout, Silent Refresh, Plaintext Suppression (2025-09-21)
      - Added single + global logout endpoints (global bumps TokenVersion). Introduced silent refresh loop on web; flag‑driven suppression of plaintext refresh token field (cookie-only steady state). Transitional expose & grace flags documented for rollback.
    - Auth/JWT — Story 6 Refresh Endpoint (2025-09-21)
      - `/api/auth/refresh` rotates neutral refresh token (hash stored Base64(SHA256)), issues new access (+ optional tenant access). Structured error codes and deprecation headers for body path during grace window.
    - Auth/JWT — Access Token Revocation & HTTPS Cookie Harness (2025-09-20)
      - `TokenVersion` claim `v` revocation strategy; secure cookie semantics validated under real HTTPS local cert. Helper consolidated for refresh cookie issuance.
    - Auth/JWT — Composite Dev+Bearer Policy Scheme (2025-09-20)
      - Development-only `BearerOrDev` policy scheme picks Dev header or Bearer automatically; reduces per-endpoint scheme lists and isolates Prod to Bearer only.
    - IAM — Roles assignment endpoint simplification (2025-09-21)
      - Removed duplicate transaction paths causing EF InMemory double execution & relational-only SQL call; unified provider-aware implementation (suite green).
    - Roles & Legacy Cleanup (Sept 18–20)
      - Removed legacy role fallbacks across API/UI; enforced flags bitmask as source of truth; backfilled zero roles; updated invites & member mutations; admin gating tightened.
    - Test Helpers & Token Factory (2025-09-20)
      - Added gated `/api/test/mint-tenant-token` plus `TestAuthClient` for issuing structured tokens in tests without dev headers.

    ### Dev Headers Deprecation Lifecycle (Concise)
    1. Migration (Stories 1–2): Migrate tests/helpers from `x-dev-user` / `x-tenant` to real JWT login + tenant selection.
    2. Guard (Phase D): CI test (`DevHeadersUsageGuardTests`) blocks reintroduction except explicit negative-path suites.
    3. Deprecation Mode (Story 3 – current): Middleware denies headers with `dev_headers_deprecated` + metric for observability.
    4. Physical Removal (Story 4 – next): Delete `DevHeaderAuthHandler`, composite `BearerOrDev`, feature flag `AUTH__ALLOW_DEV_HEADERS`; negative tests expect `dev_headers_removed`.
    5. Hard Guard & Cleanup (Stories 5–6): Remove temporary metric/middleware (or convert to noop), enforce static/code scanning, finalize docs, create rollback tag `before-dev-header-removal`.
    6. Post-Removal Monitoring: Optional alerting on spikes; retire metric when consistently zero.

    <!-- NOTE: File deduplicated 2025-09-22 (RDH Story 3 documentation hygiene). Historical granular entries trimmed; prior duplicates intentionally removed for clarity. -->

    ## Tech stack (high level)
    - Backend: .NET 8 (Minimal APIs), EF Core 8 (PostgreSQL), OpenTelemetry, FluentValidation
    - Frontend: Next.js 14 (App Router), React 18, TypeScript, MUI (Premium), Vitest
    - Mobile: Expo / React Native (TypeScript)
    - Workers: Notifications, Rendering (queue-driven, out-of-process)
    - Infra (dev): Docker Compose (Postgres, Redis, MinIO, Mailhog, Qdrant), Make targets, pnpm monorepo
    - Observability: Structured logging, OTLP metrics/spans (early metrics stories), future tracing enrichment
    - Auth: JWT (neutral + tenant tokens, refresh rotation, TokenVersion revocation), secure httpOnly cookie refresh delivery, structured error codes

    ## Conventions & Principles (abridged)
    - Server-first authorization; UI derives capability flags from issued claims & memberships.
    - Deterministic tests; ephemeral dev conveniences (e.g., dev headers) are temporary and removed pre‑1.0.
    - Explicit feature flags with documented deprecation/rollback path.
    - Structured error payloads `{ code: string }` to enable stable frontend branching & regression tests.
    - Hash sensitive tokens at rest (Base64(SHA256)) — never store or re-emit raw refresh tokens after suppression flag steady state.

    ## Current Auth Flow (summary)

    Login → (neutral access + refresh) → optional tenant selection (tenant-scoped access + rotated neutral refresh) → silent refresh loop (cookie) → logout single or all (TokenVersion bump) → revocation invalidates prior access tokens via version mismatch guard.

    ## Pending / Near-Term (selected)
    - Story 4: Remove dev header handler, composite scheme, flag; introduce `dev_headers_removed` code.
    - Retire temporary plaintext refresh metrics & emission once exposure flag off in all envs.
    - Auth session introspection endpoint (enumerate active refresh token metadata) — backlog.
    - Observability hardening: Add span attributes & dashboards for auth flows.

    ## Historical Detail

    Verbose historical "What’s new" change logs prior to 2025‑09‑22 have been trimmed from this snapshot for brevity. Refer to `devInfo/storyLog.md` or Git history for granular past entries if needed.

- Composition: Reuses the API’s notifications DI via `AddNotificationsRuntime(...)` and the same `AppDbContext` (PostgreSQL). Auto‑migrates in Development/Test when relational.
- Runtime gating: `NotificationsRuntimeOptions` controls hosted services. Recommended ops setting when the worker is running: set API `Notifications:Runtime:RunDispatcher=false` so only the worker processes the outbox; the worker may keep `RunDispatcher=true`.
- Transport: Shares the same `INotificationTransport` selection — `channel` (default) or `redis` when `Notifications:Transport:Mode=redis`.
- Telemetry: OpenTelemetry traces/metrics with optional OTLP exporter; console exporters in Development.

---

## Cross-cutting concerns

### Authentication & Authorization

- Dev headers (API): `x-dev-user` and `x-tenant`; emits claims `sub`, `email`, `tenant_id`, `tenant_slug`. All `/api/*` require authorization (dev headers expected). Swagger remains public.
  - Superadmin (dev/test friendly): `DevHeaderAuthHandler` can emit a `superadmin` claim when header `x-superadmin: true` is present or the user's email is included in config allowlist `Auth:SuperAdminEmails`. Used to enable cross-tenant notification views in admin endpoints.
- Web tenant selection/switcher:
  - Two‑stage login with `/select-tenant`; auto‑select when single membership.
  - Header `TenantSwitcher` updates session via `session.update({ tenant })` and sets `selected_tenant` cookie via `/api/tenant/select`.
  - Server proxies forward `x-tenant` based on session or cookie; when web auth is enabled, protected routes require a selected tenant (401), except invite acceptance route.
  - Cookie vs session: `selected_tenant` is a routing hint for the web layer; authorization uses server-side session/JWT and API claims. The cookie is httpOnly, SameSite=Lax, and secure in production.
- Role-based guards (Auth‑11): server-only helpers (`roleGuard.ts`) enforce Owner/Admin on sensitive proxy routes for defense-in-depth.
- Security contract: a dev-mode integration test verifies unauthenticated `/api/*` calls return 401/403; the same requests succeed with dev headers.

- Magic Link (passwordless) — Auth‑ML
  - API endpoints:
    - `POST /api/auth/magic/request { email }` → always `202 Accepted`; creates a login token row with `token_hash` (SHA‑256) and TTL=15m, and enqueues a Magic Link email with an absolute link to `/magic/verify?token=…`. Includes basic per‑email rate limiting.
    - `POST /api/auth/magic/consume { token }` → validates via hash+TTL, enforces single‑use (`consumed_at`), and returns minimal user payload. If the user doesn’t exist, it creates the user and a personal tenant (`{localpart}-personal`, de‑duped with `-2`, `-3`, …) and an Owner membership.
  - Persistence: `app.login_tokens` with indexes (unique on `token_hash`; `(email, created_at DESC)`; partial on `consumed_at IS NULL`). Raw tokens are never stored.
  - Email: `EmailKind.MagicLink` templates (Scriban) render subject/text/html; NotificationEnqueuer pre‑renders snapshots and stores only the token hash; logs avoid raw token.
  - Web integration: public pages `/magic/request` and `/magic/verify`; same‑origin proxies `/api-proxy/auth/magic/request` and `/api-proxy/auth/magic/consume` avoid CORS. The verify page bridges into the session via NextAuth Credentials (dual‑mode) and redirects to `/select-tenant` (honors optional `?next=`).

#### Auth Observability Metrics (Story 9 — 2025-09-22)

Meter: `Appostolic.Auth`

Counters (increment-only):

| Name                                    | Description                                                       | Key Tags                                      |
| --------------------------------------- | ----------------------------------------------------------------- | --------------------------------------------- |
| `auth.tokens.issued`                    | Access tokens issued (neutral + tenant)                           | `user_id`, optional `tenant_id`               |
| `auth.refresh.rotations`                | Successful refresh rotations (old revoked → new issued)           | `user_id`, `old_refresh_id`, `new_refresh_id` |
| `auth.refresh.reuse_denied`             | Refresh attempts rejected due to reuse of revoked token           | optional `user_id`, `refresh_id`              |
| `auth.refresh.expired`                  | Refresh attempts rejected due to expiration                       | optional `user_id`, `refresh_id`              |
| `auth.refresh.plaintext_emitted` (TEMP) | Plaintext refresh token included in JSON (to monitor deprecation) | `user_id`                                     |
| `auth.refresh.plaintext_suppressed`     | Plaintext emission suppressed (flag off)                          | `user_id`                                     |
| `auth.login.success`                    | Successful password/magic login (neutral issuance)                | `user_id`, `memberships` (count)              |
| `auth.login.failure`                    | Failed login attempt                                              | `reason`, optional `user_id`                  |
| `auth.refresh.success`                  | Successful refresh (neutral access re-issue + rotation)           | `user_id`                                     |
| `auth.refresh.failure`                  | Failed refresh attempt                                            | `reason`, optional `user_id`                  |
| `auth.refresh.rate_limited`             | Refresh denied by rate limiter                                    | (no tags)                                     |
| `auth.logout.single`                    | Single-device logout invocation                                   | `user_id`, `token_found` (bool)               |
| `auth.logout.all`                       | Global logout (all sessions)                                      | `user_id`, `revoked_count`                    |

Histograms:

| Name                       | Unit | Description                        | Tags             |
| -------------------------- | ---- | ---------------------------------- | ---------------- | -------- |
| `auth.login.duration_ms`   | ms   | End-to-end login processing time   | `outcome=success | failure` |
| `auth.refresh.duration_ms` | ms   | End-to-end refresh processing time | `outcome=success | failure` |

Reason taxonomies (bounded, low-cardinality — add new values only with documentation update):

Login failure reasons: `missing_fields`, `unknown_user`, `invalid_credentials`.

Refresh failure reasons: `missing_refresh`, `refresh_body_disallowed`, `refresh_invalid`, `refresh_reuse`, `refresh_expired`, `refresh_forbidden_tenant`, `rate_limited`.

Design Notes:

- Dot notation chosen for hierarchy readability and Prometheus/OpenTelemetry convention compatibility; underscores avoided in favor of dots except where legacy metrics already shipped (none external yet).
- User/refresh IDs included only where already internal identifiers (no PII / plaintext tokens). Consider future sampling or attribute scrubbing if high-cardinality pressure observed; current usage limited to security forensics.
- Plaintext emission counters are temporary; removal trigger: flag `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` disabled in all environments for two consecutive releases with zero `auth.refresh.plaintext_emitted` increments.
- Tracing enrichment (span attributes `auth.user_id`, `auth.refresh.reason`, etc.) and dashboard exemplars (success ratio, p95 latency, failure reason breakdown, reuse anomaly alert) deferred to a later observability hardening pass.
- Rate limiting instrumentation (`auth.refresh.rate_limited`) incremented in the guarded branch before failure return path ensuring latency histogram records with outcome `failure`.
- All reason tag sets are intentionally small to protect backend/exporter cardinality; any future expansion must update this section and corresponding tests.

Follow-ups (deferred): tracing span attributes; Grafana/Tempo dashboards; remove TEMP counters post rollout; potential consolidation of `reuse_denied` + `refresh.failure{reason=refresh_reuse}` into a single semantic view (keep raw counters for now for simpler alerting).

- Passwords — Auth‑PW
  - API endpoints:
    - `POST /api/auth/forgot-password { email }` → `202 Accepted` always; enqueues a password reset email with an absolute link to `/reset-password?token=…`. Includes basic per‑email rate limiting.
    - `POST /api/auth/reset-password { token, newPassword }` → `204 No Content` on success; validates token by hash+TTL and enforces single‑use.
    - `POST /api/auth/change-password { currentPassword, newPassword }` → `204 No Content` when authorized and current password matches.
  - Email: Password reset uses Scriban templates; raw tokens are never persisted. Token hashes follow the same PII hardening pattern as verification/invite (Notif‑21) and are only stored as hashes.
  - Web integration: minimal pages `/forgot-password`, `/reset-password`, and `/change-password` post to same-origin proxies under `/api-proxy/auth/*`. UI affordances: “Forgot password?” link on the Login page and a “Change password” link in the protected header near the TenantSwitcher. Unit tests cover happy paths and negative cases (invalid token, wrong current).

### Multi-tenancy & RLS

- `TenantScopeMiddleware` skips `/health*` and `/swagger*`; when authenticated and `tenant_id` exists, begins a DB transaction and sets `app.tenant_id` GUC for RLS.
- Legacy demo header `X-Tenant-Id` in `Program.cs` for sample `/lessons` endpoints.

---

## Domain capabilities

### Agents (runtime v1)

- Domain types: Agent, AgentTask, AgentTrace; enums AgentStatus, TraceKind
- Validation: `Guard.cs` enforces invariants (NotNull, MaxLength, InRange)
- Orchestration: `AgentOrchestrator` with deterministic `MockModelAdapter` in dev; allowlist enforcement; trace step numbering strategy
- TraceWriter: persists traces; clamps non‑negatives; retries once on unique conflicts
- Tools: `web.search`, `db.query`, `fs.write` via `ToolRegistry`
- Queue/Worker: `InMemoryAgentTaskQueue` (Channel<Guid>, SingleReader=true); `AgentTaskWorker` consumes and processes with idempotence and graceful shutdown semantics
- Agent store resolution is DB‑first with fallback to static `AgentRegistry`

Endpoints:

- `GET /api/agents` (+ paging, includeDisabled)
- `GET /api/agents/{id}` | `POST /api/agents` | `PUT /api/agents/{id}` | `DELETE /api/agents/{id}`
- `GET /api/agents/tools`
- Agent tasks: `POST /api/agent-tasks`, `GET /api/agent-tasks/{id}?includeTraces=true`, `GET /api/agent-tasks` (filters, paging; `X-Total-Count`)

### Notifications (email)

- Metrics: `email.sent.total` and `email.failed.total` counters (tagged by email kind) exposed via OTEL Meter "Appostolic.Metrics".
- Notif-30: Resend telemetry — `email.resend.total` (tags: kind, mode=manual|bulk, tenant_scope=self|superadmin|dev, outcome),
  `email.resend.throttled.total` (same tags), and histogram `email.resend.batch.size` (tags: tenant_scope, and kind when filtered).
- Bulk header: `X-Resend-Remaining` on `/api/notifications/resend-bulk` reflects remaining per-tenant daily cap when tenant context is known.

- Resend history (Notif‑31):
  - `GET /api/notifications/{id}/resends` lists child resend notifications linked to the original.
  - Paging via `take`/`skip`; sets `X‑Total‑Count` header; ordered by `CreatedAt DESC`.
  - Tenant scoping enforced: non‑superadmin limited to current tenant; superadmin may view across tenants.

- Automated resend (Notif‑32):
  - Background scanner (`AutoResendHostedService` + `AutoResendScanner`) runs on an interval to detect "no‑action" notifications and enqueue resends with reason `auto_no_action`.
  - Eligibility: originals only (no `ResendOfNotificationId`), `Status=Sent`, older than `AutoResendNoActionWindow`, no existing resend child, and not explicitly delivered/opened by provider webhook.
  - Guardrails: respects `ResendThrottleWindow`, per‑scan cap `AutoResendMaxPerScan`, and per‑tenant daily cap `AutoResendPerTenantDailyCap`.
  - Observability: reuse `email.resend.total` metrics with `mode=auto` and outcomes `created|throttled|forbidden|error`.
  - Configuration (NotificationOptions): `EnableAutoResend` (default false), `AutoResendScanInterval` (5m), `AutoResendNoActionWindow` (24h), `AutoResendMaxPerScan` (50), `AutoResendPerTenantDailyCap` (200).

- Transport: `INotificationTransport` abstracts the "notification queued" signal. Default `ChannelNotificationTransport` bridges to the existing in‑process `INotificationIdQueue` to preserve behavior and enable future broker integration.
  Transport: `INotificationTransport` abstracts the "notification queued" signal. Default `ChannelNotificationTransport` bridges to the existing in‑process `INotificationIdQueue` to preserve behavior and enable future broker integration. Optionally, set `Notifications:Transport:Mode=redis` to publish via Redis (`RedisNotificationTransport`) and enable the subscriber hosted service that forwards Pub/Sub messages to the dispatcher. The transport is now used consistently across:
  - Enqueue helpers (`NotificationEnqueuer`)
  - Admin/dev retry/resend endpoints (including bulk resend)
  - Automated resend scanner
    This ensures a single seam to swap in an external broker later without touching endpoint logic.

Redis transport configuration

- Options (NotificationTransportOptions):
  - `Notifications:Transport:Mode` — `channel` (default) or `redis`.
  - `Notifications:Transport:Redis:ConnectionString` — optional; if provided, used verbatim.
  - `Notifications:Transport:Redis:Host` (default `127.0.0.1`), `Port` (default `6380`), `Password` (optional), `Ssl` (bool, default false), `Channel` (default `app:notifications:queued`).
- Behavior: Publisher posts the outbox id as a GUID string to the channel; the subscriber pushes it to `INotificationIdQueue`, preserving the existing dispatcher path. When Mode=`channel`, no Redis client is created and behavior remains in‑process only.
- Queue: `IEmailQueue` + `EmailQueue` (in‑memory channel used by background dispatcher)
- Dispatcher (v1): `EmailDispatcherHostedService` renders and sends with retry/backoff; metrics/logging via OTEL
- Template renderer: `ScribanTemplateRenderer`
- Providers: `SmtpEmailSender` (dev), `SendGridEmailSender` (prod/real), `NoopEmailSender` fallback
- Enqueuer: `NotificationEnqueuer` helpers for verification and invite emails
- Resend (manual/bulk):
  - Manual: `POST /api/notifications/{id}/resend` (also dev variant) clones and enqueues a linked child, enforcing throttle via `(to_email, kind)` within `ResendThrottleWindow`.
  - Bulk: `POST /api/notifications/resend-bulk` filters by kind/date/recipients and enforces:
    - Per-request cap `Notifications:BulkResendMaxPerRequest` (default 100)
    - Per-tenant daily cap `Notifications:BulkResendPerTenantDailyCap` (default 500, rolling 24h)
    - Tenant scoping: non‑superadmin limited to current tenant; superadmin may filter by `tenantId`.
    - Throttle: per‑recipient pre‑check and outbox enforcement to avoid violating `ResendThrottleWindow`.
  - JSON: API accepts enum names in request bodies (global `JsonStringEnumConverter`), e.g., `{ "kind": "Verification" }`.
- PII hardening (Notif‑21):
  - Token hashing: verification/invite tokens are hashed (SHA‑256) and only the hash is stored on the outbox row (`TokenHash`); raw tokens are never persisted in Notification fields or `data_json`.
  - Pre‑rendered snapshots: subject/html/text may be pre‑rendered at enqueue time and stored; dispatcher reuses snapshots when present to avoid re‑render divergence.
  - Redacted logging: emails in logs are redacted (e.g., k\*\*\*@example.com) across SMTP/SendGrid providers and dispatcher paths.

PII scrubbing (Notif‑23):

- Early scrub of sensitive fields prior to deletion to minimize PII exposure time. A dedicated scrub pass nulls selected columns for notifications older than a scrub window but newer than the delete retention cutoff.
- Configuration (NotificationOptions):
  - Master switch `PiiScrubEnabled` (default true).
  - Scrub windows: `ScrubSentAfter`, `ScrubFailedAfter`, `ScrubDeadLetterAfter`.
  - Per‑field toggles: `ScrubToName`, `ScrubSubject`, `ScrubBodyHtml`, `ScrubBodyText`, and `ScrubToEmail` (email off by default).
- Observability: `NotificationsPurgeHostedService` logs `scrubbed` counts alongside purged counts each run.

Further reading:

- Privacy policy (engineering draft): devInfo/Sendgrid/privacyPolicy.md
- Vendor compliance and subprocessors: devInfo/Sendgrid/vendorCompliance.md

Outbox & Dispatcher (Notif‑13/14/15):

- Table `app.notifications` stores durable outbox entries (kind, to_email, data_json, dedupe_key, status, attempts, errors, timestamps; snapshots subject/html/text)
- Dispatcher `NotificationDispatcherHostedService` leases (`Queued`→`Sending`), renders, sends, and updates status with jittered backoff (0.5s/2s/8s +/-20%) and terminal `DeadLetter` on exhaustion; event‑driven via ID queue with polling fallback
- Testing note: EF InMemory provider does not support transactions; leasing logic gates transactional semantics behind `Database.IsRelational()` to keep tests stable while retaining transactions for relational providers.

Dedupe & Retention (Notif‑17/18):

- TTL dedupe table `app.notification_dedupes` (PK: dedupe_key, expires_at) is claimed before outbox insert; duplicate claims within TTL throw `DuplicateNotificationException`
- Partial unique index `ux_notifications_dedupe_key_active` applies only to in‑flight statuses (`Queued`,`Sending`); `Sent` dedupe is governed by the TTL table
- Hourly purge job removes expired dedupe claims and old notifications; retention windows configurable via `Notifications` options (e.g., Sent: 60d; Failed/Dead: 90d)
- Scrub‑then‑delete ordering (Notif‑23): For items within the scrub window but not yet at the deletion cutoff, the job nulls configured fields first; items past the deletion cutoff are removed entirely.

Dev endpoints:

- `POST /api/dev/notifications/verification` and `/invite` enqueue test emails; requires dev headers

Prod admin endpoints (Notif‑24):

- `GET /api/notifications` — list notifications
  - Non‑superadmin: tenant‑scoped using `tenant_id` claim; supports filters `status`, `kind`; paging via `take`/`skip`; `X-Total-Count` header.
  - Superadmin: cross‑tenant view allowed and may optionally filter by `tenantId`.
- `GET /api/notifications/{id}` — details
  - Non‑superadmin: 403 if the notification’s `tenant_id` doesn’t match current tenant.
  - Superadmin: allowed.
- `POST /api/notifications/{id}/retry` — retry Failed/DeadLetter
  - Transitions to `Queued` and nudges dispatcher; enforces same tenant/superadmin gating.

- `GET /api/notifications/{id}/resends` (Notif‑31)
  - Returns child resends for an original; ordered latest-first.
  - Paging via `take`/`skip`; sets `X‑Total‑Count`.
  - Tenant scoping as above; superadmin cross‑tenant allowed.

Access control:

- Non‑superadmin requests are auto‑scoped by current tenant (from `tenant_id` claim); cross‑tenant access is denied.
- Superadmin requests (claim `superadmin=true`) may access across tenants and use `tenantId` filter on list.

Provider webhooks:

- `POST /api/notifications/webhook/sendgrid` — receives SendGrid event webhooks; optional shared-secret via header. Normalizes and stores provider delivery status under `notifications.data_json.provider_status` along with event timestamp; designed to be idempotent for replayed events.

#### Field encryption (Notif-22)

#### DLQ and replay (Mig‑05)

- Endpoints (tenant-scoped with superadmin override):
  - `GET /api/notifications/dlq?status=Failed|DeadLetter&kind=...&tenantId=...&take=&skip=` — lists Failed and DeadLetter notifications (defaults to both) with paging and `X-Total-Count`.
  - `POST /api/notifications/dlq/replay` — body `{ ids?: Guid[], status?: Failed|DeadLetter, kind?: EmailKind, tenantId?: Guid, limit?: number }`; requeues selected items and publishes them via the active transport. Responds with `{ requeued, skippedForbidden, notFound, skippedInvalid, errors, ids }`.
- Behavior: enforces tenant scoping on both query and item level; only Failed/DeadLetter are eligible for replay. Uses `INotificationOutbox.TryRequeueAsync` and `INotificationTransport.PublishQueuedAsync` for idempotent handoff back to the dispatcher.

- Optional at-rest encryption for selected outbox fields: `to_name`, `subject` (optional), `body_html`, `body_text`.
- Format: `enc:v1:` prefix followed by Base64URL payload containing AES-GCM ciphertext + nonce + tag.
- Configuration (NotificationOptions):
  - `EncryptFields` (bool) — master switch; default false
  - `EncryptionKeyBase64` (string) — 256-bit key as base64; required when enabled
  - Per-field toggles: `EncryptToName`, `EncryptSubject`, `EncryptBodyHtml`, `EncryptBodyText`
- Runtime behavior:
  - Encrypt on write (`CreateQueuedAsync` and `MarkSentAsync`), decrypt on lease (`LeaseNextDueAsync`).
  - DI selects `AesGcmFieldCipher` when enabled+key is valid; otherwise `NullFieldCipher` (no-op) for backward compatibility.
- No schema migration required; ciphertext is stored in existing text columns. Downstream services receive plaintext via the lease path.

---

```

```
