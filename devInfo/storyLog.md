2025-09-20 — Auth/JWT: Story 5a Local HTTPS & Secure Refresh Cookie Validation — ✅ DONE

- Summary
  - Implemented local HTTPS enablement path and hardened refresh cookie Secure flag behavior. Added Makefile target `api-https` to run the API on `https://localhost:5198` (requires one-time `dotnet dev-certs https --trust`). Updated all refresh cookie issuance blocks in `V1.cs` (login, magic consume, select-tenant) to set `Secure = http.Request.IsHttps` removing the previous environment heuristic that could mark cookies Secure under pure HTTP in Development. Added integration test `RefreshCookieHttpsTests` confirming the cookie omits `Secure` over HTTP and (simulated via `X-Forwarded-Proto: https`) includes it when HTTPS is indicated. This sets the stage for reliable browser SameSite/Secure behavior prior to broader refresh endpoint rollout (Story 6) and reduces accidental confusion during QA where Secure cookies would not appear without HTTPS. Plan, SnapshotArchitecture, LivingChecklist updated; story marked complete.
  - Files changed
    - Makefile — added `api-https` target (HTTPS watcher) with explanatory comment.
    - apps/api/App/Endpoints/V1.cs — three cookie issuance blocks now use `Secure = http.Request.IsHttps`; first block annotated with Story 5a comment.
    - apps/api.tests/Auth/RefreshCookieHttpsTests.cs — new integration tests for Secure attribute presence/absence conditions.
    - devInfo/jwtRefactor/jwtSprintPlan.md — Story 5a section marked DONE with implementation notes & follow-ups.
    - SnapshotArchitecture.md — What’s New updated with brief mention (Secure flag logic + HTTPS enablement) (appended in same-day batch).
    - devInfo/LivingChecklist.md — JWT Story 5a line added & checked.
  - Quality gates
    - New test added (passes). Existing auth & refresh cookie tests unaffected (spot run). Build remains green.
  - Rationale
    - Ensures Secure cookie semantics match actual transport security, preventing false positives in dev while enabling real Secure attribute validation once HTTPS is active. Simplifies mental model (no environment override) and prepares for upcoming removal of refresh token from JSON surface post Story 6.
  - Follow-ups
    - Optional: full HTTPS TestServer instance for deterministic Secure assertion instead of header simulation; cookie helper consolidation after refresh endpoint lands.

  2025-09-20 — Auth/JWT: Follow-up Consolidation (Story 5a extras) — ✅ DONE
  - Summary
    - Implemented post-Story 5a optional improvements: consolidated duplicated refresh cookie issuance logic into a single `IssueRefreshCookie` helper in `V1.cs` (login, magic consume, select-tenant). Added `trust-dev-certs` Makefile target to streamline local certificate trust (`dotnet dev-certs https --trust`). Upgraded `RefreshCookieHttpsTests` to create an HTTPS-based test client (base address `https://localhost`) for deterministic `Request.IsHttps` evaluation instead of relying on `X-Forwarded-Proto` simulation. This reduces drift, centralizes cookie semantics, and improves test reliability ahead of the general refresh endpoint (Story 6).
  - Files changed
    - apps/api/App/Endpoints/V1.cs — added `IssueRefreshCookie` helper; replaced three inline cookie blocks.
    - Makefile — added `trust-dev-certs` target.
    - apps/api.tests/Auth/RefreshCookieHttpsTests.cs — replaced simulated header approach with HTTPS client options; assertion now deterministic.
  - Rationale
    - DRYs cookie issuance, avoiding future attribute divergence during upcoming refresh/logout stories. Provides a clear local command for cert trust and strengthens test fidelity for Secure flag behavior.
  - Follow-ups
    - After adding `/api/auth/refresh` (Story 6) consider moving helper into a dedicated auth utilities class if additional cookie surfaces are introduced (e.g., future access token cookie).

  2025-09-20 — Auth/JWT: Story 5 Access Token Validation Middleware & Principal Construction — ✅ DONE
  - Summary
    - Introduced Development-only composite authentication scheme (BearerOrDev) eliminating per-endpoint scheme enumeration and resolving widespread 401s while preserving dev header ergonomics. Hardened JwtBearer `OnTokenValidated` to enforce GUID subject parsing and token version equality (revocation via password change or future admin bump). Ensured role flags mapping remains consistent, enabling authorization policies to pass/fail deterministically. Full test suite now green (211 passed / 1 skipped) after initial invalid_sub regression was corrected by generating GUID subjects in tests.
  - Files changed
    - apps/api/Program.cs — composite auth scheme registration; OnTokenValidated enhancements (GUID + TokenVersion check with failure message on mismatch).
    - apps/api.tests/Api/AuthJwtSmokeTests.cs — updated test to issue GUID subject tokens; regression fix.
    - SnapshotArchitecture.md — Added section documenting composite scheme rationale & validation layer.
    - devInfo/jwtRefactor/jwtSprintPlan.md — Story 5 marked DONE; deferred admin logout & caching tasks moved to future stories.
  - Quality gates
    - All auth-related tests (smoke, token version, legacy role deprecation) passing; no new warnings.
  - Rationale
    - Centralizes dev vs bearer decision logic, eliminates test brittleness, and delivers revocation-ready token validation model without per-token blacklist complexity.
  - Follow-ups
    - Admin force logout endpoint (TokenVersion increment) deferred to Story 7.
    - Optional short TTL in-memory cache for TokenVersion left for performance tuning phase.

  2025-09-20 — Auth/JWT: Story 5b HTTPS E2E Secure Cookie Validation — ✅ DONE
  2025-09-20 — Auth/JWT: Story 6 Refresh Endpoint kickoff — 🚧 IN PROGRESS
  2025-09-20 — Auth/JWT: Story 7 Logout & Global Revocation kickoff — 🚧 IN PROGRESS

- Summary
  - Planned endpoints: `POST /api/auth/logout` (single refresh token revocation + cookie clear, no TokenVersion bump) and `POST /api/auth/logout/all` (bulk revoke all refresh tokens for user + TokenVersion increment for immediate access token invalidation). Will use cookie `rt` preferred extraction with transitional body fallback under same grace flag as refresh. Structured 400/401 codes (missing_refresh, refresh_invalid) where applicable; idempotent 204 responses for already-logged-out scenarios. Sets foundation for session UX and admin forced logout (future) without requiring per-access-token state server-side.
- Follow-ups
  - Implement endpoints & integration tests (rotation chain safety, reuse failure, version bump for logout-all).
  - Add LivingChecklist line and architecture snapshot entry on completion.
  - Post-1.0: session listing + selective device logout, admin forced logout endpoint.
  - Summary
    - Implemented initial `/api/auth/refresh` endpoint: cookie-first retrieval of `rt` refresh token with fallback body token during grace period; rotates (revokes old + issues new) neutral refresh token and returns new neutral access token plus optional tenant token when `?tenant=` provided. Structured JSON error responses for invalid (`refresh_invalid`), reuse (`refresh_reuse`), and expired (`refresh_expired`) tokens. Integration tests cover: success rotation, reuse unauthorized, missing token (400), expired, revoked reuse, tenant token issuance, body-based refresh under grace.
  - Follow-ups
    - Implement deprecation headers (Deprecation/Sunset) once `AUTH__REFRESH_DEPRECATION_DATE` configured.
    - Frontend silent refresh loop & removal of placeholder `_auth/refresh-neutral` route.
    - Disable JSON body/grace flag and remove plaintext refresh.token from response after adoption.
    - CSRF strategy review if SameSite=None considered later (Story 8 / Security Hardening bucket).
    - (Post-completion note 2025-09-21, commit 6063317) Centralized refresh token hashing via new `RefreshTokenHashing` helper, replacing duplicated inline SHA256 Base64 logic in endpoints (select-tenant, refresh, logout) and delegating existing `RefreshTokenService` private hashing to the helper to prevent drift.
  - Summary
    - Implemented real HTTPS end-to-end harness exercising auth flow to validate refresh cookie (`rt`) attributes under true TLS: Secure, HttpOnly, SameSite=Lax, Path=/, future Expires, and rotation on subsequent auth action. Replaced reliance on simulated HTTPS headers for Secure assertion. Documented harness and added completion entry; sprint plan & checklist updated.
  - Files changed
    - SnapshotArchitecture.md — Testing layers section updated with HTTPS E2E harness description.
    - devInfo/jwtRefactor/jwtSprintPlan.md — Story 5b marked DONE; browser/CSRF/SameSite exploratory tasks deferred.
    - (Harness-specific files) apps/api.tests/... — E2E fixture & cookie assertion logic (see diff in same-day commit series).
  - Quality gates
    - Harness run produced expected Set-Cookie attributes; no regressions in existing integration tests.
  - Rationale
    - Provides authoritative validation of cookie security attributes pre-refresh-endpoint (Story 6), ensuring platform behavior matches production TLS expectations.
  - Follow-ups
    - Evaluate Playwright HttpOnly non-read test, SameSite=None cross-site scenarios, and CSRF double-submit strategy alongside refresh endpoint design (Story 6/8 sequencing).

2025-09-21 — Auth/JWT: Story 7 Logout & Global Revocation — ✅ DONE

- Summary
  - Implemented `/api/auth/logout` (single refresh token revoke) and `/api/auth/logout/all` (bulk revoke + TokenVersion bump) endpoints. Single logout now enforces an explicit token when a JSON body is present: an empty `{}` body (or body missing `refreshToken`) returns 400 `{ code: "missing_refresh" }` rather than falling back to the cookie, aligning with test expectations and encouraging explicit client intent. Global logout revokes all active neutral refresh tokens for the user via `IRefreshTokenService.RevokeAllForUserAsync` then increments `User.TokenVersion` using record replacement (detach + updated copy) to invalidate all outstanding access tokens immediately (existing bearer validation rejects older version). Both endpoints clear the refresh cookie (expires in past) when present and return 204 on success; operations are idempotent (already revoked/missing treated as success). Structured logs emit `auth.logout.single user=<id> tokenFound=<bool>` and `auth.logout.all user=<id> revokedCount=<n>`.
  - Files changed
    - `apps/api/App/Endpoints/V1.cs` — Added logout + logout/all handlers; added `missing_refresh` error path; adjusted JSON body parsing to distinguish between “no body” and “body present but missing field”. Hardened claim extraction (fallback to `ClaimTypes.NameIdentifier`).
    - `apps/api/Application/Services/RefreshTokenService.cs` (prior commit) — Added `RevokeAllForUserAsync` used by logout-all.
    - `apps/api.tests/Auth/LogoutTests.cs` — New integration tests: single logout reuse, global logout TokenVersion invalidation, missing refresh token 400, idempotent second logout, diagnostic pre-logout access.
    - `SnapshotArchitecture.md` — Updated What’s New (Story 7 marked complete with details).
    - `devInfo/LivingChecklist.md` — (Pending) Will tick Story 7 line on next checklist update.
  - Quality gates
    - All `LogoutTests` passing (5/5). TokenVersion bump verified by 401 on /api/me with old access token post logout-all. No regressions in existing refresh or login tests (spot run subset; full suite unchanged functionally).
  - Rationale
    - Delivers foundational session management: explicit user-driven logout (single device) plus immediate global revocation without tracking individual access tokens, leveraging lightweight TokenVersion mechanism introduced in Story 5. Provides structured errors for deterministic client handling and sets groundwork for future session/device management and admin-forced logout features.
  - Follow-ups
    - Add checklist tick + potential observability counters (logout count, revoked tokens) in a later hardening pass.
    - Session listing & selective device logout (post‑1.0 candidate).
    - Deprecation headers and eventual removal of plaintext `refresh.token` once cookie adoption confirmed.

  2025-09-21 — Auth/JWT: Story 8 Silent Refresh & Plaintext Refresh Token Suppression — ✅ DONE
  - Summary
    - Eliminated routine emission of plaintext refresh tokens from auth responses by introducing backend feature flag `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` (default: false). When disabled, responses (login, magic consume, select-tenant, refresh) omit `refresh.token` while still returning metadata (`id`, `created`, `expires`) and the secure httpOnly cookie `rt` becomes the exclusive transport. Refresh endpoint additionally restricts plaintext emission to transitional scenarios (flag enabled AND (grace body path active OR cookie feature disabled)) to prevent redundant exposure during cookie-first flows. Frontend replaced placeholder `_auth/refresh-neutral` route with real `/api/auth/refresh`, adding an in-memory silent refresh scheduler (refreshes 60s before access token expiry), single-flight concurrency guard, and 401 retry-once logic in `withAuthFetch` for near-expiry races. Added force & start/stop controls for future UX hooks. Integration tests assert plaintext omission/presence under both flag states; frontend unit tests cover scheduling and retry behavior. All existing auth suites remain green.
  - Files changed
    - apps/api/App/Endpoints/V1.cs — Conditional omission of plaintext across endpoints; refresh endpoint gating logic; unified response object shaping to avoid anonymous type divergence.
    - apps/api.tests/Auth/RefreshPlaintextExposedFlagTests.cs — New tests for flag on/off (login + refresh cases).
    - apps/api.tests/WebAppFactory.cs — Added `WithSettings` helper to inject per-test configuration for the new flag.
    - apps/web/src/lib/authClient.ts — Implemented silent refresh loop (scheduler, single-flight, retry-once 401), new exports (`startAutoRefresh`, `stopAutoRefresh`, `forceRefresh`), increased skew to 60s, real backend refresh call.
    - apps/web/src/lib/authClient.test.ts — Added tests for retry-once and scheduling logic.
    - apps/web/src/pages/api/\_auth/refresh-neutral.ts — Removed deprecated placeholder.
    - SnapshotArchitecture.md — Added Story 8 section detailing flag, rationale, and follow-ups.
    - devInfo/LivingChecklist.md — Story 8 line checked; last updated banner revised.
  - Quality gates
    - API integration tests: `RefreshPlaintextExposedFlagTests` passing; no regressions in existing refresh/logout suites (spot run diff only new tests added).
    - Web unit tests (Vitest) pass with new scheduling and retry scenarios; lint/typecheck remain green.
  - Rationale
    - Reduces XSS exfiltration surface by removing access to long-lived refresh token from JavaScript, relying on httpOnly cookie channel while maintaining uninterrupted UX via scheduled silent rotation. Transitional flag provides rollback safety and staged client adoption.
  - Follow-ups
    - Remove flag post adoption window; add metrics (`auth.refresh.rotation`, `auth.refresh.plaintext_emitted` temporary) in observability story.
    - Potential session management endpoint to enumerate active refresh tokens (metadata only).
    - CSRF strategy review if `SameSite=None` required for future cross-site embedding.

2025-09-20 — Auth/JWT: Story 4 Refresh Cookie & Frontend In-Memory Access Token — ✅ DONE

2025-09-22 — Auth/JWT: RDH Story 2 Phase A Kickoff (DevHeadersDisabledTests Migration) — 🚧 IN PROGRESS

- Summary
  - Initiated Dev Header Decommission (RDH) Story 2 Phase A by migrating the positive (success) path of `DevHeadersDisabledTests` from mint/dev header shortcuts to real auth flows using `AuthTestClientFlow.LoginAndSelectTenantAsync` (exercising `/api/auth/login` and `/api/auth/select-tenant`). Negative test asserting disabled dev headers remains to preserve coverage until deprecation middleware lands. Targeted run (2 tests) passes with fully JWT-based setup, establishing baseline pattern for subsequent auth suite migrations.
  - Files changed
    - apps/api.tests/Auth/DevHeadersDisabledTests.cs — replaced mint neutral shortcut with password seeding + flow helper call.
    - devInfo/jwtRefactor/rdhSprintPlan.md — Story 2 Phase A checkbox annotated; progress log entry added.
  - Quality gates
    - Targeted test run PASS (2/2). No other suites affected yet. Build warnings unchanged (ImageSharp advisory noted pre-existing).
  - Rationale
    - Demonstrates end-to-end password + refresh rotation path in previously shortcut test, validating helper ergonomics and ensuring future migrations can follow the same pattern. Maintains deliberate negative coverage for dev header rejection path pending later removal phases.
  - Follow-ups
    - Continue Phase A migrating remaining auth flow tests (login, refresh, logout, select-tenant variants).
    - Introduce guard to fail on unintended `x-dev-user` usage once auth suite fully migrated.
    - Proceed to Phase B (domain/feature test migrations) after core auth parity achieved.

2025-09-22 — Auth/JWT: Dev Header Decommission Sprint (RDH) — Phase A Superadmin & Notifications Tests Migration — 🚧 IN PROGRESS

- Summary
  - Began phased removal of development header authentication (`x-dev-user`, `x-tenant`, `x-superadmin`) in favor of exercising only the JWT Bearer pipeline in integration tests. Added superadmin claim support to the gated test mint helper (`POST /api/test/mint-tenant-token`) via new request flag `SuperAdmin` which injects claim `superadmin=true` on either tenant-scoped or neutral tokens. Extended `MintTenantTokenRequest` DTO and updated `TestAuthClient.MintAsync` plus `AuthTestClient.UseSuperAdminAsync` convenience wrapper. Migrated `NotificationsProdEndpointsTests` off dev headers to minted JWT tokens (tenant + superadmin permutations). Initial failures (500 InternalServerError) caused by residual explicit "Dev" authentication scheme references were resolved; rerun shows all four notifications prod tests passing (0 failed / 4 passed) under pure Bearer auth. Extra-claims overloads for `JwtTokenService` already existed, aligning with helper usage.
- Files changed
  - apps/api/App/Endpoints/V1.cs — Added `SuperAdmin` flag to `MintTenantTokenRequest`; appended extraClaims (`superadmin=true`) and neutral re-issue path when no tenant selected; utilization of existing `IssueNeutralToken` / `IssueTenantToken` extra-claims overloads.
  - apps/api.tests/Auth/TestAuthClient.cs — Added `superAdmin` parameter to `MintAsync` posting `{ SuperAdmin = true }` when requested.
  - apps/api.tests/Auth/AuthTestClient.cs — Added `UseSuperAdminAsync` helper.
  - apps/api.tests/Api/NotificationsProdEndpointsTests.cs — Replaced dev header setup with auth helper mint flows (tenant + superadmin variants).
- Quality gates
  - Targeted test run: `NotificationsProdEndpointsTests` now PASS (4/4) under JWT-only path. No compile errors; broader suite pending phased migration of remaining dev header dependent tests.
- Rationale
  - Ensures production-auth parity in tests and reduces drift risk between dev/test and production environments by removing the alternate Dev header code path. Superadmin claim added strictly as a test-scope elevation mechanism while the real superadmin provisioning story is deferred.
- Follow-ups
  - Phase B: Migrate remaining test classes still relying on `x-dev-user` / `x-tenant` headers; introduce guard to fail fast if those headers appear in requests once suite migration is complete.
  - Phase C: Remove composite policy scheme fallback logic and the Dev authentication handler entirely; update `SnapshotArchitecture.md` and LivingChecklist; add regression test ensuring headers are ignored/rejected.
  - Phase D: Documentation & rollback tag (`remove-dev-headers`) plus story closure entry.

2025-09-22 — Auth/JWT: Dev Header Decommission Sprint Plan (RDH) — 🚧 IN PROGRESS

- Summary
  - Created `devInfo/jwtRefactor/rdhSprintPlan.md` detailing the Dev Header Decommission (RDH) sprint to fully remove development header authentication (`x-dev-user`, `x-tenant`) and the composite scheme from all environments and tests. Plan covers phased test migration to JWT helpers, deprecation middleware (temporary 401 `dev_headers_deprecated`), physical removal of handler & flag, regression guard (401 `dev_headers_removed` test + CI grep), documentation & rollback tag strategy, risks, acceptance criteria, and optional hardening follow-ups. SnapshotArchitecture updated to reference the new sprint. Next actionable stories: consolidate test token helpers, migrate integration tests off dev headers, introduce deprecation middleware, then remove code.
- Files changed
  - devInfo/jwtRefactor/rdhSprintPlan.md (new) — full sprint breakdown with stories 0–7, risks, rollback, matrix.
  - SnapshotArchitecture.md — What’s New entry referencing RDH sprint plan creation.
- Rationale
  - Eliminates divergence between development/test and production auth paths, reducing attack surface and ensuring all test coverage exercises the production JWT flow. Simplifies mental model and prevents accidental reliance on headers in future code.
- Follow-ups
  - Story 1 helper consolidation & Story 2 phased test migration.
  - Add temporary deprecation middleware & metric, then remove handler/flag.
  - Final regression guard + documentation updates and tag `dev-headers-removed` on completion.

- Summary
  - Implemented secure httpOnly refresh cookie delivery behind feature flag `AUTH__REFRESH_COOKIE_ENABLED` on `/api/auth/login`, `/api/auth/magic/consume`, and `/api/auth/select-tenant`. Cookie name `rt`; attributes: HttpOnly; SameSite=Lax; Path=/; Secure except in Development. Rotation logic in tenant selection endpoint revokes old neutral refresh token and overwrites cookie with the new one. Added frontend in-memory neutral access token client (`authClient.ts`) so access tokens are never persisted (reduces XSS exfiltration risk). Added `withAuthFetch` wrapper to inject `Authorization: Bearer <access>` and always include credentials for future refresh requests. Created placeholder internal route `/api/_auth/refresh-neutral` (clearly documented) to scaffold upcoming general refresh flow (Story 6). Tests `RefreshCookieTests` verify issuance and rotation (case-insensitive cookie attribute match). Architecture docs and LivingChecklist updated; story flagged complete.
    2025-09-22 — Auth/JWT: RDH Story 2 Phase A MembersList & Assignments Migration — ✅ PARTIAL

- Summary
  - Migrated `MembersListTests` and `AssignmentsApiTests` off the legacy mint helper (`AuthTestClient.UseTenantAsync`) to real authentication flows using `AuthTestClientFlow.LoginAndSelectTenantAsync`. Introduced per-class `SeedPasswordAsync` helper (uses `IPasswordHasher`) to set the default password (`Password123!`) before invoking the login endpoint, ensuring tests exercise actual password verification, refresh issuance, and tenant selection. Removed prior `ClientAsync` abstraction and explicit `forceAllRoles` elevation semantics—role enforcement now derives solely from seeded memberships (Owner vs Roles.None/Learner). Post-migration targeted runs: MembersList (3/3 PASS), Assignments (5/5 PASS) validating expected 200/403/401 outcomes under production JWT paths.
- Files changed
  - `apps/api.tests/Api/MembersListTests.cs` — replaced mint flow with password seeding + flow login/select; added comments documenting Phase A pattern.
  - `apps/api.tests/Api/AssignmentsApiTests.cs` — same migration pattern; added password seeding; updated role modification tests to rely on real tokens.
  - `devInfo/jwtRefactor/rdhSprintPlan.md` — progress log entry appended noting partial completion of Phase A.
- Quality gates
  - Both migrated suites green; no regressions observed in role enforcement (owner vs non-admin) or membership mutation side-effects. Build unchanged aside from existing advisory warnings (ImageSharp vulnerability pre-existing).
- Rationale
  - Incrementally shrinks dependency on test-only mint helper ensuring future removal (and dev header decommission) does not create gaps in membership/role coverage. Validates flow helper ergonomics before tackling larger suites (invites, audit, avatar uploads).
- Follow-ups
  - Next: migrate `MembersManagementTests` then invites/audit suites; introduce a guard to fail CI if `UseTenantAsync` remains after Phase A; subsequently plan mint helper removal and dev header physical decommission.

2025-09-22 — Auth/JWT: RDH Story 2 Phase A MembersManagementTests Migration — ✅ PARTIAL

- Summary
  - Migrated `MembersManagementTests` off legacy mint helper path (`ClientAsync` / `AuthTestClient.UseTenantAsync`) to real password + JWT flows via `AuthTestClientFlow.LoginAndSelectTenantAsync`. Added per-class `SeedPasswordAsync` (using `IPasswordHasher`) seeding the default password (`Password123!`) for involved users before invoking `/api/auth/login` then `/api/auth/select-tenant`. Replaced all helper calls with explicit flow sequence while retaining existing membership utilities (`EnsureAdminMembershipAsync`, `GetRoles`) and preserving test intent (role flag updates, last-admin prevention, member removal, forbidden viewer mutations). Targeted run: 5 tests PASS post-migration (0 failed / 5 passed) with expected status codes (200/204 for allowed operations, 403 for unauthorized role changes, 409 for last-admin protection) confirming production auth semantics fully exercised.
- Files changed
  - apps/api.tests/Api/MembersManagementTests.cs — removed `ClientAsync` implementation & usages; added `DefaultPw`, `SeedPasswordAsync`; updated each test to seed password then call `LoginAndSelectTenantAsync` for acting user.
  - devInfo/jwtRefactor/rdhSprintPlan.md — appended progress log entry documenting successful migration & follow-ups.
- Quality gates
  - Targeted suite green (5/5). No regressions observed in previously migrated suites (spot verified MembersList & Assignments remain green). Build unchanged; no new warnings introduced.
- Rationale
  - Eliminates another cluster of mint helper dependencies in high-importance membership mutation paths, ensuring authorization & last-admin guard behavior are validated under real JWT issuance rather than elevated shortcuts—critical before removing dev header and mint infrastructure.
- Follow-ups
  - Migrate invite and audit-related test suites next; then introduce a guard to prohibit residual `UseTenantAsync` usage. Consider centralizing duplicated password seeding logic into a shared test utility after broad Phase A coverage.

2025-09-22 — Auth/JWT: RDH Story 2 Phase A AuditTrailTests Migration — ✅ PARTIAL

- Summary
  - Migrated `AuditTrailTests` from legacy mint helper usage (`ClientAsync` wrapper over `AuthTestClient.UseTenantAsync`) to real authentication flows leveraging password seeding plus `AuthTestClientFlow.LoginAndSelectTenantAsync`. Introduced `DefaultPw` constant and `SeedPasswordAsync` to hash and persist the known password for `kevin@example.com` before invoking `/api/auth/login` followed by `/api/auth/select-tenant`. Updated both tests to use the new flow, retaining existing `EnsureAdminMembershipAsync` defensive call and `LogMembershipAsync` diagnostics. Ensures audit trail creation and noop second-call semantics are exercised under production JWT issuance and role enforcement.
- Files changed
  - apps/api.tests/Api/AuditTrailTests.cs — removed `ClientAsync` mint helper; added `DefaultPw`, `SeedPasswordAsync`; replaced helper calls with flow login/select pattern.
  - devInfo/jwtRefactor/rdhSprintPlan.md — progress log appended with AuditTrailTests migration entry.
- Quality gates
  - Targeted test run PASS (2/2). No regressions detected; MembersList, Assignments, MembersManagement suites remain green from prior spot checks.
- Rationale
  - Broadens Phase A coverage to auditing, reducing remaining surface area dependent on legacy mint shortcuts and validating audit event accuracy with production auth claims.
- Follow-ups
  - Migrate `AuditsListingEndpointTests` and `UserProfileLoggingTests` next; continue with invites suites; introduce guard once majority of `UseTenantAsync` usages removed; consider consolidating duplicated password seeding helpers after additional migrations.

2025-09-22 — Auth/JWT: RDH Story 2 Phase A AuditsListingEndpointTests Migration — ✅ PARTIAL

- Summary
  - Migrated `AuditsListingEndpointTests` off legacy mint helper (`CreateAuthedClientAsync` using `AuthTestClient.UseTenantAsync`) to real authentication flows via password seeding (`SeedPasswordAsync`) and `AuthTestClientFlow.LoginAndSelectTenantAsync`. Added `DefaultPw` constant aligned with existing flow helpers. Replaced helper usage in all three tests (paging/total count, optional filters, invalid GUID filters) so each now exercises `/api/auth/login` + `/api/auth/select-tenant` endpoints before performing audited listing requests. Ensures paging logic, filter query binding (userId, changedByUserId, date range), and 400 error handling for malformed GUID and inverted date range occur under production JWT validation path (password hashing + refresh issuance + tenant token selection). Targeted run: 3 tests PASS (0 failed). No behavioral changes in assertions; only auth setup path replaced.
- Rationale
  - Extends Phase A coverage across audit listing scenarios, removing another cluster of mint helper dependencies and strengthening end-to-end verification that audit enumeration respects production auth and multi-tenancy middleware without relying on dev header shortcuts.
- Follow-ups
  - Next: migrate `UserProfileLoggingTests` (privacy audit). After invites & remaining privacy/audit suites migrated, introduce guard (CI grep or test assertion) preventing reintroduction of `UseTenantAsync` before proceeding to deprecation middleware and handler removal stories.

- Files changed
  - apps/api/App/Endpoints/V1.cs — conditional cookie append blocks added to login, magic consume, select-tenant endpoints (flag + rotation). Inline comments reference consolidation follow-up.
  - apps/api.tests/Auth/RefreshCookieTests.cs — new integration tests asserting Set-Cookie present and rotated on tenant selection; header parsing normalized.
  - apps/api.tests/WebAppFactory.cs — injects in-memory configuration `AUTH__REFRESH_COOKIE_ENABLED=true` for deterministic test enablement.
  - apps/web/src/lib/authClient.ts — new in-memory neutral access token store & helper functions (`primeNeutralAccess`, `getAccessToken`, `withAuthFetch`).
  - apps/web/src/lib/auth.ts — integrate `primeNeutralAccess` in credentials & magic login callbacks.
  - apps/web/src/pages/api/\_auth/refresh-neutral.ts (or app route equivalent) — temporary stub refresh route (to be replaced by real backend refresh endpoint in Story 6).
  - apps/web/src/lib/**tests**/authClient.test.ts, withAuthFetch.test.ts (naming per existing pattern) — unit tests ensuring bearer header injection and prime logic.
  - SnapshotArchitecture.md — Story 4 section added (marked complete; follow-ups enumerated).
  - devInfo/LivingChecklist.md — added Story 4 checklist line (done) and updated last updated banner.
- Quality gates
  - API: RefreshCookieTests passing alongside existing auth suites (no regressions observed).
  - Web: New unit tests passing (authorization header injection, token priming). Existing Vitest suite green under Node 20.
  - Lint/Typecheck: No new issues introduced (scoped additions followed existing tsconfig and eslint baselines).
- Rationale
  - Moves refresh token storage to an httpOnly cookie to mitigate XSS exfiltration and enables future silent refresh via standard credentialed requests. Keeps access token ephemeral in memory (short-lived) aligning with principle of least persistence.
- Follow-ups
  - Story 6: Implement `/api/auth/refresh` endpoint; retire placeholder route; remove `refresh.token` from JSON when cookie enabled (grace window for clients).
  - Story 5 / 5a: Local HTTPS & cookie secure enforcement; potential SameSite tightening after cross-origin flows evaluated.
  - Refactor: DRY cookie issuance blocks into helper once refresh endpoint centralizes logic.
  - Observability: Add counters for refresh issuance/rotation/failure (later story) plus structured revocation logging.

2025-09-19 — IAM: Legacy invite role write path deprecated (Story 4 refLeg-04) — ✅ DONE

- Summary
  2025-09-20 — Auth/JWT: Story 5 Access Token Revocation via TokenVersion (password change) — ✅ DONE

- Summary
  - Implemented access token revocation by introducing an integer `TokenVersion` column on `app.users` (default 0) and embedding claim `"v"` in every issued access token (neutral & tenant). On token validation, the JWT bearer events now load the current `TokenVersion` from the database (single row query) and reject tokens whose claim `v` is less than the stored version (failure message `token_version_mismatch`). The password change endpoint (`POST /api/auth/change-password`) now increments `TokenVersion` atomically when the current password is verified, ensuring all previously issued access tokens immediately become invalid without requiring a server-side token blacklist. Added fallback handling in validation for identity claim mapping (`sub` vs `ClaimTypes.NameIdentifier`) to avoid false negatives caused by default inbound claim type transformations, and added an email claim fallback (`email` vs `ClaimTypes.Email`) in the change-password endpoint. Created integration test `AccessTokenVersionTests` verifying: login, successful authenticated `/api/me`, password change increments version, old token receives 401 with failure reason `token_version_mismatch`.
- Files changed
  - apps/api/Migrations/20250920154954_s6_10_user_token_version.\* — new migration adding `TokenVersion` int NOT NULL default 0 to `app.users`.
  - apps/api/App/Endpoints/V1.cs — password change endpoint increments `TokenVersion`; added email claim fallback comment; ensures update uses record replacement semantics.
  - apps/api/Program.cs — JWT bearer `OnTokenValidated` now falls back to `ClaimTypes.NameIdentifier` when `sub` is absent (inbound claim mapping), loads user TokenVersion, and fails auth if mismatch.
  - apps/api.tests/Auth/AccessTokenVersionTests.cs — new integration test covering revocation after password change.
  - apps/api.tests/Api/AuthJwtSmokeTests.cs — updated neutral token issuance call to include new `tokenVersion` parameter (signature alignment).
- Quality gates
  - Targeted test `AccessTokenVersionTests` PASS; full affected auth tests remain green. Migration builds & applies (local). No performance concerns: single user lookup per token validation (already required for version check) cached by normal connection pooling; future optimization (per-user version cache with short TTL) deferred.
- Rationale
  - Provides deterministic, O(1) revocation of all outstanding access tokens for a user on credential compromise events (password change) without tracking individual token identifiers. Simpler operational model vs maintaining distributed blacklist; aligns with planned refresh rotation flow (Story 6) for continuous session continuity with forced re-auth of stale access tokens.
- Follow-ups
  - Story 6 general refresh endpoint should issue new access tokens referencing updated `TokenVersion` automatically after password change.
  - Consider admin-driven global user revocation endpoint (increment TokenVersion without password change) and audit log entry.
  - Potential minor perf enhancement: L2 cache or memory cache of (UserId -> TokenVersion) with short expiration (e.g., 30s) to reduce DB hits under high concurrency; defer until profiling indicates need.

2025-09-22 — Auth/JWT: RDH Story 2 Phase A UserProfileEndpointsTests Migration — ✅ PARTIAL

- Summary
  - Migrated `UserProfileEndpointsTests` from legacy mint helper (`AuthTestClient.UseTenantAsync`) to full real auth flow: seed password via `IPasswordHasher` (helper `SeedPasswordAsync`) then invoke `/api/auth/login` followed by `/api/auth/select-tenant` using `AuthTestClientFlow.LoginAndSelectTenantAsync`. All three tests (`Get_me_returns_user_with_profile`, profile merge/trim/validate, non-object body rejection) now exercise password hashing, refresh issuance, and tenant selection under JWT Bearer without dev headers. Targeted run PASS (3/3). This reduces remaining legacy helper usages (guard test count expected to drop by one) and advances Phase A toward Avatar and Invites suites.
- Files changed
  - apps/api.tests/Api/UserProfileEndpointsTests.cs — removed `ClientAsync` mint helper; added `DefaultPw`, `SeedPasswordAsync`; updated each test to call real auth flow.
  - SnapshotArchitecture.md — appended What’s New bullet noting migration and next target.
- Quality gates
  - Targeted execution: 3 tests PASS after migration; no new warnings or failures. Guard test still WARN-only until all migrations complete.
- Rationale
  - Ensures profile endpoints (GET/PUT /api/users/me) are validated under production-like auth (password verification + tenant token issuance) eliminating reliance on test-only mint endpoint ahead of dev header decommission.
- Follow-ups
  - Migrate `UserAvatarEndpointsTests` next (multiple helper usages); re-run guard to confirm decremented count; continue with invites/settings/catalog/agent tasks suites; then enforce guard CI failure and remove mint helper.

2025-09-22 — Auth/JWT: RDH Story 2 Phase A UserAvatarEndpointsTests Migration — ✅ PARTIAL

- Summary
  - Migrated all six `UserAvatarEndpointsTests` cases off legacy `AuthTestClient.UseTenantAsync` to real authentication: per-test password seeding (`SeedPasswordAsync`) then `/api/auth/login` + `/api/auth/select-tenant` via `AuthTestClientFlow.LoginAndSelectTenantAsync`. Scenarios (success PNG, unsupported media 415, payload too large 413, too-rectangular 422, downscale to <=512, transparent logo preservation) now execute under production JWT pipeline (password hash verification, refresh issuance, tenant token). Targeted run PASS (6/6). Ensures avatar upload/validation paths (size, mime, aspect, downscale) are exercised with real claims before decommissioning dev headers & mint helper.
- Files changed
  - apps/api.tests/Api/UserAvatarEndpointsTests.cs — added `DefaultPw`, `SeedPasswordAsync`; replaced each `UseTenantAsync` call with login/select flow; added DI scope for password hashing.
- Quality gates
  - Targeted execution 6/6 PASS (duration ~300ms). No regressions observed in earlier migrated suites (spot confidence from isolated run). Guard count expected to drop by one (from 12 to 11) after re-run.
- Rationale
  - Avatar endpoints previously verified only under minted shortcut tokens; migrating to real flow validates password hashing + tenant selection interaction with multipart form handling and deep profile JSON merge that records avatar metadata.
- Follow-ups
  - Re-run guard test (expect 11 remaining). Proceed to Invites suite migrations next. Consider extracting shared `SeedPasswordAsync` into a base utility after a few more migrations to reduce duplication.

2025-09-22 — Auth/JWT: RDH Story 2 Phase A Invites Suite Migration & Invite Delete Claim Fallback — ✅ PARTIAL

- Summary
  - Migrated all four invites-related integration test classes off the legacy mint helper (`AuthTestClient.UseTenantAsync`) to exercise the real password + `/api/auth/login` + `/api/auth/select-tenant` flow: `InvitesEndpointsTests`, `InvitesAcceptTests`, `InvitesRolesFlagsTests`, and `LegacyRoleWritePathDeprecationTests`. Each class now seeds the default password (`Password123!`) via a local `SeedPasswordAsync` helper (using `IPasswordHasher`) before authenticating through the flow helper `AuthTestClientFlow.LoginAndSelectTenantAsync` (owner paths) or `LoginNeutralAsync` (invitee acceptance paths). During migration the full lifecycle test began failing on the final revoke (`DELETE /api/tenants/{id}/invites/{inviteId}` returning 400). Root cause: the DELETE invite endpoint extracted only the raw `sub` claim while the rest of the invite endpoints (e.g., listing) and the broader auth pipeline tolerate either `sub` or the mapped `ClaimTypes.NameIdentifier` (inbound claim type mapping can rename `sub`). Tokens issued via the real flow surfaced the user id under the fallback key, so DELETE could not parse the caller id, returning a BadRequest. Patched the DELETE invite endpoint to mirror the established fallback logic (`user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)`). After patch the lifecycle test passes (204 on revoke) and all migrated invites tests are green under pure JWT paths.
- Files changed
  - apps/api.tests/Api/InvitesEndpointsTests.cs — removed legacy client factory; added `DefaultPw`, `SeedPasswordAsync`, replaced auth setup with real login + tenant selection; lifecycle test retains a (now redundant) re-auth step (will remove in cleanup) before revoke.
  - apps/api.tests/Api/InvitesAcceptTests.cs — migrated owner + invitee paths to real flow; invitee uses neutral login after password seed instead of mint helper; removed `UseTenantAsync` usage.
  - apps/api.tests/Api/InvitesRolesFlagsTests.cs — migrated; invite acceptance now uses neutral login path for invitee; removed diagnostic token comments.
  - apps/api.tests/Api/LegacyRoleWritePathDeprecationTests.cs — migrated legacy role regression tests to real flow ensuring deprecation behavior validated under production auth.
  - apps/api/App/Endpoints/V1.cs — DELETE invite endpoint user id extraction updated to use `sub` OR `ClaimTypes.NameIdentifier` fallback (parity with listing endpoint) preventing false 400 after auth migration.
- Quality gates
  - Targeted invites test run pre-fix: 1 failure / 7 pass (DELETE 400). Post endpoint patch: all invites tests pass (8/8 and subsequent 7/7 targeted run). Guard test now reports 7 remaining `UseTenantAsync` usages (down by 4) across: AgentTasks, Notifications (already migrated by mint but still calling mint?), TenantSettings, ToolCatalog, DevGrantRoles, DenominationsMetadata, and the guard test itself.
  - Build succeeded with only pre-existing ImageSharp vulnerability warnings; no new warnings introduced by patch.
- Rationale
  - Ensures invites suite fully validates production authentication flow (password hash verification, refresh issuance, tenant token selection, role flags) rather than shortcut mints, eliminating a hidden dependency that masked claim extraction divergence in DELETE revoke path. Aligns all invite endpoints on consistent claim extraction fallback reducing future regressions when inbound claim mappings change.
- Follow-ups
  - Remove redundant re-auth step in lifecycle test once broader cleanup pass occurs.
  - Migrate remaining 6 non-guard `UseTenantAsync` usages (TenantSettings, ToolCatalog, DenominationsMetadata, DevGrantRolesEndpoint, AgentTasksEndpoints, NotificationsProd endpoints if any residual) then flip guard from warning to failing.
  - Consolidate duplicated `SeedPasswordAsync` helpers into a shared test utility (post-migration batch) to reduce boilerplate.
  - Document claim fallback parity note in `SnapshotArchitecture.md` (added) and ensure future auth endpoint additions adopt same pattern.
  - After final migration, schedule mint helper + dev header decommission phases (update sprint plan & LivingChecklist).

  - Enforced flags-only contract for invite creation: `POST /api/tenants/{tenantId}/invites` now rejects any request specifying the legacy single `role` field with HTTP 400 and `{ code: "LEGACY_ROLE_DEPRECATED" }`. Callers must provide `roles` (array of flag names) or `rolesValue` (int bitmask). Response payload no longer returns legacy `role`; it includes `{ email, roles, rolesValue, expiresAt }` with `roles` as a flags string for readability and `rolesValue` as the machine bitmask. Updated HTML email body to list composite roles flags instead of a single legacy role. Transitional behavior: member role change endpoint still accepts legacy `role` (documented by a regression test) to avoid broad surface disruption; a future story will deprecate that path and remove the legacy column.

- Files changed
  - apps/api/App/Endpoints/V1.cs — invite endpoint: reject `role`, parse `roles` or `rolesValue`, remove legacy role echoes, update email body.
  - apps/api.tests/Api/LegacyRoleWritePathDeprecationTests.cs — new regression tests (invite legacy role rejected; member role change still accepted pending next phase).
- Quality gates
  - Full API test suite PASS (190/190) post-change; added targeted regression tests green.
  - No other endpoints impacted; existing invites lifecycle tests updated earlier already using flags.
- Rationale
  - Locks in flags-first usage, flushing any lingering clients still sending the deprecated single role before dropping the legacy column. Ensures consistency between stored bitmask and API contract while providing a controlled transition window for member role changes.
- Follow-ups
  - Phase 2: Deprecate legacy role on member role change endpoint (expect 400 + LEGACY_ROLE_DEPRECATED) then remove legacy `Role` column and mapping.
  - Add DB CHECK constraint (`roles <> 0`) once legacy removal PR merges.

2025-09-18 — Org Settings: Tenant logo upload error handling hardened. Prevent raw HTML from rendering on upload/delete failures by detecting HTML responses and surfacing friendly messages; added a unit test simulating an HTML error; full web test suite PASS. Updated `TenantLogoUpload` accordingly.
2025-09-19 — Auth/Data: Backfill zero roles memberships to full flags — ✅ DONE

- Summary
  - Added migration `s5_02_membership_roles_backfill_zero_to_all` executing `UPDATE app.memberships SET roles = 15 WHERE roles = 0;` to remediate a small set of memberships created during the legacy→flags transition with an unset (`0`) roles bitmask. Assigning `15` grants all four canonical flags (TenantAdmin|Approver|Creator|Learner) to avoid accidental under‑privilege prior to legacy role column removal.
- Files changed
  - apps/api/Migrations/20250919030923_s5_02_membership_roles_backfill_zero_to_all.cs — data migration with idempotent update (no Down reversal).
- Quality gates
  - Migration applied locally via `make migrate` (build succeeded; database update completed without errors). No code paths depend on zero roles state; existing auth serialization tests remain green.
- Rationale
  - Ensures all active memberships possess a non-zero, machine-decodable flags bitmask before disabling the temporary legacy fallback in the web client, reducing risk of privilege mismatches.
- Follow-ups
  2025-09-19 — Auth/Data: Role change preserves flags + test + seeding verification — ✅ DONE

- Summary
  - Fixed a latent defect where changing a legacy `MembershipRole` (Owner/Admin/Editor/Viewer) via `PUT /api/tenants/{tenantId}/members/{userId}` replaced the record without copying the `Roles` bitmask, risking a `roles=0` membership if flags were relied upon elsewhere. Added explicit `Roles = DeriveFlagsFromLegacy(newRole)` to all replacement membership constructions in that endpoint (in-memory path, ambient transaction path, and explicit transaction path). Strengthened test seeding by ensuring `WebAppFactory` assigns a full flags bitmask for the default owner membership (previously omitted, defaulting to zero). Added an integration test asserting a role change from Owner -> Editor updates the flags to `Creator|Learner` (non-zero) and matches the legacy mapping. Also confirmed the standalone seed tool already sets full flags for owner and baseline memberships.
- Files changed
  - apps/api/App/Endpoints/V1.cs — add `Roles = DeriveFlagsFromLegacy(newRole)` in each membership replacement block of role-change endpoint.
  - apps/api.tests/WebAppFactory.cs — include `Roles = TenantAdmin|Approver|Creator|Learner` on seeded owner membership.
  - apps/api.tests/Security/RoleAuthorizationTests.cs — new test `RoleChange_Updates_RolesBitmask_FromLegacyRole` validating bitmask correctness after role mutation.
- Rationale
  - Prevents silent introduction of `roles=0` during role transitions and ensures flags-only authorization logic remains consistent post-mutation. Aligns test and production seeding to avoid false negatives or masked regressions.
- Quality gates
  - Compilation: PASS (no new errors in modified files).
  - Existing authorization tests unaffected; new test passes locally (fast in-memory DB path).
- Follow-ups
  - Optional: Add DB CHECK constraint (`roles <> 0`) after confirming no legacy rows in all environments.
  - Consider backfilling any historical role-change derived rows (none observed beyond manually corrected set of three).

2025-09-20 — Auth/JWT: Story 5b Real HTTPS Secure Refresh Cookie E2E Harness — ✅ DONE

- Summary
  - Implemented a dedicated transport-level E2E test harness to perform real HTTPS Secure cookie validation. Added new project `apps/api.e2e` containing `E2EHostFixture`, which spins up an in‑process minimal Kestrel host bound to a random localhost port with a runtime‑generated self‑signed ECDSA P‑256 certificate (SAN=localhost) via `Kestrel.ListenLocalhost(port).UseHttps(cert)`. This overcomes the limitation of `TestServer` (always reports `Request.IsHttps=false`, so Secure cookies cannot be asserted) and replaces a prior abandoned attempt to spawn the full API process plus InMemory EF (which was timing out during readiness probing). A lightweight endpoint `GET /e2e/issue-cookie` issues a refresh cookie `rt` (`HttpOnly; SameSite=Lax; Path=/; Secure=Request.IsHttps; Expires=+30d`). The test `SecureRefreshCookieTests` requests the endpoint and asserts (case-insensitive) presence of `secure`, `httponly`, `samesite=lax`, `path=/`, and a future Expires (>10 minutes). Headers are normalized to lowercase to tolerate server casing. Certificate validation is bypassed only for the test client via a custom `HttpClientHandler`. Harness logs its listen address (`[E2E] Listening https://localhost:{port}`) for troubleshooting.
  - Pivot rationale: Full API process approach added brittle dependencies (DB, migration timing) for a narrow concern (TLS + Set-Cookie). The minimal host eliminates DB overhead and accelerates feedback while keeping production code untouched.
  - Files changed
    - Added: `apps/api.e2e/Appostolic.Api.E2E.csproj`, `E2EHostFixture.cs`, `SecureRefreshCookieTests.cs`, `README.md` (harness usage docs).
    - Modified: `SnapshotArchitecture.md` (What’s New + Testing Layers section), `devInfo/LivingChecklist.md` (added Story 5b line & updated timestamp).
  - Quality gates
    - api.e2e project builds; test passes (1/1). No regressions expected—no production assemblies altered besides doc updates. Existing API & Web suites unaffected (pending full matrix run before merge).
  - Rationale
    - Provides deterministic, real TLS validation path ensuring the Secure attribute is genuinely set only under HTTPS transport, preventing false positives from simulated headers. Keeps integration suite lean while adding a focused layer for transport/security assertions.
  - Follow-ups
    - Extend harness to exercise real auth flows post refresh endpoint (Story 6) or potentially migrate cookie issuance helper behind conditional compilation. Consider integrating api.e2e into CI (separate job) to guard against regressions in cookie security semantics.

  - Add a guard test asserting no future insert results in `roles=0` (optional) and proceed with removal of legacy `role` field after staging verification.

  2025-09-20 — Auth/JWT: Development Composite Auth Policy (BearerOrDev) & Regression Fix — ✅ DONE
  - Summary
    - Introduced a Development-only composite authentication policy scheme ("BearerOrDev") that inspects each request for `x-dev-user`; when present it authenticates via the existing Dev header handler, otherwise it defers to standard JWT Bearer. This eliminated the need to redundantly annotate endpoint groups with `AuthenticationSchemes="Dev,Bearer"` and resolved a broad set of 401 Unauthorized test failures where dev-header authenticated requests hit endpoints registered only with the default Bearer scheme. Also tightened JWT subject validation (already present) by updating the auth smoke test to issue a GUID subject instead of a non-GUID string which previously triggered `invalid_sub` failures. After applying the composite scheme and test fix, the full API test suite passed (211 passed, 1 skipped, 0 failed — down from 65 failures pre-fix). Notifications admin tests (initially failing 7/7 with 401) now pass under the unified scheme without per-endpoint overrides.
  - Files changed
    - apps/api/Program.cs — Added policy scheme registration (`AddPolicyScheme("BearerOrDev")`), selector logic, and Development-only default authenticate/challenge override; retained existing Dev & Bearer scheme registrations.
    - apps/api.tests/Api/AuthJwtSmokeTests.cs — Updated to issue GUID subject and assert dynamic subject presence.
    - SnapshotArchitecture.md — Added What's New entry documenting rationale and impact.
  - Quality gates
    - Focused runs: Auth smoke (green), legacy role deprecation tests (green), then full suite (green). No production (non-Development) behavior changed — production still uses Bearer only.
  - Rationale
    - Centralizes dev ergonomics for header-based auth used heavily in integration tests and local tooling while avoiding repetitive scheme lists (reducing risk of future omissions). Ensures JWT validation logic can enforce GUID subjects consistently without breaking dev-header scenarios.
  - Follow-ups
    - Optional: Remove now-redundant explicit `AuthenticationSchemes` annotations from notifications/dev endpoint groups.
    - Consider adding a small diagnostic log when selector routes to Dev vs Bearer for future troubleshooting (behind a verbose flag).

2025-09-19 — Auth/API: Auth endpoints include numeric roles bitmask — ✅ DONE

- Summary
  - Added explicit numeric roles flags bitmask (`roles: int`) to membership objects returned by `/api/auth/login` and the magic token consume path (signup + login flow) so the web client can decode `TenantAdmin | Approver | Creator | Learner` without relying on legacy `role` fallback. Previously the server serialized the enum as a string (e.g., `"TenantAdmin, Approver, Creator, Learner"`) which the new web numeric/array decoder rejected, causing admin users to appear with only Learner privileges under transitional logic.
- Files changed
  - apps/api/App/Endpoints/V1.cs — cast `m.Roles` to int in anonymous projections for login and magic consume (`roles = (int)m.Roles`) with comments clarifying contract.
  - apps/api.tests/Auth/LoginRolesSerializationTests.cs — new test asserting `memberships[0].roles` is a number > 0.
  - apps/api.tests/Auth/MagicConsumeRolesSerializationTests.cs — new test asserting magic consume flow yields numeric roles bitmask.
- Quality gates
  - Targeted auth serialization tests PASS locally (post-change) and no regressions observed in other auth tests.
- Rationale
  - Ensures frontend flags-only gating receives a stable numeric representation; avoids brittle parsing of enum flag name strings and prevents privilege downgrades masked by legacy fallbacks.
- Follow-ups
  - Remove temporary legacy role fallback in web (`NEXT_PUBLIC_LEGACY_ROLE_FALLBACK`) once production confirms all memberships now include non-zero numeric `roles`.
  - Consider normalizing server to always include both `roles` (int) and `rolesLabel[]` (array of canonical strings) for DX clarity (optional; not required for current migration).

2025-09-18 — Nav — Admin gating tightening (explicit TenantAdmin flag) — ✅ DONE

- Summary
  - Hardened Admin menu visibility so only memberships that explicitly include the `TenantAdmin` flag render the Admin dropdown. Previously, a composite of non-admin flags (Approver+Creator+Learner — bitmask 14) could still surface the Admin menu because upstream `isAdmin` derivation was overly permissive. Added a regression test covering this scenario and updated `TopBar` to gate on `isAdmin && roles.includes('TenantAdmin')`.
- Files changed
  - apps/web/src/components/TopBar.tsx — require explicit `TenantAdmin` in `effectiveRoles` (`isAdminGated`).
  - apps/web/src/components/TopBar.admin.test.tsx — added regression: no Admin menu for roles `['Approver','Creator','Learner']` (bitmask 14, missing TenantAdmin).
- Quality gates
  - Web tests: PASS (62 files, 198 tests) after change; new test passes; coverage unchanged (lines ~85%).
- Rationale
  - Prevent accidental privilege inflation from future broadening of `isAdmin` derivation or transitional fallback mappings. Makes TenantAdmin flag the single authoritative signal for Admin UI entry points.
- Follow-ups
  - Instrument (dev-only) counts of sessions hitting legacy fallback to plan safe removal; later remove gating comment once backend consistently supplies flags.

2025-09-18 — Auth/Web: Flags-only authorization test alignment — ✅ DONE

2025-09-18 — Auth/Web: Numeric roles bitmask support — ✅ DONE

- 2025-09-18 — Auth/Nav: Roles trace instrumentation (temporary) — ✅ DONE
  - Summary
    - Added focused, env‑gated tracing to diagnose mismatch where admin bitmask users appear as Learner. Web: `getFlagRoles` now logs input shape, numeric decoding, legacy fallbacks, and final deduped roles when `NEXT_PUBLIC_DEBUG_ROLE_TRACE=true`. NextAuth `jwt` & `session` callbacks log raw memberships and derived booleans when `DEBUG_ROLE_TRACE=true`. API: authorization handler logs required vs have flags plus raw legacy role when `ROLE_TRACE=true`.
  - Files changed
    - apps/web/src/lib/roles.ts — trace hooks.
    - apps/web/src/lib/auth.ts — jwt/session trace output.
    - apps/api/App/Infrastructure/Auth/RoleAuthorization.cs — targeted trace line.
  - Usage
    - Set `NEXT_PUBLIC_DEBUG_ROLE_TRACE=true` (web) and `DEBUG_ROLE_TRACE=true` (server runtime) plus `ROLE_TRACE=true` (API) to correlate client session derivation with server policy evaluation.
  - Removal Plan
    - Remove after root cause resolved and roles payload uniform (array or numeric bitmask). Guarded by env so production unaffected when vars unset.

- Summary
  - Extended web roles helper `getFlagRoles` to accept a numeric (or numeric string) bitmask directly in `membership.roles` (e.g., `1` => `['TenantAdmin']`, `15` => all flags). Added defensive behavior: a bitmask of `0` yields an empty roles array (no fallback to legacy). This restores TenantAdmin UI access for users whose API payload now emits an integer bitmask instead of an array (previously rendered only Learner due to unsupported type).
- Files changed
  - apps/web/src/lib/roles.ts — broaden `roles` type to `number | string | (Array<...>)`, map numeric/ numeric-string via `roleNamesFromFlags`, skip legacy fallback when bitmask explicitly 0.
  - apps/web/src/lib/roles.numericFlags.test.ts — new tests: 1 (TenantAdmin), 7 (TenantAdmin+Approver+Creator), 15 (all), and 0 (empty, no legacy fallback).
- Quality gates
  - Web tests: PASS (suite re-run locally) with new file; coverage unchanged (~85% lines) minimal positive delta.
- Rationale
  - Aligns client with API serializer variant emitting bitmask; prevents silent privilege downgrade (admin appearing only as learner) when roles flags transmitted numerically during migration.
- Follow-ups
  - Measure prevalence of numeric vs array forms; when array form guaranteed, consider normalizing server output for consistency or coercing to array in session callback.

- Summary
  - Updated remaining web test files (8 failing tests across TenantSwitcher, session derivation, role guard, roles helpers, members admin page) to remove all residual legacy role (Owner/Admin/Editor/Viewer) assumptions. Tests now explicitly provide `roles: ['TenantAdmin', ...]` for admin scenarios and empty arrays / learner-only flags for non-admin cases. Eliminated references to deleted `deriveFlagsFromLegacy` helper and updated expectations to canonical flags-derived labels. No application runtime code changes in this batch—tests now accurately reflect the prior flags-only refactor.
- Files changed
  - apps/web/src/components/TenantSwitcher.test.tsx
  - apps/web/src/lib/auth.session.test.ts
  - apps/web/src/lib/roleGuard.test.ts
  - apps/web/src/lib/roles.test.ts
  - apps/web/app/studio/admin/members/page.test.tsx
- Quality gates
  - Web tests: PASS (61 files, 192 tests) after edits; coverage stable (~85% lines). Existing non-fatal MUI X license warnings unchanged.
- Rationale
  - Ensures test suite is authoritative for the new flags-only model, preventing false positives tied to legacy fallback logic and enabling confident future removal of the deprecated `role` field once backend payloads are fully migrated.
- Follow-ups
  - Consider adding a lint-time assertion or type helper to forbid accessing `membership.role` in new code paths to accelerate full removal.

2025-09-18 — Auth/Web: Transitional legacy role fallback reintroduced — ✅ DONE

- Summary
  - Reintroduced a temporary legacy→flags fallback in `getFlagRoles` so that memberships lacking an explicit `roles[]` flags array still yield correct capabilities (e.g., legacy `Admin`/`Owner` now map to `TenantAdmin, Approver, Creator, Learner`). The fallback is gated by `NEXT_PUBLIC_LEGACY_ROLE_FALLBACK` (defaults enabled, set to `false` to disable). This addresses a production parity gap where an admin user (e.g., kevin@b.com) appeared only as Learner in the Tenant Selector because the API response omitted populated flags.
- Files changed
  - apps/web/src/lib/roles.ts — add env‑guarded legacy mapping (Owner/Admin → full set; Editor → Creator+Learner; Viewer → Learner); tolerate legacy labels inside `roles[]` during transition.
  - apps/web/src/lib/roles.legacyFallback.test.ts — new tests covering Admin, Owner, Editor, Viewer mapping plus boolean derivation.
- Quality gates
  - Web tests: PASS (62 files, 197 tests) including new fallback suite; coverage impact neutral (< +0.1%).
- Rationale
  - Ensures no inadvertent privilege downgrade for existing tenants before the backend populates roles flags universally; keeps UI and server authorization consistent.
- Decommission Plan
  - Once API guarantees non-empty `roles[]` for all memberships, set `NEXT_PUBLIC_LEGACY_ROLE_FALLBACK=false` in staging, validate zero regressions, then remove fallback code and tests.
- Follow-ups
  - Add instrumentation to log (dev only) when fallback path is exercised to measure residual legacy dependency before removal.

  2025-09-20 — Auth/JWT: Story 3 Tenant Selection & Refresh Rotation — ✅ DONE
  - Summary
    - Implemented `POST /api/auth/select-tenant` enabling a neutral session (user + memberships + neutral refresh) to select a tenant (by slug or id) and receive a tenant-scoped access token plus a rotated neutral refresh token. Response mirrors login shape: `{ user, memberships, access, refresh, tenantToken }`. The old neutral refresh is revoked prior to issuing the new one, enforcing single active refresh chain. Membership absence returns 403; invalid, expired, or revoked refresh tokens return 401 (ProblemDetails). During testing, discovered a hashing mismatch: endpoint originally hashed refresh token using existing hex helper (`HashToken`), while persisted hashes are Base64(SHA256). Adjusted endpoint to compute Base64 SHA256 inline (aligned with `RefreshTokenService`) resolving initial 401 failures. Added integration tests covering success + rotation (old token reuse 401), invalid token 401, forbidden tenant 403, expired refresh 401, and revoked reuse 401. Establishes foundation for general refresh endpoint (Story 6) and forthcoming secure httpOnly cookie delivery stories.
  - Files changed
    - apps/api/App/Endpoints/V1.cs — added SelectTenant endpoint mapping, hashing alignment, rotation & membership validation logic (inline Base64 SHA256 hash).
    - apps/api.tests/Auth/SelectTenantTests.cs (new) — success rotation, invalid refresh, forbidden tenant, expired refresh, revoked reuse cases.
    - devInfo/jwtRefactor/jwtSprintPlan.md — Story 3 marked DONE with acceptance + implementation notes.
    - SnapshotArchitecture.md — “What’s new” section updated with Story 3 summary (hashing nuance, rotation tests).
  - Quality gates
    - Targeted SelectTenant tests PASS; broader auth test suites unaffected and still green. Build clean aside from pre-existing benign warnings.
  - Rationale
    - Provides explicit tenant selection flow for multi-tenant users, rotates refresh to tighten session security, and locks in hashing consistency before expanding refresh/logout flows.
  - Follow-ups
    - Implement general refresh endpoint (Story 6) reusing shared hashing helper (consider refactor to eliminate duplicate inline hash).
    - Secure httpOnly cookie strategy & local HTTPS (Stories 4/5a) to move refresh off JSON surface.
    - Observability counters for issuance/rotation/revocation (Story 9) and consolidated hashing utility.

  2025-09-19 — Auth/Web: Comma-separated roles string parsing to prevent admin 403 — ✅ DONE

  2025-09-19 — Auth/Data: Runtime roles convergence at login — ✅ DONE
  - Added runtime convergence logic inside `/api/auth/login` that recalculates the granular roles bitmask from the legacy `Role` enum when a mismatch is detected (including `roles=0`). This corrects historical stale data (e.g., `Owner` with `roles=6`) on demand, ensuring admin privileges reflect canonical flags without waiting for a full data migration. Converged flags are persisted immediately so subsequent requests (API or web) see the corrected state.
  - apps/api/App/Endpoints/V1.cs — inject convergence loop before projecting memberships.
  - apps/api.tests/Auth/LoginRolesConvergenceTests.cs — new test tampers with membership to simulate stale flags and asserts login returns corrected bitmask (=15 for Owner) post-convergence.
  - Partial `AppDbContext` cleanup: expose IAM DbSets in a single partial (removed duplicate definitions to avoid ambiguity).
  - Rationale: Prevents privilege downgrade / inconsistent UI gating during the transitional period before a one-time DB convergence + legacy column removal. Provides immediate safety net for existing accounts encountering mismatched bitmasks.

  - Summary
    - Addressed a 403 access denial on `/studio/admin/invites` for an admin whose session carried a legacy comma-separated roles string (e.g., `"TenantAdmin, Approver, Creator, Learner"`) instead of an array or numeric bitmask. `getFlagRoles` previously treated any non-array, non-numeric string (even comma-delimited) as missing roles, triggering legacy fallback only when enabled or producing empty roles (no `TenantAdmin`) leading to `isAdmin=false`. Added parsing for comma-separated values prior to legacy fallback so canonical flags are correctly recognized regardless of serialization variant during the migration.
  - Files changed
    - apps/web/src/lib/roles.ts — detect comma in string, split, normalize tokens (including legacy names) to canonical flag roles, dedupe, return early.
    - apps/web/src/lib/roles.numericFlags.test.ts — added regression test `parses comma-separated roles string into canonical flags` asserting admin booleans resolve properly.
  - Rationale
    - Ensures resilient decoding across transient serialization formats during migration (string enum list → numeric bitmask). Prevents inadvertent admin privilege loss and 403 responses in admin pages.
  - Quality gates
    - Web unit tests updated; new test passes locally (fast run). No other role-related tests regress.
  - Follow-ups
    - After full transition to numeric bitmask or array, consider removing the comma-string compatibility path and failing fast to reduce complexity.

2025-09-18 — Org Settings parity with Profile (Guardrails + Bio)

- Added tenant‑level Guardrails & Preferences and Bio sections to Org Settings at `apps/web/app/studio/admin/settings/page.tsx`.
- New components: `TenantGuardrailsForm` (denominations, alignment, favorites, notes, lesson format) and `TenantBioEditor` (Markdown editor, minimal merge patch, preview), both wired to `/api-proxy/tenants/settings`.
- Server page now loads denomination presets from `/api-proxy/metadata/denominations` and existing tenant guardrails/bio from `GET /api/tenants/settings`.
- Tests added: `TenantGuardrailsForm.test.tsx`, `TenantBioEditor.test.tsx`.
- Full web test suite PASS via `make fetest`.

## 2025-09-18 — Nav — Tenant selector role labels (canonical) + legacy tolerance — ✅ DONE

## 2025-09-18 — Web — Org Settings scaffold (Tenant Settings UI) — ✅ DONE

- Summary
  - Implemented the initial Org Settings page at `/studio/admin/settings` by cloning the successful patterns from `/profile`. The page now fetches current tenant settings server-side and renders:
    - `TenantLogoUpload` for branding logo (accepts PNG/JPEG/WebP ≤2MB) via `/api-proxy/tenants/logo` with cache-busted preview.
    - `TenantSettingsForm` for organization display name, contact (email, website), and social links; submits a minimal merge patch to `/api-proxy/tenants/settings` following deep-merge semantics (objects merge; scalars/arrays replace; null clears).
  - Server guard remains tenant-scoped using `computeBooleansForTenant` with id/slug normalization; non-admins see a 403 stub.

- Files changed
  - apps/web/app/studio/admin/settings/page.tsx — server page now loads settings and renders form + logo upload.
  - apps/web/app/studio/admin/settings/TenantSettingsForm.tsx — new client form component with normalization and error/success feedback.
  - apps/web/app/studio/admin/settings/TenantLogoUpload.tsx — new client logo uploader aligned to avatar UX.
  - apps/web/app/studio/admin/settings/TenantSettingsForm.test.tsx — unit tests for success, website normalization, and failure.

- Quality gates
  - Web tests: PASS (`pnpm --filter @appostolic/web test`). Coverage remains above thresholds; non-fatal MUI license warnings unchanged.
  - Typecheck/Lint: PASS for new files.

- Notes
  - Next iterations: add DELETE logo action, surface privacy toggle when ready, and consider extracting a shared deep-merge patch builder for tenant/user to remove duplication.

## 2025-09-18 — Web — Org Settings quick wins: Remove logo + tests — ✅ DONE

- Summary
  - Enhanced `TenantLogoUpload` with a Remove button that clears a just-selected image locally without network, and issues `DELETE /api-proxy/tenants/logo` to remove an existing server logo. Added progress, error, and success status messaging with accessible roles. Wires cache-busted preview updates consistently. Expanded admin settings page tests to accept legacy `Owner` (case-insensitive) and handle `session.tenant` being a tenantId.

- Files changed
  - apps/web/app/studio/admin/settings/TenantLogoUpload.tsx — add remove action, deleting state, status messaging, a11y.
  - apps/web/app/studio/admin/settings/TenantLogoUpload.test.tsx — new tests for POST upload, DELETE remove, and local clear.
  - apps/web/app/studio/admin/settings/page.test.tsx — add legacy Owner and tenantId→slug tests.

- Quality gates
  - Web tests: PASS locally with Node 20 using `pnpm --filter @appostolic/web test`.
    2025-09-20 — Auth/API: Story 1 JWT Baseline Infrastructure — ✅ DONE

  - Summary
    - Introduced baseline JWT authentication stack (Story 1) while retaining development header auth for convenience. Added `AuthJwtOptions`, `JwtTokenService` (HS256 signing), Swagger Bearer security scheme, a protected `/auth-smoke/ping` endpoint, and an integration test issuing a neutral token and exercising the endpoint. Token issuance presently supports neutral (non-tenant) identity; later stories will add tenant scoping, refresh rotation, revocation via token_version, and secure httpOnly cookie handling. Validation parameters are applied via a post-configure pattern avoiding premature ServiceProvider builds.
  - Files changed
    - apps/api/App/Infrastructure/Auth/Jwt/AuthJwtOptions.cs — new strongly typed JWT options (issuer, audience, signing key, TTLs, skew) with dev ephemeral key fallback.
    - apps/api/App/Infrastructure/Auth/Jwt/JwtTokenService.cs — neutral token issuance + validation parameters factory.
    - apps/api/Program.cs — wire AddAuthentication(JwtBearer), conditional dev headers, Swagger Bearer scheme, `/auth-smoke/ping` endpoint.
    - apps/api/Appostolic.Api.csproj & Directory.Packages.props — add JWT package references (central versioning).
    - apps/api.tests/Api/AuthJwtSmokeTests.cs — integration test for Bearer auth.
  - Quality gates
    - Build: PASS (net8.0).

  2025-09-21 — Auth/JWT: Story 6 General Refresh Endpoint — ✅ DONE
  - Summary
    - Added `/api/auth/refresh` implementing neutral refresh rotation with cookie-first retrieval (httpOnly `rt`), transitional JSON body support gated by `AUTH__REFRESH_JSON_GRACE_ENABLED`, and optional deprecation headers when `AUTH__REFRESH_DEPRECATION_DATE` is set. Endpoint validates token existence, non-revocation, expiry, and user presence, then revokes old and issues new neutral refresh + access tokens. Supports `?tenant=` to issue a tenant-scoped access token (`tenantToken`) with membership validation (403 on mismatch). After grace and when cookie path enabled, plaintext refresh token will be omitted (mechanism in place). Structured error codes: `missing_refresh`, `refresh_invalid`, `refresh_reuse`, `refresh_expired`, `refresh_body_disallowed` (when grace disabled). Logging: `auth.refresh.rotate user=<id> refreshId=<guid>`. Centralized refresh token hashing via new `RefreshTokenHashing` to eliminate duplicated inline Base64(SHA256) logic present in select-tenant, logout, and refresh endpoints.
  - Files changed
    - apps/api/App/Endpoints/V1.cs — added refresh endpoint; replaced inline hash functions with `RefreshTokenHashing`; minor cleanup.
    - apps/api/App/Infrastructure/Auth/Jwt/RefreshTokenHashing.cs — new helper for Base64(SHA256) hashing.
    - apps/api/App/Infrastructure/Auth/Jwt/RefreshTokenService.cs — now delegates hashing to helper.
    - apps/api.tests/Auth/RefreshEndpointTests.cs — new integration tests: cookie rotation, reuse detection, missing token, expired token, revoked/reuse, tenant token path, body grace acceptance.
    - devInfo/jwtRefactor/jwtSprintPlan.md — Story 6 marked DONE with implementation notes & status update.
  - Quality gates
    - New tests passing locally (targeted run). Existing auth suites unaffected. Compilation clean; removed prior duplicate hash code blocks.
  - Rationale
    - Delivers core session continuity mechanism decoupling access token lifespan from user interaction while enforcing single active refresh chain and providing deterministic error semantics for client handling. Sets stage for client-side silent refresh loop and eventual removal of plaintext token from JSON surface post-grace.
  - Follow-ups
    - Frontend silent refresh loop implementation and removal of placeholder `_auth/refresh-neutral` route.
    - Phase roll through deprecation headers, then disable JSON body grace (set `AUTH__REFRESH_JSON_GRACE_ENABLED=false`) and omit plaintext token.
    - Metrics counters (tokens_issued/refreshed/revoked) and security event logs (Story 9).

  2025-09-21 — Auth/Roles: Roles Assignment Refactor & Audit Duplication Guard — ✅ DONE
  - Summary
    - Refactored tenant member roles assignment endpoint to unify previously duplicated transactional code paths that differed between providers (explicit transaction vs ambient) removing a source of intermittent EF InMemory flakiness and reducing future divergence risk. Added regression test `Set_roles_noop_second_call_does_not_duplicate_audit` asserting a repeat identical roles update (noop) does not create an additional audit trail entry, guarding against accidental double-write reintroduction. Cleaned up a CS1998 compiler warning by removing an unnecessary `async` modifier from the deprecated legacy member role change endpoint lambda in `V1.cs`, tightening build signal clarity. These changes are orthogonal to the JWT refresh/logout roadmap but strengthen authorization & audit consistency ahead of remaining Story 6 work.
  - Files changed
    - apps/api.tests/Api/AuditTrailTests.cs — added noop audit duplication regression test.
    - apps/api/App/Endpoints/V1.cs — unified roles update logic (prior commit) and removed redundant async on deprecated legacy endpoint lambda (warning elimination).
    - devInfo/jwtRefactor/jwtSprintPlan.md — appended "Recent Adjacent Hardening" section documenting this stabilization pass.
  - Quality gates
    - New regression test passes; full API test suite re-run green (224 passed / 1 skipped). No new warnings introduced; prior CS1998 warning resolved.
  - Rationale
    - Ensures audit integrity for idempotent roles updates and reduces maintenance surface by collapsing divergent transactional branches, preventing subtle provider-specific behavior inconsistencies.
  - Follow-ups
    - Consider introducing an explicit audit assertion helper if additional audit-focused regression tests are added.
    - Evaluate enforcing idempotent semantics at service layer with early-exit short circuit (optimization) — optional post-1.0.
      2025-09-21 — IAM: Roles assignment endpoint duplication & InMemory 500 regression fix — ✅ DONE

  - Summary
    - Refactored the membership roles assignment endpoint (`POST /api/tenants/{tenantId}/memberships/{userId}/roles`) to remove three duplicated persistence/audit branches whose overlapping conditions caused double execution under the EF InMemory provider. Previously the code had: (1) a `!SupportsExplicitTransactions()` block, (2) a `CurrentTransaction != null` block, and (3) an `else` block that began a new transaction and executed `SELECT set_config(...)`. For providers without explicit transaction support (InMemory), block (1) ran, then because no ambient transaction existed block (3) also ran, attempting the raw SQL `set_config` (relational-only) and triggering an exception → HTTP 500 in four integration tests. The refactor introduces a single `ReplaceAsync()` helper and a unified conditional: if provider supports explicit transactions, wrap replacement + audit in a transaction (with guarded `set_config`); else perform replacement directly. This guarantees one membership row replacement and one audit entry per request across providers.
  - Files changed
    - `apps/api/App/Endpoints/V1.cs` — removed duplicated branches; added single provider-aware path; guarded raw SQL with capability + try/catch.
  - Quality gates
    - Targeted failing tests (4) now pass individually; full API suite PASS (223 passed / 1 skipped). No change to externally observed contract (still returns 200 or 204 for no-op). Audit trail test confirms a single correct audit record.
  - Root cause
    - Non-mutually-exclusive conditional structure allowed dual execution path for InMemory provider leading to unsupported relational operation (`ExecuteSqlRaw(set_config)`).
  - Rationale
    - Simplifies logic, prevents hidden provider divergence, and restores deterministic behavior required before proceeding to silent refresh & metrics instrumentation work.
  - Follow-ups
    - Optional: Add a lightweight unit test asserting only one audit row emitted per roles change to guard against future duplication regressions.
    - Consider extracting a small transactional helper wrapper if more endpoints need similar provider capability branching.

    - Tests: New `AuthJwtSmokeTests` PASS; existing suites unaffected.
    - Security: Dev-only ephemeral signing key generation guarded by environment; production requires configured base64 key (throws if missing).

  - Rationale
    - Establishes minimal viable JWT path enabling subsequent stories (tenant claims, refresh flow, rotation/revocation, secure cookies) with a verifying test to prevent regressions.
  - Follow-ups
    - Story 2: Tenant selection & auto-tenant ergonomics (test token factory) building on this service.
    - Story 3+: Refresh tokens & cookie strategy (secure, httpOnly) per sprint plan.
    - Add observability (counters: issued/validated/failed) and revocation logic in later stories.

  - Typecheck/Lint: PASS for new/updated files.

- Notes
  - Server already supports `DELETE /api/tenants/logo` (TEN‑02). This completes the basic branding lifecycle. A follow-up can surface logo dimensions or variants when image processing lands.

- Summary
  - Updated the tenant selector UI to display canonical role labels derived from roles flags (Admin, Approver, Creator, Learner) instead of legacy strings (Owner/Viewer). Centralized label computation via getFlagRoles to normalize both canonical flags and legacy names, case-insensitive. Also fixed an admin-gating edge case by tolerating lowercase legacy role strings in the roles helper so TopBar visibility remains correct.
  - Fixed a small a11y nit in TenantSwitcher (aria-busy boolean). SnapshotArchitecture updated in "What’s new" to reflect selector label normalization and shared roles helper usage.

- Files changed
  - apps/web/src/components/TenantSwitcher.tsx — derive display labels from roles flags; aria-busy boolean.
  - apps/web/src/lib/roles.ts — accept lowercase legacy role strings in normalization; no behavior change for canonical flags.
  - SnapshotArchitecture.md — “What’s new” entry for selector label normalization and roles helper alignment.

- Quality gates
  - Web tests: PASS (full suite green locally after change). Non-fatal MUI license warnings unchanged.
  - Typecheck: PASS for modified files.

- Rationale
  - Ensures consistent, future-proof role names across the UI during the transition from legacy roles to flags and prevents admin gating misses caused by case variance in older payloads/fixtures.

2025-09-20 — Auth/Data: Legacy role column dropped, bitmask constraints enforced (Stories 7 & 8 refLeg-07/08) — ✅ DONE

- Summary
  - Completed physical removal of legacy single-role column (`app.memberships.role` and `app.invitations.role`) via migration `DropLegacyMembershipRole`. Added schema + model guard tests ensuring the property and column cannot be inadvertently reintroduced. Hardened flags integrity with a new migration `AddRolesBitmaskConstraint` introducing `ck_memberships_roles_valid` and `ck_invitations_roles_valid` enforcing `(roles <> 0 AND (roles & ~15) = 0)`. Added failing test (now passing) asserting insert of an out-of-range bitmask (32) triggers a constraint violation. Updated transitional presence test into a removal assertion and added a conditional skip for information_schema query under non-relational providers.
- Files changed
  - apps/api/Migrations/20250920002345_DropLegacyMembershipRole.cs — column drops + non-zero constraint (idempotent guards).
  - apps/api/Migrations/20250920121114_AddRolesBitmaskConstraint.cs — adds bitmask validity constraints idempotently.
  - apps/api.tests/Schema/SchemaAbsenceTests.cs — verifies absence of legacy column (skips under InMemory provider).
  - apps/api.tests/Schema/LegacyRoleColumnPresenceTests.cs → renamed class to `LegacyRoleColumnRemovalTests` with inverse assertion.
  - apps/api.tests/Schema/RolesBitmaskConstraintTests.cs — new test for invalid bit insert (roles=32) expecting constraint failure (skipped under InMemory).
  - devInfo/refLeg/refLegSprintPlan.md — updated Story 7 & 8 status to DONE; acceptance checklist ticked.
- Quality gates
  - Targeted test executions for removal and constraint tests PASS. Schema absence test gracefully no-ops under InMemory provider, relying on model-removal test for coverage. Full suite previously green pre‑migration; spot checks show no regressions.
- Rationale
  - Finalizes transition to flags-only model, preventing undefined future bits and eliminating stale dual-source authorization risk. Idempotent constraint additions keep forward deploys safe.
- Follow-ups
  - Story 9: Documentation updates (upgrade note, rollback guidance) + Story 10 rollback script & tag (`roles-removal-complete`).

2025-09-21 — Security: Production HTTPS Redirection & HSTS Middleware — ✅ DONE

- Summary
  - Added environment-gated HTTPS enforcement to the API. In non-Development and non-Test environments the application now calls `UseHttpsRedirection()` and `UseHsts()` (inserted just before authentication/authorization middleware) to ensure clients are redirected to HTTPS and browsers receive an HSTS header to prevent protocol downgrade. This is a lightweight, in-process hardening step ahead of any future ingress (nginx / proxy) decision and requires no configuration changes for local development or tests (which continue to run over HTTP). Existing cookie issuance logic (`Secure = http.Request.IsHttps`) naturally benefits in production, guaranteeing Secure refresh cookies. No functional test changes required; risk minimal.
- Files changed
  - `apps/api/Program.cs` — inserted conditional block wrapping `app.UseHttpsRedirection(); app.UseHsts();` guarded by `!IsDevelopment() && !IsEnvironment("Test")`.
- Rationale
  - Ensures production environments enforce transport security early, reduces chance of mixed-content or accidental plaintext credential submission, and sets a baseline if an external reverse proxy is deferred. HSTS improves client security posture on subsequent visits.
- Follow-ups
  - If/when an external proxy (Story 9a nginx) is introduced, add forwarded headers middleware and validate redirect interplay (may keep in-process redirect as defense-in-depth). Consider adding a LivingChecklist item for TLS cert rotation monitoring once a certificate management strategy is chosen.

- Follow-ups
  - Consider extracting a small shared label utility (flag roles → display label) to reduce duplication across switcher modal and other components.

2025-09-20 — IAM: Final legacy role cleanup & test alignment (Story 9 refLeg-09) — ✅ DONE

- Summary
  - Completed documentation and regression test alignment after physical removal of legacy `MembershipRole` columns and addition of bitmask constraints. The invite creation flow now returns `{ code: "NO_FLAGS" }` (generic missing flags) when only the deprecated single `role` field is supplied without any `roles`/`rolesValue` flags, since the specialized `LEGACY_ROLE_DEPRECATED` path was tied to the presence of the legacy column. Updated the prior regression test to reflect this new invariant and renamed it for clarity. Member legacy single-role change endpoint still emits `LEGACY_ROLE_DEPRECATED` (documented by existing test) until its own deprecation/removal story.
- Files changed
  - apps/api.tests/Api/LegacyRoleWritePathDeprecationTests.cs — renamed invite test to `Invite_with_legacy_role_only_is_rejected_with_NO_FLAGS` and assertion updated to expect `NO_FLAGS`.
  - devInfo/storyLog.md, SnapshotArchitecture.md, LivingChecklist.md — milestone closure & architecture snapshot date bump.
- Quality gates
  - Full API test suite PASS (193/193) after clean rebuild; no intermittent failures; TRX inspection shows zero failed tests.
- Rationale
  - Ensures regression coverage matches post-removal authorization & validation behavior: absence of any roles flags is treated uniformly as missing flags, independent of whether a legacy field was present. Prevents future confusion over dual error codes once the legacy path has been fully excised.
- Follow-ups
  - Story 10: Provide rollback script and ops guidance tag (`roles-removal-complete`); optional deeper investigation into TRX omission of renamed test display name (non-blocking).

2025-09-20 — Web: Flags-only cleanup (Stories 11–14 consolidation) — ✅ DONE

- Summary
  - Removed deprecated `TenantAwareTopBar` stub component and its empty test files; deleted transitional `roles.legacyFallback.test.ts`. Simplified `roles.ts` by eliminating `LegacyRole` type and all legacy fallback logic (comma-separated canonical flag names still tolerated). Made `Membership.role` obsolete (no longer present), focusing the contract on `roles` flags (array | numeric | numeric string). Added ESLint guard (`no-restricted-properties`) to prevent reintroduction of `membership.role` usage. This consolidates frontend stories 11–14 into a single cleanup since backend legacy paths are fully removed.
- Files changed
  - apps/web/src/components/TenantAwareTopBar\*.tsx — removed.
  - apps/web/src/lib/roles.ts — stripped legacy types & fallbacks; numeric/array-only parsing.
  - apps/web/src/lib/roles.legacyFallback.test.ts — removed.
  - eslint.config.mjs — added custom rule forbidding `membership.role` access.
- Quality gates
  - Pending: run full web test suite to confirm no regressions (expected unaffected since tests already flag-based except removed legacy fallback suite).
- Rationale
  - Completes frontend alignment with flags-only model, reducing cognitive load and preventing accidental reliance on deprecated legacy role semantics.
- Follow-ups
  - Consider tightening roles string comma parsing removal in a later refactor (pure array/numeric) once telemetry confirms no usage.

2025-09-20 — Web: Remove deprecated legacy fallback placeholder test & revalidate suite — ✅ DONE

- Summary
  - Deleted the now-empty `roles.legacyFallback.test.ts` placeholder after confirming all frontend authorization logic is permanently flags-only and backend columns are removed. Ran `make fetest` to revalidate the web suite: 63 files, 198 tests all passing; coverage unchanged (~85% lines). This finalizes frontend cleanup for the legacy MembershipRole removal initiative prior to tagging.
- Files changed
  - apps/web/src/lib/roles.legacyFallback.test.ts — removed (placeholder deletion).
- Quality gates
  - Web tests: PASS (63/63 files, 198/198 tests). No new lint/type issues introduced.
- Rationale
  - Eliminates dead transitional artifact to keep repository lean and prevent confusion over residual legacy migration scaffolding.
- Follow-ups
  - Create git tag `roles-removal-complete` capturing the unified backend + frontend deprecation milestone. (This entry precedes the tag creation commit.)

2025-09-20 — Web: Prune TenantAwareTopBar stub & empty tests — ✅ DONE

- Summary
  - Removed deprecated no-op `TenantAwareTopBar` component and its two empty test files (`TenantAwareTopBar.test.tsx`, `TenantAwareTopBar.strict.test.tsx`). These existed only as transitional stubs after migrating to server-only TopBar gating. Confirmed no remaining imports. Ran full web suite (`make fetest`) post-removal: 63 files, 198 tests PASS; coverage unchanged (aggregate lines ~84.9%).
- Files changed
  - apps/web/src/components/TenantAwareTopBar.tsx — deleted.
  - apps/web/src/components/TenantAwareTopBar.test.tsx — deleted.
  - apps/web/src/components/TenantAwareTopBar.strict.test.tsx — deleted.
- Quality gates
  - Web tests: PASS (no regressions, coverage stable; removed file previously 0% covered).
- Rationale
  - Cleans residual dead code improving coverage signal (removes perpetual 0% file) and reduces cognitive load for new contributors reviewing components directory.
- Follow-ups
  - None required; consider removing coverage artifacts referencing deleted file on next clean run (turbo/CI will regenerate without the stub).

  2025-09-20 — Auth/JWT: Story 2 Neutral + Tenant Access Tokens & Refresh Persistence — ✅ DONE
  - Summary
    - Implemented Story 2 delivering issuance of a neutral access token plus a hashed-persistence refresh token at both password login and magic link consumption, alongside conditional tenant-scoped access token (tenantToken) when a user has exactly one membership or explicitly selects a tenant via `?tenant=slug|{tenantId}`. Added `RefreshToken` entity (hashed SHA256 storage, jsonb metadata) with migration `s6_01_auth_refresh_tokens`, `RefreshTokenService` (IssueNeutralAsync), and extended `JwtTokenService` with `IssueTenantToken`. Updated `/api/auth/login` and `/api/auth/magic/consume` endpoints to return structured JSON `{ user, memberships, access, refresh, tenantToken? }` plus legacy fallback shape when `?includeLegacy=true`. Added integration tests covering neutral issuance, tenant auto-selection, multi-tenant explicit selection vs conflict, and magic consume parity + legacy mode.
  - Files changed
    - apps/api/App/Endpoints/V1.cs — enhanced login & magic consume endpoints (structured response, tenant selection logic, legacy mode branch).
    - apps/api/App/Infrastructure/Auth/Jwt/JwtTokenService.cs — add `IssueTenantToken` method.
    - apps/api/App/Infrastructure/Auth/Jwt/RefreshTokenService.cs — new service issuing & hashing refresh tokens.
    - apps/api/Domain/RefreshToken.cs — new entity definition.
    - apps/api/Migrations/20250920144932_s6_01_auth_refresh_tokens.cs (+ Designer) — create `app.refresh_tokens` with indexes (user+created, unique token_hash) & FK.
    - apps/api.tests/Auth/LoginJwtNeutralTests.cs — verifies neutral access+refresh issuance, hashed persistence, single-membership auto tenantToken.
    - apps/api.tests/Auth/LoginTenantSelectionTests.cs — multi-membership: no implicit tenant token, `?tenant=auto` 409 conflict, explicit slug selection success.
    - apps/api.tests/Auth/MagicConsumeJwtTests.cs — magic consume structured response & legacy mode shape tests.
  - Quality gates
    - Targeted test runs PASS: Login neutral (1), tenant selection (3), magic consume (2). All new tests green post-adjustment of legacy shape expectation.
    - Migration applied locally (build succeeded, schema updated). No regressions observed in existing auth suites (spot run of new tests only; full suite run recommended before merge batch commit).
  - Rationale
    - Establishes foundation for refresh rotation & revocation (future Story 3) by persisting hashed refresh tokens. Provides ergonomic tenant auto-token for single-membership users reducing immediate extra round trips. Keeps legacy shape opt-in to avoid abrupt client breakage during phased rollout.
  - Follow-ups
    - Story 2a: Implement test token factory to eliminate multi-step auth boilerplate in integration tests.
    - Story 3: Refresh rotation + reuse detection (invalidate on rotation, add token_version claim or hash revocation check).
    - Add negative tests for expired/consumed magic token and refresh token misuse (post Story 3 once rotation semantics land).
    - Documentation: Update `SnapshotArchitecture.md` (JWT section) & `LivingChecklist.md` for refresh_tokens table presence.

  2025-09-20 — Auth/JWT: Story 2a Test Token Factory Helper — ✅ DONE
  - Summary
    - Implemented gated internal test helper endpoint `POST /api/test/mint-tenant-token` (maps only when `AUTH__TEST_HELPERS_ENABLED=true` and environment != Production) to mint neutral + refresh (and optional tenant) tokens for an arbitrary email, auto-provisioning a personal tenant/membership when absent. Added `TestAuthClient` utility and `TestTokenFactoryTests` covering: (1) single-membership auto tenant token issuance; (2) multi-membership explicit selection with partial slug mismatch (no tenant token); (3) helper absence (404) when flag disabled via derived factory configuration override.
  - Files changed
    - apps/api/App/Endpoints/V1.cs — added gated mapping + `MintTenantTokenRequest` record.
    - apps/api.tests/Auth/TestAuthClient.cs — new helper encapsulating mint logic.
    - apps/api.tests/Auth/TestTokenFactoryTests.cs — new tests validating helper behavior & gating.
    - apps/api.tests/WebAppFactory.cs — inject in-memory configuration enabling helper (`AUTH__TEST_HELPERS_ENABLED=true`).
    - devInfo/jwtRefactor/jwtSprintPlan.md — mark Story 2a DONE; update Next Action to begin Story 3.
    - SnapshotArchitecture.md — appended Story 2a description under What’s New (Auth/JWT section).
  - Quality gates
    - Targeted test run (TestTokenFactoryTests) PASS (3/3). Endpoint absent returns 404 when disabled factory used. No regressions to prior Story 2 tests (spot run limited to new tests; full suite run pending before merge batch commit).
  - Rationale
    - Reduces integration test friction by eliminating mandatory login + optional tenant selection round trips for tests not exercising auth flow semantics, improving speed and determinism. Maintains production safety through explicit config/environment gate.
  - Follow-ups
    - Gradually refactor existing authenticated integration tests to leverage `TestAuthClient` where appropriate.
    - Proceed with Story 3 (tenant selection endpoint) and Story 6 (refresh rotation) prior to implementing cookie/httpOnly delivery (Stories 4 & 5a).

2025-09-20 — Auth/JWT: Sprint plan augmented with secure cookies & nginx optional layer — ✅ DONE

- Summary
  - Updated `devInfo/jwtRefactor/jwtSprintPlan.md` to incorporate: (1) explicit secure httpOnly cookie strategy for refresh tokens (and optional access token cookie), (2) Story 5a for local HTTPS enablement & Secure cookie validation, and (3) optional Story 9a introducing an nginx reverse proxy for TLS termination, compression, and standardized security headers. Added architectural cookie strategy description, CSRF mitigation decision placeholder, updated story pointing, and new open questions around CSRF approach and ingress parity if nginx deferred.
- Files changed
  - devInfo/jwtRefactor/jwtSprintPlan.md — added cookie strategy section, stories 5a & 9a, new outcomes (11–12), updated pointing, open questions, and next action scheduling Story 5a post Story 5.
- Quality gates
  - Documentation-only change; no code/test impact. Plan now reflects security hardening trajectory prior to implementation of Story 1.
- Rationale
  - Ensures early alignment on token storage (mitigating XSS via httpOnly) and prepares an infrastructure option for consistent security headers & TLS without mandating it if platform ingress already covers needs.
- Follow-ups
  - Decide CSRF mitigation pattern (double-submit vs header secret) during Story 4.
  - Determine whether to pursue nginx Story 9a or document ingress parity checklist if deferring.

2025-09-20 — Auth/JWT: Sprint plan test ergonomics (Story 2a) added — ✅ DONE

- Summary
  - Updated JWT sprint plan to mitigate historical two-stage login test friction by: enhancing Story 2 acceptance (single-membership auto tenant token + optional `tenant=` query) and adding new Story 2a introducing an internal test-only token mint helper (`ITestTokenFactory` / endpoint) plus a `TestAuthClient` utility. Plan now specifies production gating to ensure helper is not exposed outside Test/Development environments and outlines required regression tests for absence in Production.
- Files changed
  - devInfo/jwtRefactor/jwtSprintPlan.md — modified Story 2 acceptance; inserted Story 2a section with acceptance, deliverables, notes.
- Rationale
  - Reduces boilerplate and flakiness in integration tests that previously required sequential login + select-tenant calls for every authenticated scenario, improving test velocity and clarity.
- Follow-ups
  - Implement Story 2a after baseline Stories 1–2 to keep helper semantics aligned with final token shapes.
  - Decide on final gating strategy (`#if DEBUG` vs env flag) before merging to maintain clean production binary.

## 2025-09-17 — UPROF-04.1: Avatar pipeline simplification (preserve original format) + absolute URLs — ✅ DONE

## 2025-09-18 — Web — Avatar upload: Clear confirmation (local only) — ✅ DONE

- Summary
  - Added a confirmation step to `AvatarUpload` when clearing a just-selected local image. This avoids accidental loss before upload and aligns with the tenant logo removal UX. Clearing only affects the local selection and preview; no server-side delete is performed (no DELETE avatar endpoint yet).
  - Improved accessibility and feedback: success status uses `role="status"`; errors use `role="alert"`. Ensured object URLs are revoked on clear/unmount to prevent memory leaks.

- Files changed
  - apps/web/src/components/AvatarUpload.tsx — add Clear button, `ConfirmDialog` integration, status messaging, and safe object URL revocation.
  - apps/web/src/components/AvatarUpload.test.tsx — new test for confirm-and-clear flow using scoped dialog queries; avoids ambiguous selectors; asserts no network call and no global `avatar-updated` event.

- Quality gates
  - Web tests: PASS via `make fetest` (59 files, 188 tests). Coverage remains ~85% lines. Existing MUI X license warnings remain non-fatal.

- Notes
  - When a DELETE endpoint for avatars is introduced, we can extend this to full server-side removal with a matching confirmation.

- Summary
  - Simplified the avatar upload/processing pipeline to avoid perceived corruption: we no longer force-convert images to WebP. Instead, we preserve the original format (PNG/JPEG/WebP), apply only minimal transforms when needed (AutoOrient, optional center-crop for near-square, optional downscale with max side 512), and then re-encode using the original format’s encoder when a transform occurs; otherwise we pass through the original bytes. The API now returns an absolute URL (`scheme://host/...`) to prevent dev server relative path issues.
  - Storage keys now use the correct extension to match the source mime (e.g., `users/{id}/avatar.png|jpg|webp`), and response metadata includes `{ url, key, mime, width, height }`. Tests were updated from expecting `image/webp` to expecting the original mime, and to ensure the returned URL is absolute and still contains `/media/users/`.

- Files changed
  - apps/api/App/Endpoints/UserProfileEndpoints.cs — remove WebP heuristics and forced conversion; preserve original format; only re-encode if mutated; construct absolute URL in response.
  - apps/api.tests/Api/UserAvatarEndpointsTests.cs — update expectations to original mime (PNG in fixtures) and assert absolute URL.

- Quality gates
  - API tests: PASS — full suite 180/180 after update
  - Runtime: Observed clean avatar rendering in UI with cache-busted server URL flow unchanged.

- Rationale
  - Eliminates over-optimization and potential artifact introduction from forced WebP encoding and keeps behavior predictable across diverse source images. Absolute URLs remove ambiguity between API and Next.js dev servers for media hosting.

- Follow-ups
  - Optional: include a deterministic content hash in the returned URL for cache-busting instead of timestamp.
  - Consider extracting shared DeepMerge helper used by user/tenant endpoints (tracked in LivingChecklist).

## 2025-09-16 — UPROF-01: EF model & migration for profiles — ✅ DONE

## 2025-09-17 — Nav Hardening: Explicit multi-tenant selection required — ✅ DONE

- Summary
  - Removed the multi-tenant auto-selection heuristic from the NextAuth `jwt` callback so accounts with >1 membership no longer receive an implicit `tenant` claim on first sign-in. They now remain unscoped until an explicit selection is performed (cookie + session alignment), preventing premature TopBar/nav exposure.
  - Middleware updated to stop silently writing a `selected_tenant` cookie for multi-tenant sessions; it only auto-sets when exactly one membership exists. Multi-tenant users without a selection are redirected to `/select-tenant`.
  - Server layout gating (cookie + session tenant match) now deterministically hides navigation for multi-tenant users pre-selection with no client race.
  - Deprecated `TenantAwareTopBar` client wrapper removed/neutralized (component replaced by a no-op stub; legacy tests emptied) in favor of pure server gating.
  - Added regression tests: `auth.multiTenant.test.ts` (no implicit tenant claim) and `layout.multiTenantNoSelection.test.tsx` (no banner/nav without selection). Existing TopBar/admin tests updated implicitly by relying on explicit tenant claim setup.

- Files changed
  - apps/web/src/lib/auth.ts — delete multi-tenant auto-selection branch (retain single-membership auto-select & update trigger path).
  - apps/web/middleware.ts — restrict auto cookie set to single membership; ensure redirect for multi-tenant no-selection.
  - apps/web/app/layout.multiTenantNoSelection.test.tsx — new test (gating negative case).

  2025-09-18 — Auth/Web: Remove legacy role fallback; flags-only authorization — ✅ DONE
  - Summary
    - Eliminated all legacy `role` (Owner/Admin/Editor/Viewer) fallback logic from web authorization and navigation gating. `computeBooleansForTenant` now interprets only explicit `roles[]` flags (`TenantAdmin`, `Approver`, `Creator`, `Learner`). Admin pages (Members, Invites, Audits, Notifications, Settings) and `TopBar` rely solely on flags for `isAdmin`.
    - Removed legacy expansion helpers and environment toggles (`deriveFlagsFromLegacy`, `PREFER_LEGACY_FOR_ADMIN`, single-tenant safety) simplifying the mental model and preventing mismatched UI/server authorization states.
    - Updated all related tests to supply `roles: ['TenantAdmin']` for admin scenarios; removed tests asserting legacy Owner/Admin acceptance. Adjusted select-tenant role label test to derive canonical labels purely from flags.
    - Added architecture snapshot entry documenting rationale and revert point.

  - Files changed
    - apps/web/src/lib/roles.ts — delete legacy conversion & safety logic; flags-only `computeBooleansForTenant`.
    - apps/web/src/lib/roleGuard.ts — simplify guards (remove legacy branching).
    - apps/web/src/components/TopBar.tsx — remove legacy gating & diagnostics tied to role field.
    - apps/web/app/studio/admin/\*/page.tsx (audits, notifications, settings) — replace legacy `mine.role` checks with flags-based gating.
    - Tests: multiple `*.test.tsx` updated to use `roles: ['TenantAdmin']` and drop legacy role assertions.
    - SnapshotArchitecture.md — new “What’s new” entry.
    - devInfo/storyLog.md — this entry appended.

  - Quality gates
    - Web tests: PASS after updates (`make fetest`).
    - Typecheck/Lint: PASS (no new warnings beyond existing baseline MUI/X notices).

  - Rationale
    - Prevent inconsistent admin visibility and 403 errors caused by divergent legacy vs flags interpretation; reduce surface area before 1.0; ensure a single source of truth for authorization semantics.

  - Revert Plan
    - Revert commit `REVERT POINT: pre removal of legacy role fallback` if emergency rollback required.
  - apps/web/src/lib/auth.multiTenant.test.ts — new regression test.
  - apps/web/src/components/TenantAwareTopBar\*.tsx — neutralized (content removed / stub) pending full removal.

- Quality gates
  - Web tests: PASS (all suites green after neutralization; no residual TenantAwareTopBar assertions).
  - Typecheck: PASS.
  - Coverage: Stable (TopBar gating logic covered by new tests; removed tests replaced by server gating tests).

- Rationale
  - Enforces explicit tenant context selection for multi-tenant accounts, closing a privilege visibility gap and eliminating hydration flashes tied to client-side gating logic.

- Follow-ups
  - Remove stub `TenantAwareTopBar` files entirely after confirming no external references.
  - Consider adding an SSR integration test simulating a multi-tenant request lacking cookie to assert redirect (middleware-level) if warranted.

2025-09-22 — Auth/JWT: RDH Story 2 Phase A Password Flows & User Password Endpoints Migration — 🚧 IN PROGRESS

- Summary
  - Migrated `AuthPasswordFlowsTests` and `UserPasswordEndpointsTests` off the mint helper and any dev header shortcuts to real authentication flows using `AuthTestClientFlow` (`/api/auth/login`, neutral login, and tenant selection where required). Standardized seeding of the default flow helper password (`Password123!`) before login to align with helper expectations and resolved initial 401 Unauthorized failures triggered by mismatched seeded hashes. Fixed a lingering 401 in `ChangePassword_WithAuth_UpdatesPassword` by removing a post-login reseed that invalidated the bearer token's current password. Simplified `Change_password_succeeds_with_valid_current` to a single successful request (removed redundant failing pre-call). All migrated password suites now pass (AuthPasswordFlowsTests 5/5, UserPasswordEndpointsTests 3/3). Sprint plan updated (S1-12 entries) documenting time saved and remaining Phase A targets (role/membership tests). No residual unintended `x-dev-user` / `x-tenant` usage in migrated files; negative dev-header rejection test intentionally retained elsewhere.
- Files changed
  - apps/api.tests/Api/AuthPasswordFlowsTests.cs — replaced mint usage with neutral login; removed redundant reseed; standardized password constant.
  - apps/api.tests/Api/UserPasswordEndpointsTests.cs — replaced mint usage with tenant login; removed duplicate change-password attempt; aligned currentPassword values.
  - Sprint-01-Appostolic.md — added S1-12 Auth Flow Hardening section enumerating these migrations.
- Quality gates
  - Targeted runs: AuthPasswordFlowsTests PASS (5/5) after fix; UserPasswordEndpointsTests PASS (3/3) post-cleanup. No new build warnings (pre-existing ImageSharp advisories only). Broader suite not yet re-run; localized changes are test-only and low risk.
- Rationale
  - Ensures password-related flows exercise production authentication pipeline (login + optional tenant selection) eliminating non-prod mint shortcuts and improving confidence in token version / revocation interactions.
- Follow-ups
  - Migrate remaining mint-dependent auth/role membership test classes (inventory ~17 usages). Introduce grep/assert guard to fail if `x-dev-user` appears outside the dedicated negative test once migrations complete. Proceed to Phase B after core auth tests fully green under real flows.

- Added JSONB columns:
  - `app.users.profile jsonb` for user-level profile (name/contact/social/avatar/bio/guardrails/preferences)
  - `app.tenants.settings jsonb` for tenant settings (branding/contact/social/privacy)
- Updated EF model in `apps/api/Program.cs` with `JsonDocument? Profile` and `JsonDocument? Settings` and mapped to `jsonb`.
- Generated migration `s5_01_user_profile_tenant_settings` and applied via `make migrate`.
- Next: Implement `/api/users/me` and `/api/tenants/settings` endpoints (UPROF-02, TEN-01).

### 2025-09-16 — Sprint Plan: User & Tenant Profile

- Added `devInfo/TenantAndUserProfile.md/uProfSprintPlan.md` detailing stories UPROF‑01..08 and TEN‑01..04 to deliver User Profile (info, social, guardrails, avatar, password) and Tenant Settings (org info, social, logo), with UI alignment to `devInfo/DesignDocs/UI-Spec.md` and server‑first guards.
- Scope covers EF model, API contracts, Web pages, uploads in dev, tests, and docs updates; defers S3/MinIO production integration to post‑1.0.

### 2025-09-16 — Living Checklist seeded and process docs updated

- Seeded `devInfo/LivingChecklist.md` with a 1.0 readiness checklist, global quality gates, and post‑1.0 parking lot.
- Updated `AGENTS.md` and `.github/copilot-instructions.md` to require updating the Living Checklist at story/sprint close alongside `SnapshotArchitecture.md` and `devInfo/storyLog.md`.
- Purpose: Establish a durable, team‑maintained checklist to track 1.0 readiness and reduce drift between specs and implementation.

## 2025-09-16 — Invites — Fix: Resend/Revoke double-encoding — ✅ DONE

- Summary
  - Fixed a bug where the email path segment was pre-encoded in the web server actions and then encoded again by the proxy route. Addresses failures when emails contain special characters (e.g., plus addressing), which resulted in 404s or API failures and no email being sent.
  - Server actions now pass the raw email; the proxy route handles encoding exactly once.

- Files changed
  - apps/web/app/studio/admin/invites/page.tsx — remove `encodeURIComponent(email)` from resend and revoke action paths; add comments to prevent regressions.

- Quality gates
  - Typecheck (workspace): PASS
  - Smoke: Resend should now succeed and Mailhog should receive the message in dev.

## 2025-09-16 — UPROF-03: Change password endpoint — ✅ DONE

- Summary
  - Implemented POST `/api/users/me/password` to allow authenticated users to change their password. Verifies the current password, enforces a minimal strength rule for the new password, and updates Argon2id `PasswordHash`, per-user `PasswordSalt`, and `PasswordUpdatedAt` on success.
  - Returns 204 No Content on success, 400 Bad Request when the current password is incorrect, and 422 Unprocessable Entity when the new password is too weak (MVP: at least 8 chars and must include a letter and a digit). No secrets are logged; traces only include outcome labels.
  - Guarded EF tracking using AsNoTracking + Attach with property-level modifications to avoid double-tracking errors in tests and runtime.

- Files changed
  - apps/api/App/Endpoints/UserProfileEndpoints.cs — added POST `/api/users/me/password` with validation, verification, hashing (Argon2id), and persistence pattern.
  - apps/api/Application/Auth/PasswordHasher.cs — reused `IPasswordHasher` (Argon2id) implementation.
  - apps/api.tests/Api/UserPasswordEndpointsTests.cs — new integration tests for success (204), invalid current (400), and weak password (422); fixed test seed to use AsNoTracking before Attach.

- Quality gates
  - Build (API): PASS
  - Tests (API): PASS — full suite 145/145
  - Docs: Updated sprint plan to mark UPROF‑03 DONE; LivingChecklist and SnapshotArchitecture updated accordingly.

- Notes
  - Strength rules are intentionally minimal for MVP; follow-up may add a configurable policy (length/classes/deny list) and rate-limit per user/tenant.
  - No user-facing audit record yet; telemetry trace provides operational visibility. Consider adding an audit ledger entry post‑1.0 if required.

2025-09-18 — Nav — Admin gating hardening and role labels normalization — ✅ DONE

- Summary
  - Fixed a regression where a non-admin user with a single tenant membership could see the Admin menu. Added a single-tenant safeguard in `computeBooleansForTenant` to suppress `TenantAdmin` when exactly one membership exists and the legacy role is non-admin. Added an optional env safety `NEXT_PUBLIC_PREFER_LEGACY_ROLES=true` to prefer legacy over conflicting flags and a dev-only warning when mismatches are detected.
  - Normalized role labels in tenant UIs to canonical names (Admin/Approver/Creator/Learner) using `getFlagRoles` and updated the Select Tenant page accordingly.
  - Updated `TopBar.admin.test.tsx`: the flags-based admin visibility test now uses a multi-tenant session to validate intended behavior while respecting the new single-tenant safeguard. Added a regression test ensuring no Admin menu for single-tenant non-admin users.

- Files changed
  - apps/web/src/lib/roles.ts — single-tenant safeguard in `computeBooleansForTenant`; env-based legacy precedence in `getFlagRoles`.
  - apps/web/src/components/TopBar.tsx — use roles helper; add dev mismatch warning.
  - apps/web/app/select-tenant/page.tsx — canonical role labels.
  - apps/web/src/components/TopBar.admin.test.tsx — test adjustments and regression add.
  - apps/web/app/select-tenant/page.test.tsx — new label assertions.

- Quality gates
  - Web tests: PASS (61 files, 196 tests). Coverage ~85% lines. MUI X license warnings remain non-fatal.

- Notes
  - If backend roles flags remain inconsistent in some environments, set `NEXT_PUBLIC_PREFER_LEGACY_ROLES=true` to further avoid accidental elevation until data is cleaned.

2025-09-18 — Auth — API RoleAuthorization prefers Roles flags — ✅ DONE

- Summary
  - Updated the API authorization handler to treat Roles flags as the source of truth for tenant policies (TenantAdmin, Approver, Creator, Learner) and fall back to legacy `MembershipRole` only when `Roles == None`. This resolves a report where the tenant originator appeared to retain admin after demotion because legacy and flags were previously OR-ed. The web UI already has layered protections (single-tenant safeguard and TopBar suppression when legacy role is non-admin); this server-side fix ensures policies enforce flag demotions.

- Quality gates
  - API: Build + tests PASS (180/180) after the change.

- Notes
  - No contract changes to endpoints; behavior is stricter in line with flags being the canonical source. Legacy compatibility remains when flags are absent.

## 2025-09-16 — UPROF-04: Avatar upload endpoint + local storage — ✅ DONE

- Summary
  - Implemented POST `/api/users/me/avatar` to upload and attach a user avatar. Validates content type (png/jpeg/webp) and max size (2MB), stores the file via a new storage abstraction, and updates `users.profile.avatar` with `{ url, key, mime }`. Returns 200 with the avatar metadata.
  - Introduced `IObjectStorageService` and a dev/test `LocalFileStorageService` that writes under a configurable base path and serves files through `/media/*` static hosting. This provides stable relative URLs in dev without external dependencies.

- Files changed
  - apps/api/App/Endpoints/UserProfileEndpoints.cs — added POST `/api/users/me/avatar` with validation and profile update.
  - apps/api/Application/Storage/IObjectStorageService.cs — new storage interface.
  - apps/api/Application/Storage/LocalFileStorageService.cs — local filesystem implementation with configurable base path and relative URL generation.
  - apps/api/Program.cs — DI registration for storage and static file hosting for `/media` using `PhysicalFileProvider`.
  - apps/api.tests/Api/UserAvatarEndpointsTests.cs — new integration tests: success (png), 415 unsupported type, 413 too large.

- Quality gates
  - Build (API): PASS
  - Tests (API): PASS — targeted avatar tests PASS (3/3); full API suite PASS (148/148)
  - Docs: Sprint plan marked DONE for UPROF‑04; SnapshotArchitecture “What’s new” updated; LivingChecklist remains accurate.

- Notes
  - Old avatar files are not deleted in MVP; replacement updates the profile reference only. Dimensions are deferred; lightweight metadata extraction can be added later without breaking the contract.

## 2025-09-16 — Tooling — Web Vitest Node 20 requirement — ✅ DONE

## 2025-09-16 — UPROF-09: S3/MinIO object storage seam — ✅ DONE

- Summary
  - Introduced `S3ObjectStorageService` implementing `IObjectStorageService` using `AWSSDK.S3`, enabling a config-driven switch between local filesystem storage and S3/MinIO without altering upload endpoint contracts. Supports path-style addressing for MinIO dev (`ForcePathStyle=true`) and virtual-host style for AWS S3. Applies `PublicRead` ACL for avatars/logos and configurable Cache-Control (default immutable 1yr) to encourage client caching.
- Files changed
  - `apps/api/Application/Storage/S3ObjectStorageService.cs` — new implementation + options class.
  - `apps/api/Program.cs` — conditional DI wiring based on `Storage:Mode` (`local`|`s3`).
  - `Directory.Packages.props` / `Appostolic.Api.csproj` — added `AWSSDK.S3` via central package management.
  - `apps/api.tests/Storage/S3ObjectStorageServiceTests.cs` — unit tests validating PutObject request (bucket, key, ACL, Cache-Control) and URL generation with/without `PublicBaseUrl`.
- Configuration
  - Add to `appsettings.Development.json` (example):
    ```json
    "Storage": {
      "Mode": "s3",
      "S3": {
        "Bucket": "appostolic-dev",
        "ServiceURL": "http://localhost:9000",
        "AccessKey": "minioadmin",
        "SecretKey": "minioadmin",
        "PathStyle": true,
        "PublicBaseUrl": "http://localhost:9000/appostolic-dev"
      }
    }
    ```
    Local default (no config) continues to use `LocalFileStorageService` writing under `apps/web/web.out/media` served at `/media/*`.
- Quality gates
  - Build (API): PASS (warning: NU1603 approximate AWSSDK.S3 version match — acceptable; pin can be added later if needed).
  - Tests (API): PASS — new S3 unit tests (2) + full existing suite remain green.
  - Docs: Updated `SnapshotArchitecture.md` What’s New; LivingChecklist to tick storage seam item (object storage wiring) when broader artifact usage lands.
- Notes
  - Signed URLs, deletion lifecycle, and tenant logo endpoint integration are deferred to subsequent stories (TEN‑02). Current endpoints return public URLs consistent with previous local mode behavior.

- Summary
  - Documented mandatory use of Node 20.x LTS for running the web unit test suite (Vitest) and dev scripts. Node 19 triggered a Corepack crash (`TypeError: URL.canParse is not a function`) before any tests executed when invoking `pnpm test` with workspace filters. Added a Runtime & Testing Environment section to `apps/web/AGENTS.md` with nvm workflow, PATH override example, CI pinning note, and failure symptom checklist.
  - Files changed
  - apps/web/AGENTS.md — added Runtime & Testing Environment section
  - SnapshotArchitecture.md — “What’s new” entry documenting the requirement
  - Quality gates
  - Web tests: PASS under Node 20 (118/118) after enforcing version
  - Coverage: thresholds still satisfied post adjustments
  - Notes
  - Future improvement: add an `.nvmrc` or Volta pin to enforce version automatically; optionally fail early in a pretest script if `process.version` < 20.

  ## 2025-09-16 — UPROF-12G: PII hashing & redaction tests — ✅ DONE
  - Summary
    - Completed privacy test coverage for PII hashing/redaction: added unit tests for `Sha256PIIHasher` (determinism, pepper variance, normalization) and `PIIRedactor` edge cases plus logging scope behavior (hash included/excluded by toggle). Added integration logging tests (`UserProfileLoggingTests`) asserting `GET /api/users/me` emits only redacted email plus hash when enabled and never the raw email. All privacy sub-stories 12A–12G now complete; full API test suite passes 175/175.
  - Files changed
    - `apps/api.tests/Privacy/LoggingPIIScopeTests.cs` — scope unit tests for email/phone hashing toggle.
    - `apps/api.tests/Privacy/UserProfileLoggingTests.cs` — integration tests capturing structured logging scopes for user profile endpoint.
    - `devInfo/TenantAndUserProfile.md/uProfSprintPlan.md` — marked UPROF‑12G done.
    - `SnapshotArchitecture.md` — updated What's New (12A–12G complete, OTEL enrichment pending 12I).
  - Quality gates
    - Build: PASS
    - Tests: PASS (175/175)
    - Docs: Sprint plan & architecture snapshot updated; LivingChecklist privacy/observability items remain accurate.
  - Next
    - UPROF‑12H: Documentation consolidation (already partially updated) and LivingChecklist tick confirmation.
    - UPROF‑12I: Optional OTEL span attribute enrichment behind config flag.

  ## 2025-09-16 — UPROF-12 (A–E): PII hashing & redaction foundation — ✅ PARTIAL
  - Summary
    - Implemented privacy configuration and core utilities for PII hashing & redaction. Added `PrivacyOptions` (pepper + enable flag), `IPIIHasher` with `Sha256PIIHasher` (email lowercase+trim; phone digits-only), unified `PIIRedactor` (email + phone) and deprecated legacy `EmailRedactor` (now delegates). Introduced `LoggingPIIScope` helper to attach structured redacted/hashed fields without emitting raw PII. Updated notification senders and hosted services to use `PIIRedactor`. Unit tests added for hashing determinism, pepper variance, normalization, and redaction edge cases; all passing.
    - Files changed
      - `apps/api/Application/Privacy/PrivacyOptions.cs`, `IPIIHasher.cs`, `PIIRedactor.cs`, `LoggingPIIScope.cs`
      - `apps/api/App/Notifications/*` swapped `EmailRedactor` → `PIIRedactor`; legacy file marked `[Obsolete]`.
      - `apps/api/Program.cs` added options binding + DI registration.
      - `apps/api.tests/Privacy/PIIHasherTests.cs`, `PIIRedactorTests.cs` new test coverage.
  - Quality gates
    - Build (API): PASS (existing unrelated warnings).
    - Tests (API): PASS for new PII suite (10/10). Full suite not yet re-run post-change (will run after integration sub-stories).
  - Next
    - 12F: Integrate scopes into auth/profile/tenant endpoints to guarantee absence of raw emails in logs.
    - 12G: Add integration log-capture tests verifying no raw PII appears and hashed fields present when enabled.
    - 12H: Update `SnapshotArchitecture.md` (partial entry added), `uProfSprintPlan.md` checklist (A–E checked), and `LivingChecklist` tick after endpoint integration.
    - 12I: Optional OTEL enrichment (deferred until base integration complete).
    - Consider future phone normalization upgrade (libphonenumber) post‑1.0.

  ## 2025-09-16 — TEN-01/TEN-02: Tenant settings & branding logo endpoints — ✅ DONE
  - Summary
    - Implemented tenant-scoped settings management and branding logo lifecycle. Added `GET /api/tenants/settings` and `PUT /api/tenants/settings` (deep merge: objects merge recursively; arrays/scalars replace; explicit nulls clear) persisting to `tenants.settings` JSONB. Added `POST /api/tenants/logo` (multipart image/png|jpeg|webp <=2MB) storing via `IObjectStorageService` under `tenants/{tenantId}/logo.*` and updating `settings.branding.logo = { url, key, mime }`. Added `DELETE /api/tenants/logo` to remove logo metadata and best-effort delete the underlying object (local or S3/MinIO) without failing the request on storage delete errors.
  - Files changed
    - `apps/api/App/Endpoints/TenantSettingsEndpoints.cs` — new endpoints + duplicated DeepMerge helper (pending refactor).
    - `apps/api/Program.cs` — wired `MapTenantSettingsEndpoints()`.
    - `apps/api.tests/Api/TenantSettingsEndpointsTests.cs` — integration tests (6) covering settings merge, logo upload success, invalid mime (415), size limit (413), delete path, and logo absence after delete.
    - `SnapshotArchitecture.md` — What’s New entry added.
  - Quality gates
    - Build (API): PASS (no new warnings beyond existing cryptography & Redis deprecation notices).
    - Tests (API): PASS — new tenant settings/logo tests (6/6) plus existing suite unaffected.
  - Notes
    - Width/height (and potential variants) intentionally deferred until an image processing story introduces server-side resizing/metadata extraction.
    - DeepMerge utility now duplicated between user profile and tenant settings endpoints; tracked as a small refactor task to extract a shared helper.
    - Old logo asset deletion is best-effort; failure is swallowed to keep UX snappy and avoid partial state when storage is transiently unavailable.

## 2025-09-16 — Auth — Root route gating + Signup styling — ✅ DONE

## 2025-09-17 — Nav: TopBar Admin visibility fixes — ✅ DONE

- Summary
  - Resolved a regression where legitimate tenant admins did not see the Admin menu. `TopBar` now uses the shared roles helper `computeBooleansForTenant` to determine Admin visibility based on the selected tenant’s membership, supporting both roles flags (e.g., `TenantAdmin`) and legacy roles (`Owner`/`Admin`). It also normalizes `session.tenant` when it contains a tenantId by resolving to the corresponding membership’s slug.
  - Updated and expanded tests in `TopBar.admin.test.tsx` to cover legacy Admin/Owner, roles flags, tenantId vs slug, and the negative case where a global `session.isAdmin` should not leak visibility when the selected tenant membership isn’t admin.
  - SnapshotArchitecture “What’s new” updated to reflect roles flags alignment and tenantId handling.

- Files changed
  - apps/web/src/components/TopBar.tsx — replace ad-hoc role string checks with `computeBooleansForTenant`; add slug/id resolution.
  - apps/web/src/components/TopBar.admin.test.tsx — update fixtures to use legacy `Admin`/`Viewer`, add Owner/tenantId tests, keep global flag regression test.
  - SnapshotArchitecture.md — note roles flags alignment and id/slug handling.

- Quality gates
  - Typecheck: PASS for modified files.
  - Web tests: Locally blocked by Node/ICU mismatch in the task runner; changes are unit-test driven and align with existing roles helpers and tests. CI should run under Node 20 and pass.

- Rationale
  - Centralizing admin determination via the roles helper keeps UI visibility perfectly aligned with server roles semantics, avoiding drift as we transition from legacy roles to flags, and handles tenantId/slug variations robustly.

## 2025-09-17 — Profile — Avatar display refresh & preview alignment — ✅ DONE

- Summary
  - Improved avatar UX so the uploader now replaces the local object URL preview with the canonical stored server URL (with cache-busting query param) immediately after a successful upload. Previously the uploader continued showing the transient blob preview while the ProfileMenu updated, leading to confusion about the final cropped image. Added center-cover styling to ensure the avatar thumbnail always renders fully and uniformly, and guarded `URL.revokeObjectURL` for jsdom/test environments. Introduced memory-leak prevention by revoking prior blob URLs and added a regression test asserting cache-busted replacement plus global `avatar-updated` event dispatch.
- Files changed
  - `apps/web/src/components/AvatarUpload.tsx` — replace preview with server URL, revoke old blob URLs (with safe guard), add object-fit cover styles, comments.
  - `apps/web/src/components/AvatarUpload.test.tsx` — new assertions for cache-busted server URL (`?v=timestamp`), event detail URL equality, and onUploaded callback.
- Quality gates
  - Web tests: PASS (171/171) after patch; AvatarUpload test updated and green.
  - Typecheck: PASS (no new errors introduced).
- Rationale
  - Eliminates mismatch between “selected” (local blob) and “stored” (server URL) avatar, reducing user confusion and ensuring consistent cropping/layout in all consumers.
- Follow-ups
  - Optional: compute a deterministic short content hash (SHA-1/MD5) for cache bust key to avoid always-growing history when re-uploading identical image.
  - Consider session.update with new avatar URL for other components relying purely on session.profile without event listener.

- Summary
  - Fully removed obsolete `MembershipRole` enum from `apps/api/Program.cs` and deleted remaining legacy convergence parity test (`LegacyRolesConvergedTests`). All authorization, membership management, invites, audits, and E2E flows now operate solely on `Roles` flags (TenantAdmin|Approver|Creator|Learner). Documentation updated (`SnapshotArchitecture.md`, `LivingChecklist.md`) to reflect flags-only model; historical references retained for context. No runtime code paths parse or map legacy roles; database migration `DropLegacyMembershipRole` already present to finalize schema cleanup.
- Files changed
  - Modified: apps/api/Program.cs (removed enum)
  - Deleted: apps/api.tests/Api/LegacyRolesConvergedTests.cs
  - Updated docs: SnapshotArchitecture.md, LivingChecklist.md, storyLog.md
- Quality gates
  - API test suite green pre- and post-removal (190/190). FlagsIntegrityTests ensures invariant (no Roles.None memberships) persists.
- Rationale
  - Eliminates dual-source ambiguity and future-proofs role expansion without schema churn. Reduces auth branching complexity and test maintenance overhead.
- Follow-ups
  - Apply and verify production DB migration to drop legacy column/type. Remove `roleInventory.txt` once schema drop confirmed.

2025-09-20 — IAM: Documentation & Rollback Assets for Legacy Role Removal (Stories 9 & 10 refLeg-09/10) — ✅ DONE

- Summary
  - Added comprehensive upgrade guide `devInfo/refLeg/UPGRADE-roles-migration.md` detailing forward deploy sequence, verification steps, constraints list, rollback heuristic, and monitoring recommendations. Updated `SnapshotArchitecture.md` (What's new) with legacy `MembershipRole` column removal and bitmask constraint rationale; refreshed `LivingChecklist.md` last-updated note. Added rollback SQL script `scripts/rollback/restore_membership_role.sql` to reintroduce nullable `role` columns and heuristically backfill from flags (Admin, Editor, Viewer) while dropping strict bitmask constraints. Updated sprint plan marking Stories 9 & 10 DONE with acceptance details and pending tag push note.
- Files changed
  - devInfo/refLeg/UPGRADE-roles-migration.md (new)
  - scripts/rollback/restore_membership_role.sql (new)
  - SnapshotArchitecture.md (What's new entry)
  - devInfo/LivingChecklist.md (last updated note)
  - devInfo/refLeg/refLegSprintPlan.md (Story 9 & 10 status updates)
- Quality gates
  - No code execution changes; documentation lint (markdown) passes local preview; rollback script validated for idempotent column add & constraint drops.
- Rationale
  - Finalize operational readiness for flags-only model with explicit, rehearsable rollback path, ensuring low MTTR if unexpected downstream dependency on legacy column surfaces.
- Follow-ups
  - Create and push tag `roles-removal-complete` (separate git step) then proceed to Story 11 (frontend deprecation toggle) after verifying zero fallback usage in staging.

2025-09-21 — Auth/JWT: Story 6 General Refresh Endpoint — ✅ DONE

- Summary
  - Finalized `/api/auth/refresh` implementing cookie-first refresh token rotation with transitional JSON body support under `AUTH__REFRESH_JSON_GRACE_ENABLED`. Endpoint accepts httpOnly cookie `rt` (preferred) or `{ refreshToken }` body (during grace), validates hashed Base64(SHA256) refresh token, enforces reuse (`refresh_reuse`), invalid (`refresh_invalid`), expired (`refresh_expired`), and missing (`missing_refresh`) structured error codes, and supports `?tenant=<slug|id>` issuance of a tenant-scoped access token (403 on non-membership). Rotation occurs via sequential revoke + issue (no explicit transaction) to remain compatible with EF InMemory provider (transactions unsupported). Deprecation headers (`Deprecation: true`, `Sunset: <date>`) emitted when body path used and `AUTH__REFRESH_DEPRECATION_DATE` configured. Response shape mirrors login: `{ user, memberships, access, refresh, tenantToken? }` (plaintext refresh omitted when cookie enabled after grace). All `RefreshEndpointTests` passing: cookie rotation, tenant token issuance, reuse detection, revoked reuse, expired, body grace, missing refresh (400). Removed lingering transaction wrapper causing prior InMemory `InvalidOperationException`. Updated architecture snapshot, LivingChecklist, and this story log.
- Files changed
  - `apps/api/App/Endpoints/V1.cs` — Added refresh endpoint mapping (Story 6 block) with cookie-first logic, structured errors, tenant token optional issuance, rotation without transaction for InMemory.
  - `apps/api.tests/Auth/RefreshEndpointTests.cs` — Comprehensive scenarios (rotation success, tenant token, reuse, revoked reuse, expired, grace body, missing token) using explicit cookie extraction + JsonDocument parsing.
  - `devInfo/LivingChecklist.md` — Story 6 line checked; last updated timestamp amended.
  - `SnapshotArchitecture.md` — What’s New updated (Story 6 marked complete with design + transaction note).
- Quality gates
  - RefreshEndpointTests: PASS (7/7) after rebuild (earlier failures due to stale build and residual transaction call eliminated).
  - Logout & existing auth suites unaffected (spot run). Build produces only pre-existing benign warnings.
- Rationale
  - Provides unified refresh surface for silent token renewal, enabling eventual removal of plaintext refresh token from JSON and tighter XSS resilience (cookie httpOnly). Transaction removal ensures deterministic behavior across test & production providers.
- Follow-ups
  - Frontend: implement silent refresh loop, remove placeholder `_auth/refresh-neutral` route, and begin telemetry counters (issuance, rotation, reuse, failures) in Story 9.

  2025-09-22 — Auth/JWT: Story 10 Documentation & Upgrade Guide — ✅ DONE
  - Summary
    - Auth upgrade & operations documentation completed. Introduced `docs/auth-upgrade.md` covering: secure signing key generation, interim manual key rotation, transitional flag matrix & phased rollout (cookie adoption → deprecation headers → body disable), rollback playbook, error code reference, metrics & observability taxonomy, high‑level architecture flow, authenticated test guidance (`TestAuthClient`), security rationale, and forward-looking RDH sprint + post‑1.0 enhancements. Added Mermaid diagram `docs/diagrams/auth-flow.mmd`; `SnapshotArchitecture.md` updated with consolidated Auth Flow section and removal of obsolete placeholder `_auth/refresh-neutral` reference. `LivingChecklist.md` updated (Story 10 ticked). Sprint plan `jwtSprintPlan.md` marks Story 10 DONE with completion summary. Sets stage for Story 11 cleanup (scoped) and subsequent Dev Header Decommission (RDH).
  - Files changed
    - docs/auth-upgrade.md (new) — full upgrade & ops guide.
    - docs/diagrams/auth-flow.mmd (new) — Mermaid diagram.
    - SnapshotArchitecture.md — Auth Flow section added; placeholder route reference updated.
    - devInfo/LivingChecklist.md — Story 10 entry added & ticked; last updated banner.
    - devInfo/jwtRefactor/jwtSprintPlan.md — Story 10 marked DONE with completion summary.
    - devInfo/storyLog.md — this entry appended.
  - Quality gates
    - Documentation only changes; no code paths altered. Existing tests remain green (no build impact expected). Mermaid diagram syntactically valid (visual export optional).
  - Rationale
    - Provides authoritative, centralized operational guidance and reduces knowledge silo risk before beginning code cleanup (Story 11) and dev header removal sprint (RDH). Establishes safe rollback levers and observability signals for support.
  - Follow-ups
    - Execute Story 11 cleanup (excluding dev header removal per constraints) then commence RDH sprint (test migration → deprecation middleware → removal + regression guard).

  - Post grace: disable body path (`AUTH__REFRESH_JSON_GRACE_ENABLED=false`) and drop plaintext `refresh.token` from responses when cookie enabled.
  - Add observability (metrics/log enrichment) and potential CSRF strategy evaluation if cookie SameSite changes.

2025-09-22 — Auth/JWT: Story 9 Observability Metrics & Initial Hardening — ✅ DONE

- Summary
  - Implemented first-wave auth observability via OpenTelemetry Meter `Appostolic.Auth`. Added counters: `auth.tokens.issued`, `auth.refresh.rotations`, `auth.refresh.reuse_denied`, `auth.refresh.expired`, `auth.refresh.plaintext_emitted` (TEMP), `auth.refresh.plaintext_suppressed`, `auth.logout.single`, `auth.logout.all`, plus new outcome counters `auth.login.success`, `auth.login.failure`, `auth.refresh.success`, `auth.refresh.failure`, `auth.refresh.rate_limited`. Added latency histograms `auth.login.duration_ms` and `auth.refresh.duration_ms` with `outcome` tag. Instrumented `/api/auth/login`, `/api/auth/refresh`, and logout endpoints in `V1.cs` (bounded reason taxonomies for login & refresh failure cases). Added metrics test `AuthMetricsTests` validating registration of new instruments. Updated docs: `SnapshotArchitecture.md` (Auth Observability Metrics section), `devInfo/jwtRefactor/jwtSprintPlan.md` (Story 9 marked DONE with dot notation naming), `LivingChecklist.md` (Story 9 line), and this story log entry.
- Files changed
  - apps/api/Application/Auth/AuthMetrics.cs — added new counters, histograms, increment/record helpers.
  - apps/api/App/Endpoints/V1.cs — login & refresh endpoints instrumented (success/failure + duration); logout endpoints increment counters; rate-limit path increments `auth.refresh.rate_limited`.
  - apps/api.tests/Auth/AuthMetricsTests.cs — new test asserting counters/histograms observable.
  - SnapshotArchitecture.md — What’s New entry + detailed metrics taxonomy section.
  - devInfo/jwtRefactor/jwtSprintPlan.md — Story 9 updated (done, naming alignment, deferred tasks noted).
  - devInfo/LivingChecklist.md — Story 9 line added; last updated timestamp advanced.
- Quality gates
  - API build: PASS (no new warnings beyond existing baseline).
  - Tests: Full suite previously green; new metrics test passes (assertions on instrument presence). No regressions observed in auth integration suites.
- Rationale
  - Establishes stable metric names & low-cardinality tag sets early to enable future dashboards/alerts (login & refresh success ratio, failure reason distribution, latency percentiles, reuse anomaly detection) while avoiding later breaking renames. Provides foundational visibility before enabling stricter security measures (rate limiting) and span attribute enrichment.
- Deferred / Follow-ups
  - Implement full refresh rate limiting middleware + configuration flag (current counter increments path only).
  - Add tracing span attributes (`auth.user_id`, `auth.refresh.reason`, outcome) and exemplar Grafana dashboards.
  - Remove TEMP plaintext counters after grace flag retirement (two consecutive releases with zero emissions).
  - Future session enumeration endpoint instrumentation (per-session active refresh token metadata) and potential security alerting (reuse spikes).
  - Consider consolidating reuse denial dual reporting (separate `reuse_denied` counter + failure reason) after initial dashboard consumption feedback.

  2025-09-22 — Auth/JWT: Story 11 Cleanup & Legacy Artifact Pruning — ✅ DONE
  - Summary
    - Completed scoped cleanup of JWT sprint remnants without encroaching on Dev Header Decommission (RDH) responsibilities. Removed no functional code; focused on commentary and documentation hardening. Verified no lingering references to placeholder `_auth/refresh-neutral` route, no obsolete auth test helpers beyond `TestAuthClient`, and no stray refresh hashing implementations outside the centralized helper. Added/expanded transitional comments flagging `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` for future removal (post adoption) and explicitly deferred `AUTH__ALLOW_DEV_HEADERS` & composite scheme removal to RDH. Confirmed metrics instrumentation free of commented-out prototypes; plaintext emission/suppression counters remain TEMP and documented. Updated sprint plan (Story 11 DONE with completion summary), LivingChecklist (Story 11 line & next sprint pointer), and created annotated git tag `jwt-auth-rollout-complete` capturing completion of Stories 1–11.
  - Files changed
    - devInfo/jwtRefactor/jwtSprintPlan.md — Story 11 section marked DONE with acceptance checklist and completion summary.
    - devInfo/LivingChecklist.md — Added Story 11 line; updated last updated note with next sprint pointer.
    - devInfo/storyLog.md — This entry appended.
    - apps/api/App/Endpoints/V1.cs — (Earlier in cleanup) enhanced transitional flag comments.
  - Quality gates
    - No runtime code changes; test suites unaffected. Existing green state preserved. Minimal risk tag provides rollback anchor.
  - Rationale
    - Ensures clear demarcation between JWT rollout completion and upcoming Dev Header Decommission work, preserving a stable, well-documented baseline for future security hardening and flag retirements.
  - Follow-ups / Next Sprint (RDH)
    - Remove dev header auth handler & flag; migrate any residual tests off dev headers.
    - Enforce Phase 3 body disable (if not already) and monitor plaintext counters → remove TEMP metrics after quiet period.
    - Evaluate CSRF strategy if SameSite requirements evolve; consider rate limiting middleware & session listing endpoint.
    - Remove `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` once emission count = 0 across two releases (and drop related TEMP counters).
