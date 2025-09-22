# Appostolic — Architecture Snapshot (2025-09-22)

This document describes the structure, runtime, and conventions of the Appostolic monorepo. It’s organized to group related topics together for easier navigation and future updates.

## What’s new

- IAM — Final legacy role cleanup & test alignment (2025-09-20)
  - Auth/JWT — Dev Header Decommission Sprint (Phase A) In Progress (2025-09-22)
    - Added superadmin claim support to test-only mint endpoint (`POST /api/test/mint-tenant-token`) via `SuperAdmin` flag injecting `superadmin=true` into issued access tokens (neutral or tenant). Updated `TestAuthClient` and `AuthTestClient` helpers; migrated notifications production endpoints integration tests off development headers to pure Bearer JWT authentication (4/4 tests passing). Residual explicit `Dev` scheme references removed from those paths. Next phases will migrate remaining tests, add a guard prohibiting `x-dev-user`/`x-tenant` usage, and finally remove the Dev header authentication handler + composite scheme.
    - RDH Story 2 Phase A: Migrated `UserProfileEndpointsTests` off legacy `AuthTestClient.UseTenantAsync` mint helper to real password + `/api/auth/login` + `/api/auth/select-tenant` flow (password seeded via `IPasswordHasher`). Guard test now reports reduced remaining usages; next target `UserAvatarEndpointsTests`.
    - RDH Story 2 Phase A: Migrated invites test suite (`InvitesEndpointsTests`, `InvitesAcceptTests`, `InvitesRolesFlagsTests`, `LegacyRoleWritePathDeprecationTests`) off legacy mint helper to real auth flow; fixed DELETE invite endpoint to fall back to `ClaimTypes.NameIdentifier` when `sub` is absent (parity with listing endpoint) resolving 400 revoke regression under migrated tokens.
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

## Auth/JWT — Story 4 Refresh Cookie & Frontend In-Memory Access Token (2025-09-20)

- Feature flag `AUTH__REFRESH_COOKIE_ENABLED` (config or env) governs issuance of secure httpOnly refresh cookie `rt` (SameSite=Lax, Path=/, Secure except in Development) on `/api/auth/login`, `/api/auth/magic/consume`, and `/api/auth/select-tenant`.
- Rotation: `POST /api/auth/select-tenant` rotates the neutral refresh token (revokes old, issues new) and overwrites the cookie when enabled; tests assert old token reuse returns 401.
- Tests: `RefreshCookieTests` (green) cover initial issuance and rotation – header parsing normalized to be case‑insensitive (`httponly`).
- Frontend: Added `authClient.ts` for neutral access token kept only in memory (not persisted). `primeNeutralAccess` primes token post credential & magic login flows. `getAccessToken` (scaffold) will attempt refresh once available.
- Fetch wrapper `withAuthFetch` injects `Authorization: Bearer <access>` and sets `credentials: 'include'` so the refresh cookie accompanies requests when future refresh endpoint lands.
- Temporary internal Next.js route `/api/_auth/refresh-neutral` is a placeholder (explicit comments) and will be replaced by real `/api/auth/refresh` (Story 6). It is intentionally not part of standard user flows to avoid accidental production reliance.
- (Removed) Earlier temporary internal Next.js route `/api/_auth/refresh-neutral` (placeholder prior to `/api/auth/refresh`) has been fully removed (Story 8). All clients now rely on `/api/auth/refresh` + silent refresh loop; rollback guidance lives in `docs/auth-upgrade.md`.
- Security improvement: Moves refresh token out of JSON/localStorage and into httpOnly cookie (defense against XSS exfiltration). Access token remains short‑lived and ephemeral in JS memory only.
- Backwards compatibility: Current auth JSON still includes `refresh.token` for clients while flag incubation proceeds; removal planned post refresh endpoint rollout (grace window with dual support).
- Follow-ups (next stories):
  - Story 5 / 5a: Local HTTPS enablement + secure cookie enforcement hardening.
  - Story 6: General refresh endpoint returning a new access (and rotated refresh) token; remove JSON refresh after adoption.
  - Story 7+: Logout / global revocation improvements and observability counters (issuance / validation / failures).
  - Refactor: Consolidate duplicated cookie issuance blocks in endpoints into a small helper (deferred until refresh endpoint lands to reduce churn).

### What’s new

- Web: Tenant selector labels now derive from roles flags (Admin/Approver/Creator/Learner) instead of legacy Owner/Viewer strings. This uses `getFlagRoles` to normalize roles[] or legacy role names (case-insensitive) ensuring consistent UX across components (TopBar, TenantSwitcher, TenantSwitcherModal).
  - Tests updated to assert original mime and absolute URL; full API suite green.

- Nav — Multi-tenant explicit selection hardening (2025-09-17)
- Nav — Tenant‑scoped Admin gating regression fix (2025-09-17)
  - Fixed a leakage where `TopBar` could show the Admin menu based on a global `session.isAdmin` even when the user no longer has admin rights for the selected tenant. Admin visibility is now derived strictly from the membership that matches the currently selected tenant (`tenantSlug` or `tenantId`) with role `admin`. A regression unit test was added to lock this behavior.
  - Admin dropdown label updated from “Settings” to “Org Settings” (route unchanged: `/studio/admin/settings`).
  - Update (2025-09-17 later): `TopBar` now uses the shared roles helper `computeBooleansForTenant` so Admin visibility is driven by roles flags (e.g., `TenantAdmin`) or legacy roles (`Owner`/`Admin`). It also tolerates `session.tenant` being either a slug or an id by resolving to the membership’s slug before evaluation. Additional unit tests cover legacy Owner and tenantId matching.

  - Removed multi-tenant auto-selection heuristic in NextAuth `jwt` callback that previously picked an arbitrary/high-privilege membership and populated `token.tenant` on initial sign-in. Multi-membership users now always start with `tenant` unset until they explicitly select one via `/select-tenant` (which persists the `selected_tenant` cookie) or the tenant switcher modal. Single-membership users still auto-select for ergonomics.
  - Middleware no longer silently sets `selected_tenant` for multi-tenant users; it only auto-sets when exactly one membership exists. Authenticated requests lacking a valid selection are redirected to `/select-tenant`.
  - Server layout gating (`app/layout.tsx`) already required both a `selected_tenant` cookie and a matching session tenant claim; with the heuristic removed, the TopBar cannot appear prematurely for multi-tenant accounts.
  - Deprecated client `TenantAwareTopBar` wrapper fully removed/neutralized (tests replaced with server-gating tests). A stub file remains temporarily to avoid breaking deep imports; will be deleted after a repo-wide search confirms no external dependents.
  - Added regression tests: `auth.multiTenant.test.ts` (ensures no tenant claim post sign-in for multi-membership) and `layout.multiTenantNoSelection.test.tsx` (asserts nav absence without selection). Updated middleware tests to reflect stricter redirect behavior.
  - Rationale: Eliminate UI leakage of nav/actions prior to explicit tenant selection for multi-tenant users and close the gap where a heuristic could elevate unintended tenant context.

- Nav — Server-side TopBar gating (2025-09-16)
  - Replaced client `TenantAwareTopBar` wrapper with deterministic server-side gating in `app/layout.tsx` that renders `<TopBar />` only when the `selected_tenant` cookie is present. Eliminates hydration race/flash of navigation items for authenticated users who have not yet selected a tenant. Removed obsolete component + associated tests and updated `TopBar` to continue internal tenant-claim checks for nav/actions. Follow-up (optional): middleware redirect for authenticated requests lacking the cookie to force `/select-tenant`.
  - Hardened (later same day): Gating now also requires the server session tenant claim to match the cookie to avoid stale cookie nav leakage.

- User Profile — UPROF‑11: Denomination presets & multi-select guardrails UI (2025-09-16)
- Web Fix — Test alignment: Guardrails patch now root-level (no nested `profile` wrapper); bio preview soft line break test updated for `remark-breaks`; avatar upload test uses explicit button (2025-09-16)
- Web UX — Multi-tenant TopBar gating: Added `TenantAwareTopBar` wrapper to suppress navigation when user has multiple memberships but no active tenant selection (no cookie/session.tenant) until selection is made (2025-09-16)
- User Profile — UPROF‑05 Enhancement: Rich bio editor (MUI, GFM preview) & styled avatar upload (2025-09-16)
  - Web Fix — Bio editor diff patch & soft line breaks (2025-09-16)
    - `BioEditor` now tracks a saved baseline and submits minimal merge patches. Clearing sends `{"bio":null}`; unchanged content results in no network call. Adds `remark-breaks` to render single newlines as soft line breaks consistent with GitHub-flavored Markdown expectations. Tests updated to assert minimal patch emission, null clearing, soft line break preview, and avoidance of redundant saves.
- Web Fix — Profile name clearing semantics (2025-09-16)
  - `ProfileEditForm` now sends explicit `null` for name/contact/social fields that transition from non-empty to empty, enabling proper clearing under backend deep-merge logic. Previous behavior omitted the `name` object entirely, leaving stale values persisted. Diff-based patch builder prevents unnecessary writes and keeps no-op submissions empty.
- Replaced plain textarea bio editor with tabbed Write/Preview Markdown component using `react-markdown` + `remark-gfm`, MUI Tabs, and accessible character count + helper/error messaging. Maintains flat merge‑patch contract `{ bio: { format:'markdown', content } } | { bio:null }`.
- Refactored `AvatarUpload` to MUI (Avatar, Buttons, progress) adding file info, tooltips, and improved accessibility; preserves validation (png/jpeg/webp ≤2MB) and global `avatar-updated` CustomEvent with cache-busted URL.
- Added dependencies: `react-markdown`, `remark-gfm` to web; no server contract changes.
- Deferred: Slash commands, emoji picker, drag-drop image upload, markdown linting, and persisted draft autosave.
- Privacy & Observability — UPROF‑12 (12A–12G complete) PII hashing & redaction (2025-09-16)
  - Added `PrivacyOptions` (`Privacy:PIIHashPepper`, `Privacy:PIIHashingEnabled`) bound in `Program.cs`.
  - Introduced `IPIIHasher` with `Sha256PIIHasher` (email normalization: lowercase+trim; phone normalization: digits-only) computing peppered SHA-256 hex string; deterministic across processes sharing pepper.
  - Added `PIIRedactor` (email → `f***@domain`; phone → `***1234` last 4) centralizing masking; legacy `EmailRedactor` now obsolete shim delegating to `PIIRedactor`.
  - Logging enrichment helper `LoggingPIIScope` supplies structured scope fields: `user.email.redacted`, optional `user.email.hash`, `user.phone.redacted`, optional `user.phone.hash` when hashing enabled — no raw PII emitted.
  - Integrated scopes into auth/profile/tenant endpoints; verified via integration tests `UserProfileLoggingTests` capturing structured scopes for `GET /api/users/me` (hash present only when enabled, raw email absent).
  - Unit tests: hashing determinism (same input → same hash), pepper variance (different pepper → different hash), normalization (email casing/whitespace, phone punctuation), redaction edge cases (short local parts, short phone numbers), and logging scope toggle behavior.
  - Outcome: Full API test suite now 175/175 with privacy coverage.
  - Pending (12I): Optional OpenTelemetry enrichment — span attributes `user.email.redacted` and `user.email.hash` (when enabled) behind config flag; ensure attributes excluded when hashing disabled to minimize cardinality. Will add focused test validating attribute presence/absence.
  - Rationale: Privacy-by-default observability (correlate users across logs/traces without storing raw identifiers) and a single seam for future expansion (additional PII types, irreversible tokens).

- API: Added authenticated metadata endpoint `GET /api/metadata/denominations` returning `{ presets: Array<{ id, name, notes? }> }` sourced from a static JSON file (`apps/api/App/Data/denominations.json`). Serves 10 curated baseline presets (e.g., Baptist, Anglican, Mere Christianity) with future migration path to a DB table + versioning.
- Web: Extended `/profile` server page to best‑effort fetch presets and pass them to `ProfileGuardrailsForm`. Added searchable multi-select picker (chips) allowing users to add/remove denominations; on first add, if `guardrails.denominationAlignment` is empty it auto-fills with the preset display name (never overwrites subsequent manual edits).
- Patch semantics: Introduced `profile.presets.denominations: string[]`. Submission always sends the full array (`profile.presets.denominations`) so arrays remain deterministic replace operations under existing deep-merge JSONB logic; clearing = empty array. Alignment field remains an independent freeform string.
- Tests: API integration tests `DenominationsMetadataTests` cover 401 unauthenticated and 200 shape; web tests extend `ProfileGuardrailsForm.test.tsx` with selection, auto-fill, non-overwrite, removal, and patch body assertion (now 46 files / 142 tests total web suite, coverage ~84% lines).
- Accessibility: Search input labeled, add/remove buttons use descriptive `aria-label`s, helper text explains auto-fill behavior; chips use `aria-label` for the readable name.
- Deferred: Versioned presets with revision/deprecation flags; tenant-level preset overrides; primary/ordering metadata; faceted grouping (family/tradition); server-side validation rejecting unknown IDs (400) with error details; ETag/If-None-Match caching + CDN headers; analytics on co-occurrence and selection trends; preset change notifications.
- Rationale: Static JSON keeps iteration fast; moving to DB later is non-breaking (endpoint contract stable). Always-sent array avoids accidental partial merge anomalies and simplifies diff reasoning.

- User Profile — UPROF‑08: Change password UI enhancements (2025-09-16)
  - Web: Refactored `/change-password` page to use new proxy `POST /api-proxy/users/me/password` aligning with API `/api/users/me/password`. Added confirm field, client strength heuristic meter (length + class variety), inline mismatch prevention, accessible live region for status. Maps 400 (incorrect current) and 422 (weak new password) to targeted inline errors; other failures show generic retry.
  - Tests: Added `ChangePasswordPage.test.tsx` covering mismatch prevention, weak password block, incorrect current (400) handling, and success (204) flow.
  - Deferred: Stronger entropy scoring via zxcvbn/passphrase lib and configurable backend policy.

- User Profile — UPROF‑06: Guardrails & preferences form (2025-09-16)
  - Web: Added `ProfileGuardrailsForm` collecting authors/books allowlists, instructional notes, and preferred lesson format. Chip input UX replaces arrays entirely (consistent with server deep merge array replacement semantics). Only changed guardrails/preferences paths are included in JSON merge patch submission.
  - Tests: `ProfileGuardrailsForm.test.tsx` validates add/remove chips, empty submission no-op, and successful submit.
  - Deferred: Denominational presets & advanced policy validation (planned UPROF‑11).

- User Profile — UPROF‑05: Profile page (personal & social editing) (2025-09-16)
  - Web: Introduced `/profile` server page that fetches via `GET /api-proxy/users/me` and renders `ProfileView` plus `ProfileEditForm` for personal/social fields (name, location, social links). Form constructs minimal JSON merge patch (omits untouched) and submits through `PUT /api-proxy/users/me` leveraging existing deep merge semantics.
  - Tests: `ProfileEditForm.test.tsx` ensures untouched field omission, success state, and error mapping.
  - Deferred: Rich validation (phone normalization, strict URL schema filtering, timezone selection).

- Tenant Settings & Branding — TEN-01/TEN-02 (2025-09-16)
  - API: Added `GET /api/tenants/settings` and `PUT /api/tenants/settings` (merge semantics: objects deep-merge; arrays/scalars replace; explicit nulls clear) storing JSONB in `tenants.settings`.
  - API: Added `POST /api/tenants/logo` (multipart image/png|jpeg|webp <=2MB) storing via `IObjectStorageService` under `tenants/{tenantId}/logo.*` and updating `settings.branding.logo = { url, key, mime }`.
  - API: Added `DELETE /api/tenants/logo` removing `settings.branding.logo` and best-effort deleting the underlying object. Leaves `branding` removed if empty.
  - Tests: Integration test suite `TenantSettingsEndpointsTests` covers GET/PUT settings merge, logo upload success, invalid mime (415), payload too large (413), and delete path cleanup (6 tests passing).
  - Notes: Width/height fields intentionally deferred pending future image processing (resizing & metadata extraction) story; code comments reference this. DeepMerge duplicated from user profile endpoints pending small refactor to a shared helper.

- User Profile — UPROF‑09: S3/MinIO object storage seam (2025-09-16)
  - Added `S3ObjectStorageService` implementing `IObjectStorageService` using `AWSSDK.S3` with support for MinIO in development (path‑style) and AWS S3 in production (virtual host style).
  - Configuration: `Storage:Mode` = `local` (default) or `s3`. When `s3`, options bound from `Storage:S3` section: `Bucket` (required), `PublicBaseUrl` (optional CDN/base URL override), `RegionEndpoint`, `DefaultCacheControl` (defaults to `public, max-age=31536000, immutable`), `PathStyle` (bool, defaults true for MinIO), `AccessKey`, `SecretKey`, and `ServiceURL` (endpoint override e.g. `http://localhost:9000`).
  - DI wiring in `Program.cs` selects S3 vs local at startup; local mode continues to write under `apps/web/web.out/media` and serve via `/media/*` static files. S3 mode sets `S3CannedACL.PublicRead` (public avatars/logos) and applies the configured Cache-Control.
  - URL generation: if `PublicBaseUrl` configured, returned object URL = `<PublicBaseUrl>/<key>`; otherwise falls back to `https://<bucket>.s3.<region>.amazonaws.com/<key>` (best effort). Keys are normalized path segments (no backslashes).
  - Tests: Added unit tests (`S3ObjectStorageServiceTests`) mocking `IAmazonS3` to assert ACL, Cache-Control header, key integrity, and URL fallback behavior. Existing avatar endpoint tests remain green using local mode.
  - Rationale: Establish a production‑ready seam for future tenant logo and lesson artifact storage without changing current endpoint contracts. Signed URLs and deletion lifecycle deferred to a later story.

- User Profile — UPROF‑07: Web avatar upload UX & cache-bust (2025-09-16)
  - Web: Added client-side `AvatarUpload` tests (validation: mime/size; successful upload triggers event). Component now dispatches a global `avatar-updated` CustomEvent with a cache-busted URL instead of forcing a full page reload.
  - `ProfileMenu` now displays the current avatar (falling back to icon) and listens for `avatar-updated` to swap the image source live without navigation. Added link to `/profile` replacing placeholder alert.
  - Coverage: Removed temporary exclusion for `AvatarUpload.tsx`; suite updated (122 tests) with lines coverage >84% overall and >90% for AvatarUpload. Cache-busting uses `?v=timestamp` appended to returned avatar URL.
  - Remaining storage story (UPROF‑09) will replace local filesystem implementation with MinIO/S3 provider while preserving event-driven refresh.

- Auth/JWT — Story 5b Real HTTPS Secure Refresh Cookie E2E Harness (2025-09-20)
  - Added dedicated `apps/api.e2e` test project with an in‑process Kestrel host fixture (`E2EHostFixture`) that stands up a minimal HTTPS server using a self‑signed ECDSA P‑256 certificate (generated at runtime) and explicit `Kestrel.ListenLocalhost(port).UseHttps(cert)` binding. This bypasses `TestServer` (which does not perform real TLS handshakes and always reports `Request.IsHttps = false`), enabling deterministic validation of transport‑dependent cookie attributes (Secure).
  - Introduced helper endpoint `GET /e2e/issue-cookie` (test‑only surface inside the harness host, not part of production API) that issues a refresh cookie `rt` with `HttpOnly`, `SameSite=Lax`, `Path=/`, `Secure = Request.IsHttps`, and a 30‑day `Expires`. The E2E test `SecureRefreshCookieTests` asserts presence of the `rt` cookie plus case‑insensitive attributes: `secure`, `httponly`, `samesite=lax`, `path=/`, and future expiry (>10m ahead).
  - Pivot rationale: Initial attempt spawned the full API process with an InMemory EF path guarded by `E2E_INMEM_DB`; startup synchronization proved brittle (timeouts waiting for readiness while DB dependencies resolved). Replaced with lean in‑process host isolating only the concern under test (TLS + Set‑Cookie semantics) for faster, deterministic execution and zero database dependency.
  - Implementation notes: Self‑signed cert built via `CertificateRequest` + SAN `localhost`; custom `HttpClientHandler` disables certificate validation for test client only. Attributes asserted using lowercase normalization to tolerate server casing differences. Harness logs base address on start (`[E2E] Listening https://localhost:{port}`).
  - Follow‑ups: Optionally evolve harness to exercise real auth flows (login/magic) once general refresh endpoint (Story 6) lands; consider moving helper issuance endpoint behind the existing auth pipelines with a test‑only compile symbol.
  - Quality gates: New project compiles; test passes (1/1). No changes to production `Program.cs`; risk isolated to new test assembly.

## Testing Layers

The solution now uses a tiered testing strategy:

1. Unit & Integration (apps/api.tests)

- Uses `WebApplicationFactory` + in‑memory configuration & real Postgres (or test container) abstractions; asserts business logic, persistence, and auth flows without real TLS. Cookie Secure attribute previously simulated via header overrides prior to Story 5b.

2. E2E Transport (apps/api.e2e)

- Purpose-built minimal Kestrel HTTPS host (self‑signed cert) for transport/security attributes that `TestServer` cannot validate (e.g., `Request.IsHttps` dependent cookies). Avoids full DB stack; issues deterministic test cookie.

3. Web (apps/web)

- Vitest + Playwright for component, server action, and navigation behaviors under Next.js runtime.

4. Workers (notifications, rendering) — current coverage via targeted unit/integration tests; future E2E pipeline tests planned post refresh/auth hardening.

Rationale: Separating the HTTPS cookie attribute validation into its own minimal layer keeps the primary integration suite fast/stable while still achieving true Secure flag verification under a real TLS handshake.

## Auth Flow (Final JWT Rollout Summary)

The finalized JWT authentication architecture (Stories 1–9 complete; Story 10 docs) is represented in `docs/diagrams/auth-flow.mmd` (Mermaid). Key characteristics:

- Neutral access token (short‑lived) + persisted hashed refresh token issued at login/magic consume.
- Secure httpOnly cookie `rt` (SameSite=Lax; Secure over HTTPS) transports refresh token; plaintext emission suppressed by default (`AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT=false`).
- Rotation pattern: refresh endpoint and tenant selection revoke old refresh before issuing new (single active chain); reuse yields 401 `refresh_reuse`.
- TokenVersion claim `v` enables instant global revocation (logout-all/password change) without blacklist.
- Silent refresh loop (frontend) calls `/api/auth/refresh` ~60s before expiry, retry-once on 401, updating in-memory access token only.
- Logout endpoints: single (revokes one refresh) and all (revokes all + TokenVersion++).
- Observability: OpenTelemetry Meter `Appostolic.Auth` counters + histograms (see metrics taxonomy in upgrade guide Section 7).
- Transitional flags & phases documented in `docs/auth-upgrade.md` (Section 3 & 4) governing body path deprecation & plaintext suppression.

Forward Work (not in this sprint): Dev Header Decommission (RDH) will physically remove `AUTH__ALLOW_DEV_HEADERS` and composite scheme; multi-key signing & session enumeration will follow post‑1.0.

- Web Tooling — Vitest Node 20 requirement (2025-09-16)
  - Added explicit guidance in `apps/web/AGENTS.md` to always run Vitest and Next dev tasks under Node 20.x LTS. Running under Node 19 triggered a Corepack failure (`TypeError: URL.canParse is not a function`) before tests executed. CI and local docs now mandate Node 20 to avoid the crash; sample `nvm`/PATH override commands documented.

- Nav Debug — Temporary user/tenant labels (2025-09-17)
  - To investigate potential cross-tenant cookie persistence, the TopBar now surfaces the current user's email (left of the avatar) and the selected tenant slug beneath the Appostolic brand. A previously added temporary JavaScript alert on `/logout` has been removed after diagnosis.
  - Admin UX: Added a Tenant Settings link for admins in the TopBar (desktop Admin menu and mobile drawer) pointing to `/studio/admin/settings`. The page is server‑gated (TenantAdmin) and currently renders a placeholder.

- User Profile — UPROF‑04: POST /api/users/me/avatar (2025-09-16)
  - API: Added avatar upload endpoint that accepts multipart/form-data, validates image type (png/jpeg/webp) and size (<=2MB), stores via a new storage abstraction `IObjectStorageService` with a local filesystem implementation. Files are saved under `users/{userId}/avatar.*` and exposed through static file hosting at `/media/*`. The endpoint updates `users.profile.avatar` to `{ url, key, mime }` and returns the avatar metadata.
  - Runtime wiring: Registered `IObjectStorageService` → `LocalFileStorageService` in `Program.cs` and added static file mapping for `/media` pointing at `apps/web/web.out/media` by default. This provides stable relative URLs in dev/test without external object storage.
  - Testing: Integration tests cover success (200), unsupported media type (415), and payload too large (413). Full API suite PASS (148/148).

- User Profile — UPROF‑02: GET/PUT /api/users/me (2025-09-16)
  - API: Implemented current user profile endpoints. PUT performs a server-side deep merge into `users.profile` (objects merged; arrays/scalars replace) with normalization (trim strings) and basic social URL validation (invalid URLs dropped). EF update uses AsNoTracking + Attach with property-level modification to avoid double-tracking record types. Tests added; full API suite PASS (142/142).
  - Testing: Introduced provider-aware `JsonDocument` converters for EF InMemory in Program startup to enable reliable JsonB-like behavior under tests.

- User Profile — UPROF‑03: POST /api/users/me/password (2025-09-16)
  - API: Added change-password endpoint that verifies the current password and updates the hash/salt using Argon2id. Returns 204 on success, 400 on invalid current, and 422 on weak password (MVP strength: >=8 chars, includes letter+digit). EF update uses AsNoTracking + Attach with property-level modification. Integration tests added; full API suite PASS (145/145).

- Auth — Root route auth gate (2025-09-16)
  - Web: The root page (`/app/page.tsx`) is now a server-only redirector. Unauthenticated users are redirected to `/login`; authenticated users are redirected to `/studio` (which further redirects to `/studio/agents`). This prevents the dashboard UI from rendering to unauthenticated users. A unit test was updated to assert redirect behavior by mocking `next-auth` and `next/navigation`.
- Auth — Signup page styling (2025-09-16)
  - Web: `/signup` received a CSS module with consistent layout, accessible labels/helper text, an invite-aware banner that links to Login (`/login?next=/invite/accept?token=...`) when an invite token is present, and a clear primary action. ARIA warnings were resolved in the client form.

- Admin UX — Tenant switcher centering + Invites Accepted state (2025-09-16)
  - Web: Centered `TenantSwitcherModal` and prevented cut‑off by using a full‑screen flex container with `items-center justify-center` and making the dialog panel scrollable via `max-h` + `overflow-auto`.
- Web — Dev logs cleanup (2025-09-16)
  - Toaster is now SSR-safe: the portal to `document.body` is created only after the component mounts to avoid “document is not defined” during server rendering. Removed a duplicate `middleware.js` that caused Next.js to warn about duplicate middleware registrations (we keep `middleware.ts`).
- Auth Flows: Forgot Password styled with accessible form and status messaging; Reset Password now reads token from URL (hidden field), adds confirm password with validation, and provides clear success/error feedback.
  - Web: `/studio/admin/invites` now surfaces acceptance state from the API. The table shows a Status chip: Accepted (green) when `acceptedAt` is set, Pending (amber) otherwise. When an invite has been accepted, the Resend/Revoke actions are hidden to avoid invalid operations. Also fixed a broken `ConfirmSubmitButton` import and restored the Expires column cell to match the header.

- Auth — Login redirect loop fix (2025-09-16)
  - Web Login now lets NextAuth perform the post‑sign‑in redirect instead of manually calling `router.replace` with `redirect: false`. This removes a race where middleware still sees no session cookie and bounces back to `/login`, causing repeated `/api/auth/csrf` requests. The login page also fetches the CSRF token once per mount and surfaces friendly error text for `?error=CredentialsSignin`.

- Admin Invites — roles flags + HTML email (2025-09-16)
  - Web: `/studio/admin/invites` now uses granular roles flags (TenantAdmin, Approver, Creator, Learner) in the dropdown and sends `{ email, roles: [...] }` to align with the API’s `invitations.roles` column. Server actions redirect with `?ok=` only on success to avoid false error toasts.
  - API: Invite create/resend emails sent via Mailhog now use a small HTML body with an Accept link, tenant name, selected role, and expiry; `IsBodyHtml = true` set for better dev readability.

- Web — Studio: Tasks ergonomics (Completed)
- Agents: Editor form polished (a11y helper texts, inline validation, clear isEnabled toggle) and test added for isEnabled payload.
  - Task detail and inbox now include copy-to-clipboard for Task IDs (detail header and table ID column).
  - Export action guarantees a predictable filename fallback `task-<id>.json` when Content-Disposition is absent.
  - Tests cover retry/cancel, export filename fallback, and copy actions; full web suite green.

- Web — Admin: Audits UI polish (Completed)
  - `/studio/admin/audits` now has a more complete UX: quick date presets (Today, 7/30 days), styled filter form, formatted table with role flag decoding to names, a compact pager that reads `X-Total-Count`, and clear empty/error states.
  - Server page accepts defaulted `searchParams` to support test invocation without args. Tests added to assert: non-admin 403 render, pager text computed from `X-Total-Count`, and Prev/Next link query sync.
  - Navigation note: Admin links (Members, Invites, Audits, Notifications) are visible in the desktop TopBar for admins; mobile drawer remains role-aware. Existing guard logic remains server-first.

- Web — Admin: Invites UX polish (phase 1)
  - `/studio/admin/invites` now shows status banners based on `ok`/`err` query parameters after server actions (create/resend/revoke). Revoke uses a small client helper `ConfirmSubmitButton` to ask for confirmation before submitting the server-action form, avoiding accidental revocations while keeping server-first behavior.
  - Tests extended to cover banner rendering and error fallback when invites fetch fails. Next phase will add toast notifications, empty/validation states, and an accessible confirm dialog.

- Web — Admin: Invites UX polish (phase 2)
  - Added a lightweight global toast system and switched `/studio/admin/invites` from SSR banners to client toasts derived from `ok/err` query parameters (then stripped from the URL). Replaced `window.confirm` with an accessible modal `ConfirmDialog` used by `ConfirmSubmitButton`.
  - Added an empty state when no invites exist and introduced a validated `EmailField` client component that announces inline errors using aria-invalid/aria-describedby and role=alert after first blur.

- Web — Admin: Members roles UX polish
  - `/studio/admin/members` now provides save feedback via client toasts triggered from `ok/err` query parameters after server action redirects. Checkboxes expose `data-pending` during submission, and the last-admin safety rule is surfaced with an accessible helper message using `aria-describedby`.
  - Tests updated and hardened by adding `useToastOptional()` (a no-throw hook for isolated renders) and wrapping the shared test Providers with `ToastProvider`.

- Docs — Frontend ↔ Backend Parity Sprint Plan (Added)
  - Introduced `devInfo/FrontendStuff/paritySprintPlan.md`, a concrete plan to align the Next.js web UI with existing backend capabilities. It inventories Admin (Members, Invites, Audits, Notifications), Studio (Agents, Agent Tasks), and Auth/Tenant flows, defines stories with acceptance criteria, quality gates (typecheck, tests, lint, a11y), and a phased timeline.

- Web — Navigation Sprint: A11y & theming polish (Completed)
  - TopBar now gains a subtle elevation (shadow) when the page is scrolled; preserves sticky behavior and accessibility. Mobile hamburger has a clear `aria-label`; desktop nav landmark is named; `aria-expanded` values are proper booleans; dialog semantics affirmed for `NavDrawer` and `TenantSwitcherModal` with focus handling.
  - Tests extended to validate elevation toggle and accessible labels; full web test suite remains green with coverage ~91.5% lines.

- Web — Navigation Sprint: Admin section (Role-gated) (Completed)
  - Added `/studio/admin/invites` page guarded server-side: unauthenticated users redirect to `/login`, a selected tenant is required, and non-admins receive RFC7807 403. Page lists invites through the existing `/api-proxy/tenants/[tenantId]/invites` route and supports server actions to create, resend, and revoke invites.
  - Updated nav: Admin section now includes Members, Invites, Audits, and Notifications (DLQ) in `TopBar` and `NavDrawer`. UI mirrors server roles (TenantAdmin) while authorization remains server-first.
  - Tests: unit tests cover unauth redirect, 403 for non-admin, and admin render; full web suite passes with coverage ~91% lines.

- Web — Navigation Sprint: Profile menu and tenant switching (Completed)
  - Added a lightweight `ProfileMenu` in the web header with Superadmin chip, a Switch tenant action, and Sign out. The switch action opens `TenantSwitcherModal`, which lists memberships from the NextAuth session, updates the session via `session.update({ tenant })`, persists the `selected_tenant` cookie via `POST /api/tenant/select`, and triggers a client refresh (`router.refresh()`).
  - Integrated `ProfileMenu` into `TopBar` alongside existing CTAs and `ThemeToggle`. The modal supports backdrop/ESC close and restores focus to the trigger.
  - Tests cover dropdown toggle, superadmin visibility, modal open/close, and update flow; full web suite remains green with coverage >90% lines.

- Web — Auth/Tenant: Multi-tenant UX polish (Completed)
  - `/select-tenant` now validates same-origin `next` and auto-selects when a single membership exists; added tests for safe/unsafe `next` values.
  - Tenant switcher modal shows role badges and hints the last selected tenant from localStorage for quick re-selection; cookie remains authoritative for server reads.

- Web — Various FE cleanup (auth/nav/login)
  - TopBar hides primary nav when logged out (shows Sign in). Login page styled and now links to Sign up and Magic Link (both preserve `next`). Members page includes an "Invite members" link. Mobile drawer backdrop made more opaque; Tenant Switcher modal alignment improved. Tests updated; full web suite green.

- IAM — Sprint 4.1: Seeds + dev roles utility (Completed)
  - Seeded baseline users with distinct Roles per tenant (Admin, Approver, Creator, Learner) via an idempotent seed that converges memberships and augments Owner composite flags when needed.
  - Added developer utility endpoint `POST /api/dev/grant-roles` that accepts `tenantId` or `tenantSlug`, `email`, and `roles[]` (case-insensitive names). It auto-creates the user if missing, ensures membership, and sets role flags.
  - Guard: when configuration `Dev:GrantRolesKey` is present, `POST /api/dev/grant-roles` requires header `x-dev-grant-key` with the same value; otherwise it’s open in dev/test.
  - Standardized the “pencil” model for roles: `Membership.Roles` is now mutable with a new method `ApplyRoleChange(...)` that updates flags and returns an `Audit` entry when a change occurs; the dev endpoint now persists that audit.
  - Routing reliability: removed earlier environment-gated mapping that hid dev routes under tests; temporary endpoint-enumeration diagnostics removed after validation.
  - Verification: Full API test suite PASS (138/138) after changes; added an explicit test asserting that updating an existing membership’s roles writes an `Audit` with correct `OldRoles`/`NewRoles`.

<a id="roles-matrix"></a>

### Roles → Capabilities matrix (tenant-scoped)

- TenantAdmin:
  - Manage memberships (list, invite, resend, delete, set roles)
  - Access audits listing for the tenant
  - Access admin pages in web (e.g., Studio members)
- Approver:
  - Approve/publish content (future endpoints; policy present)
- Creator:
  - Create/edit content (e.g., lessons; agents CRUD)
- Learner:
  - Read-only access to learning content

<a id="dev-grant-roles-guard"></a>

### Dev grant-roles guard

- Endpoint: `POST /api/dev/grant-roles`
- Behavior: When configuration `Dev:GrantRolesKey` is present, requests must include header `x-dev-grant-key` with the same value. When not set, the endpoint is available by default in dev/test.

- IAM — Sprint 3.3: Audit trails for membership role changes (Completed)
  - Added `app.audits` table to persist role change events with: id, tenant_id, user_id, changed_by_user_id, changed_by_email, old_roles (int), new_roles (int), changed_at (utc).
  - Endpoint `POST /api/tenants/{tenantId}/memberships/{userId}/roles` writes an audit after successful updates (works for EF InMemory and relational providers). Indexed by `(tenant_id, changed_at)` for efficient per-tenant queries.
  - New admin endpoint `GET /api/tenants/{tenantId}/audits` lists recent audit entries for the tenant. Requires TenantAdmin, validates tenant claim vs. route, supports `take`/`skip` paging with `ChangedAt DESC` ordering, optional filters `userId`, `changedByUserId`, `from`, `to`, and sets `X-Total-Count` header.
  - Migration `20250915145000_s4_02_membership_roles_audits` applied; database is up to date (`make migrate`).
  - Migration `20250915173000_s4_03_audits_view` creates SQL view `app.vw_audits_recent` as a convenience for reporting. Applied via `make migrate`.
  - Web surfacing: Added proxy `GET /api-proxy/tenants/{tenantId}/audits` with TenantAdmin guard and header forwarding; Studio page `/studio/admin/audits` lists audits with basic filters and paging (reads `X-Total-Count`).
  - Hardening: Added vitest proxy route test (`apps/web/app/api-proxy/tenants/[tenantId]/audits/route.test.ts`) asserting TenantAdmin guard (403 on non-admin) and preservation of `X-Total-Count` header on success.
  - Hardening (post‑baseline): Manual GUID format validation for `userId` / `changedByUserId` query parameters added in audits listing endpoint to return 400 early on malformed GUID strings (prevents unnecessary DB query construction). UI now decodes numeric role flag bitmasks (TenantAdmin|Approver|Creator|Learner) into human‑readable lists in the audits table via `roleNamesFromFlags`; tests updated to assert 400 behavior for malformed GUID filters.

- IAM — Sprint 2.2: Invites include Roles (Completed)
  - Invitation model now captures granular Roles flags in addition to the legacy Role. Invite creation accepts an optional array of flag names and returns both roles (string) and rolesValue (int). When omitted, flags are derived from the legacy Role for backward compatibility (Owner/Admin → Admin+Approver+Creator+Learner; Editor → Creator+Learner; Viewer → Learner).
  - Accepting an invite creates the membership with both the legacy Role and the Roles flags from the invitation. Signup flows that consume an invite token also propagate Roles; personal-tenant creation derives Owner flags by default. Last‑admin invariant enforcement remains in place.
  - Tests cover flag parsing/validation (400 on invalid), persistence/listing of roles, and acceptance behavior.

- Web — IAM Sprint 2.4: Membership admin page (Completed)
  - Added server proxies to surface assignment APIs: `GET /api-proxy/tenants/{tenantId}/memberships` and `POST /api-proxy/tenants/{tenantId}/memberships/{userId}/roles`. Both are guarded server-side (Owner/Admin) via `guardProxyRole` and forward dev headers via `buildProxyHeaders`.
  - New page `/studio/admin/members` lists members for the selected tenant and exposes checkboxes for flags: TenantAdmin, Approver, Creator, Learner. The UI disables unchecking the last remaining TenantAdmin for safety; the API remains the source of truth and enforces the invariant.
  - Tests cover proxy handlers (guard and forwarding); page-level render tests are planned alongside route gating in Sprint 3.

- Web — IAM Sprint 2.3: Roles‑aware session (Completed)
  - NextAuth JWT/session now includes memberships with Roles flags when available and derives convenience booleans for the selected tenant: isAdmin, canApprove, canCreate, isLearner. A helpers module `apps/web/src/lib/roles.ts` normalizes flags from legacy roles when flags are absent.
  - Added a dev diagnostics endpoint `GET /api/debug/session` that returns the effective session, the `selected_tenant` cookie, and the derived booleans/roles for quick verification.
  - Unit tests cover the roles helpers and the session callback derivation, including tenant switching via `session.update({ tenant })`.

- IAM — Sprint 2.1: Membership assignment APIs (Completed)
  - Added GET /api/tenants/{tenantId}/memberships to list memberships including legacy Role and Roles flags (names and numeric value). Requires TenantAdmin and ensures the `tenant_id` claim matches the route.
  - Added POST /api/tenants/{tenantId}/memberships/{userId}/roles to replace Roles flags using an array of enum names (case-insensitive). Returns 200 on change with a roles summary, 204 on no-op, 400 on invalid names, and 404 when membership is missing. Enforces the “at least one TenantAdmin per tenant” invariant across both legacy Role and Roles flags and returns 409 Conflict when violated.
    - Auditability: The roles update endpoint writes an audit row capturing tenant, target user, changer identity, old/new flags, and timestamp in `app.audits`.
  - 403 responses are formatted as RFC7807 ProblemDetails via the custom authorization result handler; the RoleAuthorizationHandler maps legacy Role to flags for compatibility during transition.

- IAM — Sprint 1.3: Role policies and uniform 403s (Completed)
  - Added policy-based authorization: TenantAdmin, Approver, Creator, Learner. Applied to critical endpoints in V1 (Creator on lesson creation; TenantAdmin on members/invites management). Legacy `MembershipRole` is mapped to `Roles` flags in the auth handler for compatibility.
  - Introduced a custom authorization result handler to return RFC7807 ProblemDetails on Forbidden, with extensions including tenantId and requiredRoles. Added a small fallback middleware to cover manual `Forbid()` responses.

- IAM — Sprint 1.4: TenantAdmin invariant (Completed)
  - Enforced invariant that every tenant must always have at least one TenantAdmin. Membership admin endpoints now return 409 Conflict when a role change or deletion would leave zero TenantAdmins (Owner/Admin). Owner-only demotion restrictions removed; invariant governs behavior. Tests updated to confirm last-admin protection and allow demotion when another admin exists.

- IAM — Sprint 1.1: Membership Roles flags (Completed)
  - Added hardcoded tenant-scoped Roles as a [Flags] enum: TenantAdmin, Approver, Creator, Learner (None=0).
  - Extended `Membership` with a `roles` column (int) to store role flags per user per tenant.
  - EF Core migration: `20250915130937_s4_01_membership_roles_flags` adds `roles integer not null default 0` to `app.memberships`.
  - (Historical) Legacy `MembershipRole` enum (Owner/Admin/Editor/Viewer) existed for backward compatibility during transition; fully removed 2025-09-20 alongside deletion of obsolete convergence test.
  - 2025-09-19 Update: Major API test suites (authorization, members list, members management) ran exclusively on `Roles` flags; legacy enum usage in tests phased out ahead of removal.
  - No behavior change yet; enforcement and APIs will land in subsequent stories.

- Mig‑07: Transport privacy hardening — Redis subscriber no longer logs raw Pub/Sub payloads on warning/error paths; logs include channel and payload length only. Publisher and subscriber continue to send/accept GUID IDs only; no PII is present in transport payloads.
- Mig‑03b: External notifications worker — introduced `apps/notifications-worker` which hosts the notifications runtime out-of-process. Added `NotificationsRuntimeOptions` to gate hosted services (dispatchers/purge/auto‑resend) so the API can disable dispatch when the worker runs. Default behavior remains unchanged.
- Mig‑05: DLQ and replay tooling — added admin endpoints to list DLQ (`GET /api/notifications/dlq`) and bulk replay (`POST /api/notifications/dlq/replay`) with tenant scoping and summaries.
- Mig‑06: Web DLQ admin — Studio page adds pagination, status/kind filters, and per‑row replay; proxy tests cover guard/forwarding and X‑Total‑Count propagation.
- Mig‑08: Rollout plan & fallback docs — RUNBOOK now includes a staged rollout for Redis transport + external worker with instant rollback to Channel transport. Default remains Channel.
- Notif‑29: Bulk resend endpoint `/api/notifications/resend-bulk` with per-request and per-tenant daily caps, tenant scoping, and per-recipient throttling. Also enabled JSON string↔︎enum serialization globally so request bodies may use enum names.
- Notif‑30: Resend telemetry and policy surfacing — metrics `email.resend.*` and bulk header `X‑Resend‑Remaining` to expose remaining per-tenant daily cap.
- Notif‑31: Resend history endpoint `GET /api/notifications/{id}/resends` with paging (`take`/`skip`), `X‑Total‑Count` header, and tenant/superadmin scoping.
- Notif‑32: Automated resend service — background scanner detects "no‑action" originals (Sent and older than a window) and creates linked resends under caps/throttle. Feature‑gated via `Notifications:EnableAutoResend`.
- Auth‑12: API integration tests expanded for core auth flows and security contracts. Added Members list tests (Admin/Owner allowed; Viewer 403; Unauth 401/403) and confirmed signup/login/invites coverage. Full suite passing (119/119).
- Auth‑13: Web tests for Sign‑up, Invite acceptance, Two‑stage tenant selection, and Header tenant switcher added. Web suite passing (18/18 files; 45 tests). Coverage ~92% lines.
- Auth‑14: Documentation updates — README gains an Authentication (dev) section; RUNBOOK adds an "Authentication flows (operations)" run guide. See README “Authentication (dev)” and RUNBOOK “Authentication flows (operations)”.
- Auth‑ML: Magic Link (passwordless) — added endpoints `POST /api/auth/magic/request` and `POST /api/auth/magic/consume`; new DB table `app.login_tokens` (SHA‑256 token hashes, single‑use, 15‑minute TTL); Magic Link email templates and `NotificationEnqueuer.QueueMagicLinkAsync`; web proxy routes `/api-proxy/auth/magic/{request|consume}` and public pages `/magic/request` and `/magic/verify`. Dev email continues to use Mailhog (SMTP).
- Web: Server-side absolute URL helper (`apps/web/app/lib/serverFetch.ts`) now uses `x-forwarded-host`/`x-forwarded-proto` (or `NEXT_PUBLIC_WEB_BASE`) to build absolute URLs for internal `/api-proxy/*` calls. Server pages were refactored to use `fetchFromProxy(...)`, fixing “Failed to parse URL from /api-proxy/…” errors in server components.
- Auth‑15: Signup and tenant‑selection hardening — added same‑origin proxy `POST /api-proxy/auth/signup` to avoid browser CORS; introduced `GET /api/tenant/select?tenant=...&next=...` to set the `selected_tenant` cookie and then redirect (with safe, same‑origin `next` validation and default `/studio/agents`); `/select-tenant` now supports `?next=` deep‑links and auto‑selects when a single membership exists; `/studio` now redirects to `/studio/agents`; new public `/health` and protected `/dev/health` pages aid diagnosis; API signup ensures unique personal tenant slug generation.
- IAM — Hotfix: Avoid nested transactions in membership endpoints
  - Membership endpoints (`PUT /api/tenants/{tenantId}/members/{userId}`, `DELETE /api/tenants/{tenantId}/members/{userId}`, and `POST /api/tenants/{tenantId}/memberships/{userId}/roles`) now detect and reuse an ambient transaction opened by `TenantScopeMiddleware` instead of always starting a new one. This prevents runtime 500s observed during roles updates when the middleware already began a tenant-scoped transaction. Behavior under EF InMemory remains unchanged.
- Pre‑Mig‑01: Introduced `INotificationTransport` with default `ChannelNotificationTransport` bridging to the existing in‑process ID queue. `NotificationEnqueuer` now publishes via the transport seam to prepare for external brokers without changing behavior.
- Pre‑Mig‑02: Outbox publisher integration — admin/dev retry/resend (incl. bulk) and the auto‑resend scanner now publish via `INotificationTransport`. The default channel transport still bridges to the in‑process `INotificationIdQueue`, so runtime behavior is unchanged. Full API suite remains green (108/108).
- Mig‑03: Redis transport option for notifications — added `RedisNotificationTransport` (publisher) and a background `RedisNotificationSubscriberHostedService` that listens to a Redis Pub/Sub channel and forwards IDs to the in‑process dispatcher queue. Feature‑selectable via `Notifications:Transport:Mode` = `channel` (default) or `redis`; Redis settings configurable under `Notifications:Transport:Redis`.
- Dev: Notifications transport health + ping — added `GET /api/dev/notifications/health` (reports transport mode and Redis subscriber diagnostics) and `POST /api/dev/notifications/ping` (creates a synthetic queued outbox item and publishes via transport) to make e2e checks easy in Development.
- Auth‑PW: Password flows (Forgot/Reset/Change) — API endpoints added for `POST /api/auth/forgot-password` (202), `POST /api/auth/reset-password` (204), and `POST /api/auth/change-password` (204; authorized). Matching web proxy routes and minimal pages were added; Login now links to “Forgot password?” and protected layout exposes “Change password”. New unit/integration tests cover happy paths and negative cases.
- Web‑MW: Middleware consolidation — removed legacy `middleware.js` and consolidated auth/route protection in `middleware.ts`, including safe login redirects and `x-pathname` header injection for diagnostics. Added `/api/debug/session` to inspect session/cookie/headers in dev.

## Monorepo overview

- Package manager: PNPM workspaces + Turborepo
- Languages: TypeScript/Node (web/mobile/packages), C# .NET 8 (API)
- Root files: `appostolic.sln`, `package.json`, `pnpm-workspace.yaml`, `turbo.json`, `tsconfig.base.json`, `Makefile`
- Top-level folders:
  - `apps/`
    - `api/` — .NET 8 Minimal API + EF Core 8; dev header auth; agent runtime; worker/queue
    - `web/` — Next.js 14 (App Router) with server-side API proxy routes
    - `mobile/` — React Native/Expo
    - `render-worker/` — Node worker (TS)
  - `packages/` — shared packages: `sdk/`, `models/`, `ui/`, `docgen/`, `prompts/`, `video-scenes/`
  - `infra/` — Docker Compose stack, devcontainer, init SQL
  - `scripts/` — helper scripts (e.g., dev doctor)

### Full folder tree (abridged but comprehensive)

```
appostolic/
├─ appostolic.sln
├─ package.json
├─ pnpm-workspace.yaml
├─ turbo.json
├─ tsconfig.base.json
├─ Makefile
├─ README.md
├─ RUNBOOK.md
├─ SnapshotArchitecture.md
├─ apps/
│  ├─ api/
│  │  ├─ Appostolic.Api.csproj
│  │  ├─ Program.cs
│  │  ├─ App/
│  │  │  ├─ Endpoints/
│  │  │  │  ├─ V1.cs
│  │  │  │  ├─ DevToolsEndpoints.cs       # POST /api/dev/tool-call (Development only)
│  │  │  │  ├─ DevAgentsDemo.cs          # POST /api/dev/agents/demo (Development only)
│  │  │  │   ├─ DevAgentsEndpoints.cs     # GET /api/dev/agents (Development only)
  │  │  │  │  ├─ DevNotificationsEndpoints.cs # GET/POST /api/dev/notifications (Development only)
  │  │  │  │  ├─ NotificationsAdminEndpoints.cs # /api/notifications (prod: list/details/retry; tenant-scoped + superadmin)
  │  │  │  ├─ AgentsEndpoints.cs        # /api/agents CRUD + /api/agents/tools
  │  │  │  │  └─ AgentTasksEndpoints.cs    # /api/agent-tasks (create/get/list; X-Total-Count; filters: status/agentId/from/to/q)
  │  │  │  └─ Infrastructure/
  │  │  │     ├─ Auth/
  │  │  │     │  └─ DevHeaderAuthHandler.cs
  │  │  │     └─ MultiTenancy/
  │  │  │        └─ TenantScopeMiddleware.cs
  │  │  ├─ Application/
  │  │  │  ├─ Agents/
  │  │  │  │  ├─ AgentRegistry.cs
  │  │  │  │  ├─ Runtime/                   # Orchestrator + TraceWriter
  │  │  │  │  │  ├─ AgentOrchestrator.cs
  │  │  │  │  │  └─ TraceWriter.cs
  │  │  │  │  ├─ Queue/                     # In-memory queue + worker
  │  │  │  │  │  ├─ IAgentTaskQueue.cs
  │  │  │  │  │  ├─ InMemoryAgentTaskQueue.cs
  │  │  │  │  │  └─ AgentTaskWorker.cs
  │  │  │     ├─ AgentTask.cs
  │  │  │     ├─ AgentTrace.cs
  │  │  │     ├─ AgentStatus.cs
  │  │  │     └─ TraceKind.cs
  │  │  ├─ Infrastructure/
  │  │  │  ├─ AppDbContext.cs
  │  │  │  └─ Configurations/
  │  │  │     └─ *.cs
  │  │  ├─ Migrations/
  │  │  │  └─ *.cs
  │  │  ├─ tools/
  │  │  │  └─ seed/                        # Idempotent seed for dev user/tenants
  │  │  └─ Properties/launchSettings.json
  │  ├─ web/
  │  │  ├─ next.config.mjs
  │  │  ├─ package.json
  │  │  ├─ src/
  │  │  │  └─ lib/serverEnv.ts              # API_BASE/DEV_USER/DEV_TENANT validation
  │  │  └─ app/
  │  │     ├─ layout.tsx
  │  │     ├─ page.tsx
  │  │     ├─ login/page.tsx
  │  │     ├─ signup/page.tsx
  │  │     ├─ forgot-password/page.tsx
  │  │     ├─ reset-password/page.tsx
  │  │     ├─ change-password/page.tsx
  │  │     ├─ magic/
  │  │     │  ├─ request/page.tsx
  │  │     │  └─ verify/page.tsx
  │  │     ├─ select-tenant/page.tsx       # Tenant selection with optional ?next=
  │  │     ├─ studio/page.tsx              # Redirects to /studio/agents
  │  │     ├─ health/page.tsx              # Public health page
  │  │     ├─ dev/health/page.tsx          # Protected health page
  │  │     ├─ dev/page.tsx
  │  │     ├─ dev/agents/page.tsx
  │  │     ├─ dev/agents/components/AgentRunForm.tsx
  │  │     ├─ dev/agents/components/TracesTable.tsx
  │  │     ├─ studio/agents/                # Agent Studio (CRUD UI)
  │  │     │  ├─ page.tsx                   # List (defaults to enabled-only)
  │  │     │  ├─ new/page.tsx               # Create
  │  │     │  ├─ [id]/page.tsx              # Edit
  │  │     │  └─ components/
  │  │     │     ├─ AgentForm.tsx           # Create/Edit form (includes Enabled toggle)
  │  │     │     └─ AgentsTable.tsx         # List table
  │  │     └─ api-proxy/
  │  │        ├─ auth/forgot-password/route.ts # POST /api-proxy/auth/forgot-password → API /api/auth/forgot-password (anonymous)
  │  │        ├─ auth/reset-password/route.ts  # POST /api-proxy/auth/reset-password → API /api/auth/reset-password (anonymous)
  │  │        ├─ auth/change-password/route.ts # POST /api-proxy/auth/change-password → API /api/auth/change-password (authorized)
  │  │        ├─ auth/signup/route.ts      # POST /api-proxy/auth/signup → API /api/auth/signup (anonymous proxy)
  │  │        ├─ auth/magic/request/route.ts   # POST /api-proxy/auth/magic/request → API /api/auth/magic/request
  │  │        └─ auth/magic/consume/route.ts   # POST /api-proxy/auth/magic/consume → API /api/auth/magic/consume
  │  │        ├─ dev/agents/route.ts        # GET /api-proxy/dev/agents → API /api/dev/agents
  │  │        ├─ agents/route.ts            # GET/POST /api-proxy/agents → API /api/agents
  │  │        ├─ agents/[id]/route.ts       # GET/PUT/DELETE /api-proxy/agents/{id}
  │  │        └─ agents/tools/route.ts      # GET /api-proxy/agents/tools → API /api/agents/tools
  │  │        └─ agent-tasks/
  │  │           ├─ route.ts                 # GET/POST /api-proxy/agent-tasks → API
  │  │           └─ [id]/route.ts            # GET /api-proxy/agent-tasks/{id}
  │  │     └─ api/
  │  │        └─ tenant/select/route.ts    # GET/POST /api/tenant/select — set cookie; GET redirects with validated next
  │  │        └─ debug/session/route.ts    # GET /api/debug/session — diagnostic: session, cookie, and proxy headers
  │  ├─ mobile/
  │  │  ├─ app.json
  │  │  ├─ package.json
  │  │  └─ src/App.tsx
  │  └─ render-worker/
  │     └─ src/index.ts
├─ packages/
│  ├─ sdk/
│  │  ├─ package.json
│  │  ├─ tsconfig.json
│  │  └─ src/index.ts
│  ├─ models/
│  │  └─ src/*.ts
│  ├─ prompts/
│  ├─ ui/
│  └─ video-scenes/
├─ infra/
│  ├─ devcontainer/
│  │  └─ devcontainer.json
│  └─ docker/
│     ├─ compose.yml
│     ├─ docker-compose.yml
│     ├─ Dockerfile.api
│     ├─ Dockerfile.web
│     ├─ .env
│     ├─ initdb/
│     │  └─ init.sql
│     └─ data/
│        ├─ postgres/
│        ├─ minio/
│        └─ qdrant/
└─ scripts/
  └─ dev-doctor.sh
```

## Tech stack (high level)

- Backend: .NET 8, Minimal API, EF Core 8 (Npgsql provider, PostgreSQL)
- Frontend: Next.js 14 (app router), TypeScript, React 18, MUI Premium
- Mobile: Expo/React Native (TypeScript)
- Infra: Docker Compose for Postgres, Redis, MinIO, Qdrant, pgAdmin, Mailhog (dev)
- Docs/SDK: Swashbuckle for OpenAPI; custom TypeScript SDK package

---

## Runtime architecture

### API service (`apps/api`)

- Entrypoint: `apps/api/Program.cs`
- Middleware: Swagger, Authentication/Authorization, TenantScopeMiddleware, legacy sample for `X-Tenant-Id`
- Auto-migration (Dev/Test): `Database.Migrate()` at startup for relational providers
- OpenTelemetry: traces, metrics, logs with optional OTLP exporter; console exporters in Development

### Web app (`apps/web`)

- Next.js 14 (App Router); server-only proxy routes under `app/api-proxy/*` inject dev headers and avoid CORS
- Env validation: `src/lib/serverEnv.ts` ensures `NEXT_PUBLIC_API_BASE`, `DEV_USER`, `DEV_TENANT`
- Client hardening: dev-only env checks for `DEV_*` are scoped to the server to avoid client runtime crashes when `WEB_AUTH_ENABLED=false`.
- Dev pages: `/dev/agents`, Agent Studio under `/studio/agents`
- Middleware: consolidated into a single `middleware.ts` that protects routes (avoids login loops) and injects an `x-pathname` header for diagnostics.
- Server fetch helper: `app/lib/serverFetch.ts` exports `fetchFromProxy()` which:
  - Builds an absolute base URL from request headers (`x-forwarded-host`/`x-forwarded-proto`) or `NEXT_PUBLIC_WEB_BASE` when provided
  - Forwards cookies so NextAuth session reaches the proxy
  - Disables cache by default (`no-store`) with `next: { revalidate: 0 }`
    Use this helper in server components and server actions to call internal `/api-proxy/*` routes. Avoid `fetch('/api-proxy/...')` directly in server code to prevent URL parse errors.
- Signup proxy: `POST /api-proxy/auth/signup` forwards to API `/api/auth/signup` with same-origin semantics to avoid browser CORS on `/signup`.
- Password flows (web): minimal pages `/forgot-password`, `/reset-password`, and `/change-password` call the corresponding same-origin proxies under `/api-proxy/auth/*`. The login page links to Forgot Password, and the protected layout includes a Change Password link beside the TenantSwitcher.
- Tenant selection route: `GET /api/tenant/select?tenant={slug}&next={path}` sets the `selected_tenant` cookie and redirects to `next` (must be a same-origin path beginning with `/`); defaults to `/studio/agents` when `next` is missing/invalid. `POST /api/tenant/select` sets the cookie and returns JSON for programmatic updates.
- Select-tenant deep-linking: `/select-tenant` accepts `?next=...` and validates it server-side. If the user has exactly one membership, it auto-selects and redirects via the GET route above. Otherwise, the form includes a hidden `next` and redirects via GET after selection.
- Health pages: `/health` (public) and `/dev/health` (protected) help verify session/tenant state and middleware behavior.
- Session diagnostics: `GET /api/debug/session` returns session summary, the `selected_tenant` cookie value, and computed proxy headers.
- Studio landing: `/studio` redirects to `/studio/agents` to avoid 404s and improve deep-link resilience.

### Mobile (`apps/mobile`) and Render Worker (`apps/render-worker`)

- Mobile: Expo/React Native (TypeScript), minimal scaffold
- Render Worker: Node/TypeScript worker for rendering tasks (placeholder)

### Notifications worker (`apps/notifications-worker`)

- Entrypoint: `apps/notifications-worker/Program.cs`
- Purpose: Run the notifications runtime (outbox dispatcher, purge, auto‑resend, optional Redis subscriber) in a separate process from the API.
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
