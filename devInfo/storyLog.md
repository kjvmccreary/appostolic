2025-09-22 — Auth/JWT / RDH: Story 2 Phase A AgentTasks Auth & Assertions Migration — ✅ PARTIAL
2025-09-22 — Docs / Architecture: Lean SnapshotArchitecture rewrite & tail purge — ✅ DONE

- Summary
  - Replaced historically accreted `SnapshotArchitecture.md` (previously >1000 lines with duplicated snapshots + "What’s new" changelog blocks) with a concise point-in-time architecture snapshot (15 sections, <250 lines). Removed all embedded historical narrative to eliminate context drift, duplication, and token overhead in AI/chat assisted workflows. Snapshot now serves strictly as an authoritative current-state reference; evolution narrative resides exclusively in this story log and git history.
  - Cleanup required multiple attempts: initial head-only patches left a large legacy tail; partial truncations produced duplicate end markers and interleaved sections. Final resolution performed a hard delete followed by full-file recreation to ensure zero residual content. Verified single end-of-snapshot marker and absence of historical tail via direct read.
  - Added maintenance guidance section (update snapshot <250 lines each story, move narrative here) and reinforced Dev Header Decommission (RDH) near-term story references without duplicating sprint plan detail. Purged prior "What’s new" section entirely (superseded by story log) to prevent future accidental re-expansion.
- Rationale
  - Reduces cognitive load, accelerates onboarding/context priming for new chats, avoids embedding stale or contradictory architectural statements, and decreases accidental prompt inflation costs. Centralizes change history in one location (`devInfo/storyLog.md`) enabling linear narrative without bloating the snapshot.
- Quality Gates
  - Post-rewrite line count well under 250 target. Grep confirms removal of prior duplicated headings (e.g., multiple "Quick Facts" / "Pointers" blocks). No markdown lint or build tooling errors (non-code doc).
- Follow-ups
  - Update sprint plan (Story 3 docs marked done; Story 4 acceptance criteria expanded) — completed same batch.
  - Update LivingChecklist to reflect snapshot lean compliance and Story 3 (deprecation middleware) documentation completion.
  - Proceed with Story 4 (physical removal) tasks; snapshot will be updated again only after handler removal to reflect single auth path (removal of composite scheme reference).
  - Consider adding a lightweight CI guard limiting snapshot file line count (<260) to prevent silent expansion (future tooling task).

2025-09-22 — Auth/JWT / RDH: Story 2 Phase A AgentTasks E2E Migration — ✅ DONE

- Summary
  - Migrated all AgentTasks E2E harness tests (`AgentTasksE2E_HappyPath`, `AgentTasksE2E_Concurrency`, `AgentTasksE2E_List`, `AgentTasksE2E_Allowlist`, `AgentTasksE2E_Golden`) from a bespoke always-authenticating `TestAuthHandler` + dev header injection (`x-dev-user`, `x-tenant`) to real authentication flows using `AuthTestClientFlow.LoginAndSelectTenantAsync`. Each test now seeds a password via the platform `IPasswordHasher` and exercises `/api/auth/login` + `/api/auth/select-tenant` to acquire tenant-scoped access, ensuring they traverse the production JWT pipeline (claims validation, token version checks, refresh rotation eligibility). Removed all direct dev header usage and deleted custom handler override blocks, eliminating an alternate auth path in high-level end-to-end coverage.
  - Introduced per-test in-memory database isolation (unique DB name) preserving existing determinism while ensuring no hidden coupling through the former static handler state. Differential polling logic retained; timing windows unchanged (HappyPath/List 10s, Concurrency 20s, Golden 30s). Golden projection test now validates against fixture using real flow-issued claims rather than artificial handler claims.
- Files changed
  - apps/api.tests/E2E/AgentTasksE2E_HappyPath.cs — Removed `TestAuthHandler`, dev headers; added password seeding + flow login/select helpers.
  - apps/api.tests/E2E/AgentTasksE2E_Concurrency.cs — Same migration; concurrent creation/poll logic unmodified besides auth path.
  - apps/api.tests/E2E/AgentTasksE2E_List.cs — Replaced dev header bootstrap; list pagination assertions unchanged (still rely on creation ordering + skip/take semantics).
  - apps/api.tests/E2E/AgentTasksE2E_Allowlist.cs — Flow auth; retained allowlist failure scenario with disallowed tool invocation, validating error trace still produced under real tokens.
  - apps/api.tests/E2E/AgentTasksE2E_Golden.cs — Flow auth; removed custom handler and obsolete override code; retained projection to stable shape and comparison to fixture.
- Quality gates
  - Grep verification: no `x-dev-user`, `x-tenant`, or `TestAuthHandler` strings remain in `apps/api.tests/E2E/AgentTasksE2E_*` files. Compilation check (no handler symbol references). Flow helpers already validated in prior suites; E2E logic strictly additive to earlier auth path confidence.
  - Structural parity: DB seeding now explicit (tenant, user, membership) when absent; password seeded each run ensuring hash alignment with chosen default `Password123!` before login.
- Rationale
  - Removes final dependency on dev headers inside high-value E2E task execution scenarios, guaranteeing coverage of authentic user journey (password → login → tenant selection → agent task lifecycle) and preventing divergence once deprecation middleware blocks header usage (Story 3). Consolidates on single auth mechanism, reducing future maintenance of parallel handlers.
- Follow-ups
  - Consider extracting shared `SeedPasswordAndLoginAsync` helper to reduce duplication across five E2E files (post-migration cleanup bucket).
  - Add a full-suite run record before enabling deprecation middleware to capture baseline timings (optional metrics snapshot).
  - Proceed to remaining migrations (profile/logging tests) then implement temporary grep guard and Story 3 middleware.
- Snapshot / Docs
  - `rdhSprintPlan.md` Agent Tasks section will be updated to mark E2E tests complete. LivingChecklist pending an updated line referencing full AgentTasks migration completion.

- Summary
  - Migrated AgentTasks authentication and list/filter pagination tests away from legacy shortcut token bootstrap (`EnsureTokens` + static `TenantToken`) and brittle dev-header influenced assertions to real production flows. `AgentTasksAuthContractTests` now performs password seeding plus `AuthTestClientFlow.LoginAndSelectTenantAsync` for tenant-scoped access instead of relying on a pre-minted static token. In `AgentTasksListFilterPaginationTests`, removed non-deterministic assertions that assumed ≥2 tasks and email-based free-text matches (side-effects of dev header / seeding shortcuts). Rewrote free-text search test to assert only created task input substring presence and agent filter test to count actual `agentId` matches in returned JSON. Ensures AgentTasks suite fully exercises production JWT issuance (password hash verification, refresh issuance, tenant selection) with deterministic, data-owned assertions.
  - Conducted grep verification confirming removal of `x-dev-user`, `x-tenant`, `EnsureTokens`, and `TenantToken` references within AgentTasks tests. Build remains green; prior failing legacy assertion removed. Sets foundation to apply forthcoming deprecation middleware (Story 3) without residual hidden dependencies in this suite.
- Files changed
  - apps/api.tests/AgentTasks/AgentTasksAuthContractTests.cs — Removed `EnsureTokens`/`TenantToken`; added password seeding + login/select flow.
  - apps/api.tests/AgentTasks/AgentTasksListFilterPaginationTests.cs — Refactored free-text and agent filter tests; eliminated brittle ≥2 count/email assertions; deterministic matching logic added.
- Quality gates
  - Targeted run: contract test PASS (1/1). List/filter pagination tests compile; legacy failing assertion eliminated. Grep for `x-dev-user|x-tenant|EnsureTokens|TenantToken` returns no matches in AgentTasks scope. Build warns only on pre-existing ImageSharp advisories (no new warnings/errors).
- Rationale
  - Removes final reliance on dev header shortcuts in AgentTasks domain and stabilizes tests to reflect explicit created data rather than incidental seeding side-effects. Improves fidelity of coverage prior to disabling/removing dev header auth path and reduces flake risk.
- Follow-ups
  - Execute full AgentTasks suite run post broader Phase A migrations to confirm no hidden dependencies.
  - Introduce CI guard (grep) once remaining suites migrated to flag any reintroduction of removed helpers or dev headers.
  - Proceed with remaining migrations (privacy/logging, residual invites if any) then implement Story 3 deprecation middleware.
- Snapshot / Docs
  - Sprint plan `rdhSprintPlan.md` updated with progress entry (Story 2 Phase A AgentTasks partial). LivingChecklist update pending minor note addition.

2025-09-20 — Auth/JWT: Story 5a Local HTTPS & Secure Refresh Cookie Validation — ✅ DONE

2025-09-23 — Auth/JWT: Security Hardening Story 4 Dual-Key Signing Scaffold — ✅ PARTIAL

- Summary
  - Introduced dual-key JWT signing scaffold: added `AUTH__JWT__SIGNING_KEYS` (ordered list, first signs, all verify) with backward compatibility for legacy `AUTH__JWT__SIGNING_KEY`. Implemented key parsing in `AuthJwtOptions` (`GetSigningKeyBytesList`) and updated `JwtTokenService` to assign `kid` headers (first 8 bytes hex) and use `IssuerSigningKeyResolver` to validate tokens across all configured keys. Added health verification method `VerifyAllSigningKeys()` plus rotation integration tests (`DualKeySigningTests`) covering A → A,B → B (legacy A token rejected once removed). Ensures future rotations can proceed with a deterministic overlap period.
- Rationale
  - De-risks upcoming operational key rotations by validating multi-key verification logic before introducing production key changes. Establishes deterministic KID derivation without external dependency and sets foundation for metrics & health endpoint.
- Quality Gates
  - New tests: 2 passed (rotation lifecycle + health). No existing tests broken. SnapshotArchitecture updated with scaffold note. No public surface area modifications beyond config.
- Follow-ups
  - Add metrics (`auth.jwt.key_rotation.tokens_signed`, `auth.jwt.key_rotation.validation_failure`).
  - Optional internal endpoint `/internal/health/jwt-keys` exposing verification status.
  - Document rotation runbook (overlap, cutover, rollback) in sprint plan and runbook.
  - Integrate structured log event for failed validation attempts (if any) once metrics added.
  - Close Story 4 after metrics + docs; then proceed with Story 3 limiter enforcement (currently queued next).

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
      2025-09-23 — Auth/JWT / RDH: Story 6 Dev Header Decommission Documentation & Finalization — ✅ DONE

  - Summary
    - Completed documentation and cleanup phase of Dev Header Decommission (RDH). Added Upgrade Guide section 11.1 (migration/adaptation steps, rollback tag) and removed remaining dev header references from `apps/api/README.md`, `apps/web/README.md`, and `apps/api.tests/AgentTasks/README.md`. Updated sprint plan (`rdhSprintPlan.md`) to reflect completed LivingChecklist tick and README replacements; adjusted Story 6 checklist marking pending only story log (this entry), final grep sweep, and tag creation (tag to follow this commit: `dev-headers-removed`). LivingChecklist updated to mark dev header removal complete; architecture snapshot already reflected single-path JWT model (no wording changes needed). Ensures repos now have zero functional reliance on dev headers with only intentional historical references preserved in sprint plan and story log.
  - Rationale
    - Finalizes the decommission by eradicating stale operational guidance that could mislead contributors into using removed shortcuts, increases confidence in single-path auth assumptions, and provides an explicit adaptation guide for any external scripts or local workflows still expecting header-based impersonation. Establishes a permanent rollback boundary (`before-dev-header-removal`) and a forward-looking guard (grep script + negative-path tests) to prevent regression.
  - Quality Gates
    - Grep (pre-commit) shows no occurrences of `x-dev-user` or `AUTH__ALLOW_DEV_HEADERS` outside documented historical plan/story log allowlist. All existing tests previously green (239 passed / 1 skipped API + 1 HTTPS E2E). No code changes beyond docs; risk isolated to documentation clarity. Upgrade Guide section added without altering prior numbering except adding 11.1 subsection.
  - Rollback
    - Short-term rollback: checkout tag `before-dev-header-removal` (restores handler, composite scheme). Long-term rollback discouraged; adaptation guide favors real JWT flows. Post-tag creation `dev-headers-removed` will serve as forward reference for audits.
  - Follow-ups
    - Create annotated tag `dev-headers-removed` after merging this commit.
    - Optionally prune one redundant negative-path test file after a stability window.
    - Future: consider Roslyn analyzer to disallow introducing new custom auth handlers referencing dev headers.
  - References
    - `devInfo/jwtRefactor/rdhSprintPlan.md` (Story 6 section)
    - `docs/auth-upgrade.md` (Section 11.1)
    - `scripts/dev-header-guard.sh`
    - `SnapshotArchitecture.md` (Auth & Flags sections)

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
2025-09-22 — Auth/JWT / RDH: Story 2 Phase C Schema/Migration Audit — ✅ DONE

- Summary
  - Completed Phase C by auditing schema/migration-oriented test suite (`apps/api.tests/Schema/`) for any usage of dev headers (`x-dev-user`, `x-tenant`). Grep returned zero matches across `RolesBitmaskConstraintTests`, `LegacyRoleColumnPresenceTests`, and `SchemaAbsenceTests`. No changes required; phase is a documentation-only confirmation that no hidden dependencies exist in low-level schema validation tests. Sprint plan updated to mark Phase C complete with note citing empty audit result.
- Files changed
  - devInfo/jwtRefactor/rdhSprintPlan.md — Phase C checkbox marked complete with explanatory note.
- Quality gates
  - No code modifications to test logic; zero risk. Existing green suite unaffected.
- Rationale
  - Establishes certainty that forthcoming deprecation middleware (Story 3) and eventual handler removal (Story 4) will not impact schema/migration tests, reducing rollback risk and clarifying scope of remaining migrations.
- Follow-ups
  - Proceed to Story 3 (Deprecation Mode): introduce middleware rejecting dev headers with structured code, add metric counter, adapt negative-path tests to assert new response shape.
  - After stable soak, advance to Story 4 physical removal tasks.

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

  2025-09-22 — Auth/JWT: Dev Header Decommission Sprint (RDH) — Phase A Superadmin Elevation via Auth Flow — ✅ DONE
  - Summary
    - Replaced test-only superadmin mint helper usage with configuration-driven claim injection during normal `/api/auth/login` and `/api/auth/select-tenant` issuance. Added allowlist key `Auth:SuperAdminEmails` (seeded with `kevin@example.com` in test factory) which, when matched, appends `superadmin=true` claim via existing extra-claims overloads. Updated notifications production endpoint tests to authenticate using real password flow (`AuthTestClientFlow.LoginNeutralAsync`) for cross-tenant listing instead of `AuthTestClient.UseSuperAdminAsync`. Removed obsolete `UseSuperAdminAsync` helper. Adjusted resend authorization test to use a non-allowlisted user to preserve the forbidden cross-tenant assertion. All targeted tests pass (4/4) post-migration.
  - Files changed
    - apps/api/App/Endpoints/V1.cs — Inject superadmin claim on login & select-tenant when email is in allowlist.
    - apps/api.tests/WebAppFactory.cs — Added `Auth:SuperAdminEmails` test configuration setting.
    - apps/api.tests/Api/NotificationsProdEndpointsTests.cs — Switched superadmin test to real flow; updated resend test user to avoid unintended superadmin claim.
    - apps/api.tests/Auth/AuthTestClient.cs — Removed deprecated `UseSuperAdminAsync` helper.
  - Quality gates
    - Targeted notifications tests PASS (4/4). No remaining usages of `UseSuperAdminAsync`. Grep confirms zero references. Build succeeds locally; no new warnings introduced.
  - Rationale
    - Eliminates reliance on mint helper for role elevation, ensuring integration tests exercise only production auth surfaces and paving the way to remove the mint endpoint in later phases. Reduces duplication of superadmin logic across dev header & mint paths.
  - Follow-ups
    - Proceed with remaining Phase A tasks to eliminate any lingering mint usages for other elevated scenarios (if present).
    - Introduce guard to fail CI if deprecated helpers (`MintAsync` superadmin flag or removed methods) reappear.
    - Update `SnapshotArchitecture.md` in next story batch to reflect config-based elevation.

2025-09-22 — Auth/JWT: Dev Header Decommission Sprint Plan (RDH) — 🚧 IN PROGRESS

- Summary
  - Created `devInfo/jwtRefactor/rdhSprintPlan.md` detailing the Dev Header Decommission (RDH) sprint to fully remove development header authentication (`x-dev-user`, `x-tenant`) and the composite scheme from all environments and tests. Plan covers phased test migration to JWT helpers, deprecation middleware (temporary 401 `dev_headers_deprecated`), physical removal of handler & flag, regression guard (401 `dev_headers_removed` test + CI grep), documentation & rollback tag strategy, risks, acceptance criteria, and optional hardening follow-ups. SnapshotArchitecture updated to reference the new sprint. Next actionable stories: consolidate test token helpers, migrate integration tests off dev headers, introduce deprecation middleware, then remove code.
- Files changed
  - devInfo/jwtRefactor/rdhSprintPlan.md (new) — full sprint breakdown with stories 0–7, risks, rollback, matrix.
  - SnapshotArchitecture.md — What’s New entry referencing RDH sprint plan creation.
- Rationale
  2025-09-22 — Auth/JWT / RDH: Story 2 Phase B Domain/Feature Test Migration — ✅ DONE
  - Summary
    - Completed Phase B by migrating remaining feature/domain test relying on dev headers (`NotificationsE2E_Mailhog`) to the real authentication flow using `AuthTestClientFlow.LoginAndSelectTenantAsync` with password seeding and membership creation. Removed direct `x-dev-user` / `x-tenant` header injection in that E2E path. Annotated intentional negative-path guard suites (`DevHeadersDisabledTests`, `DevHeadersRemovedTests`) to explicitly exclude them from Phase B completion criteria—they continue to reference legacy header names solely to validate rejection behavior for upcoming deprecation and removal stories (Stories 3–5). Updated sprint plan to mark Phase B complete with explanatory parenthetical. No functional regressions; targeted test run of modified file passes. Grep now shows no remaining domain/feature tests (outside intentional negative-path guards) using dev header injection.
  - Files changed
    - apps/api.tests/E2E/NotificationsE2E_Mailhog.cs — Replaced dev header bootstrap with password seeding (in-memory DB) + login/select flow; added roles membership; removed header additions.
    - apps/api.tests/Auth/DevHeadersDisabledTests.cs — Added annotation comment clarifying exclusion from Phase B scope.
    - apps/api.tests/Auth/DevHeadersRemovedTests.cs — Added annotation comment clarifying future-stage guard purpose.
    - devInfo/jwtRefactor/rdhSprintPlan.md — Marked Phase B checkbox complete with explanatory note about intentional negative-path suites.
  - Quality gates
    - Targeted test (`NotificationsE2E_Mailhog`) passes post-migration. Full suite previously green; incremental change limited in scope. Grep for `x-dev-user` in tests now returns only guard suites as intended.
  - Rationale
    - Ensures all functional domain coverage now exercises the production JWT pipeline (password verification, refresh rotation eligibility, membership projection) eliminating reliance on dev-only auth shortcuts ahead of deprecation middleware (Story 3). Clear separation between functional coverage and regression guards reduces risk of accidental header reintroduction being mistaken for required negative-path tests.
  - Follow-ups
    - Story 3: Introduce deprecation middleware returning structured 401 for dev header usage and add metric counter.
    - Prepare Phase C (schema/migration tests audit) — early indication suggests minimal/no header usage; confirm via grep and mark accordingly.
    - Implement fail-fast CI assertion (grep-based) once Story 3 middleware live to enforce zero accidental header reinsertion.
  - Snapshot / Checklist
    - Sprint plan updated (Phase B complete). LivingChecklist to be updated next batch with Phase B status tick.

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
  - devInfo/jwtRefactor/rdhSprintPlan.md — progress log entry appended noting partial completion of Phase A.
- Quality gates
  - Both migrated suites green; no regressions observed in role enforcement (owner vs non-admin) or membership mutation side-effects. Build unchanged aside from existing advisory warnings (ImageSharp vulnerability pre-existing).
- Rationale
  - Incrementally shrinks dependency on test-only mint helper ensuring future removal (and dev header decommission) does not create gaps in membership/role coverage. Validates flow helper ergonomics before tackling larger suites (invites, audit, avatar uploads).
- Follow-ups
  - Next: migrate `MembersManagementTests` then invites/audit suites; introduce a guard to fail CI if `UseTenantAsync` remains after Phase A; subsequently plan mint helper removal and dev header physical decommission.

2025-09-22 — Auth/JWT: RDH Story 2 Phase A MembersManagementTests Migration — ✅ PARTIAL

2025-09-23 — Auth/JWT / Testing: Deterministic Auth Seeding Refactor — ✅ DONE

- Summary
  - Replaced fragile multi-step password → /api/auth/login → /api/auth/select-tenant setup sequences across non-auth integration suites with a deterministic auth seeding helper (TestAuthSeeder) that provisions user, tenant, membership (Owner or specified roles), and issues neutral or tenant-scoped access tokens directly. Migrated remaining legacy suites (UserAvatar, UserPassword, AgentTasks, AuditTrail, LegacyRoleWritePathDeprecation, UserProfileLogging) plus earlier migrated domains to use CreateOwnerClient / CreateAuthedClient patterns. Removed all incidental dependencies on select-tenant side-effects (source of earlier 403 regressions) while retaining a focused set of true end-to-end auth flow tests (login, select-tenant rotation, refresh, logout, cookie HTTPS, plaintext suppression, metrics, token version revocation). Eliminated 18 prior failing tests (16 authorization 403s, 2 missing-seed data issues); full API suite now green (239 passed / 1 skipped, 34s local arm64). E2E project (api.e2e) smoke remains intact validating real HTTPS cookie + JWT path.
- Rationale
  - Speeds test runs and improves determinism by collapsing multi-hop HTTP auth choreography into single in-process seeding for cases where auth mechanics are not under test, reducing brittleness and future refactor cost. Ensures domain tests fail only for domain logic regressions, not auth flow drift.
- Changes
  - Updated helper usage across affected test files to call CreateOwnerClientAsync / CreateAuthedClientAsync (tenant-scoped) or variants that also seed password hashes when password mutation endpoints are under test.
  - Consolidated unique identifier generation via GUID fragment helpers (candidate for follow-up centralization).
  - Verified no residual reliance on deprecated login/select-tenant in non-auth suites via grep; retained auth suites intentionally.
- Quality Gates
  - Full API test run: 239 passed / 0 failed / 1 skipped. E2E run: 1/1 passed. No new warnings except pre-existing ImageSharp advisories (pending remediation story).
- Follow-ups
  - Consolidate duplicated UniqueFrag/UniqueSlug/UniqueEmail helpers into a shared test utility.
  - Upgrade SixLabors.ImageSharp to patched version (advisories GHSA-2cmq-823j-5qj8, GHSA-63p8-c4ww-9cg7, GHSA-qxrv-gp6x-rc23, GHSA-rxmq-m78w-7wmc) after confirming green baseline.
  - Consider trimming SnapshotArchitecture back to <250 line lean goal (file has re-expanded) in a documentation hardening pass.
  - Add optional timing telemetry for seeded vs flow-based auth paths to track ongoing test performance.

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
    2025-09-23 — Testing: UniqueId Helper Consolidation — ✅ DONE

- Summary
  - Introduced centralized test identifier utility `UniqueId` (Frag, Slug, Email) under `apps/api.tests/TestUtilities/UniqueId.cs`, replacing scattered local helpers and ad-hoc inline Guid slicing across Avatar, AgentTasks, UserPassword, AuditTrail, and LegacyRoleWritePathDeprecation test suites. Provides uniform short hex fragments and sanitized slug/email composition.
- Rationale
  - Eliminates duplication and drift (different fragment lengths, domains, or formats), simplifies future guard enforcement, and standardizes on non-routable test email domain (`example.com`).
- Implementation
  - Added `UniqueId` static class with `Frag(int length=8)`, `Slug(string? prefix=null, int fragLength=8)`, and `Email(string? userPrefix=null, string domain="example.com", int fragLength=8)` plus basic sanitization. Existing test files already referencing a prior lightweight `UniqueId` were updated in-place earlier; this consolidation formalizes richer API & docs.
- Quality Gates
  - Spot build succeeds; grep for legacy helper names returns zero matches. No behavior changes to tests beyond identifier source.
- Follow-ups
  - Add guard test preventing reintroduction of `UniqueFrag`/`UniqueSlug`/`UniqueEmail` patterns.
  - Reference helper in `SnapshotArchitecture.md` Test Strategy section.
  - Consider collision counter/log (low priority; probability negligible for test scope).

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
    2025-09-23 — Auth/JWT: Superadmin Claim Parity & Notifications Cross-Tenant Guard — ✅ DONE

- Summary
  - Added configuration-driven superadmin claim parity to deterministic test token issuance (`TestAuthSeeder`) ensuring tokens created directly in tests mirror production `/api/auth/login` allowlist behavior (`Auth:SuperAdminEmails`). Implemented early explicit 403 guard in notifications listing endpoint when a non-superadmin supplies a foreign `tenantId` query parameter (previous behavior silently scoped results, obscuring unauthorized enumeration attempts). Resolved failing `SuperAdminAllowlistTests` (previously: non-superadmin received 200 + empty list, superadmin saw empty list due to missing claim). Post-change targeted tests pass (2/2) and a regression avatar test remains green.
- Files changed
  - SnapshotArchitecture.md — updated generation line noting superadmin claim parity & stricter cross-tenant guard.
  - (Earlier commit in same batch) apps/api.tests/Auth/TestAuthSeeder.cs — inject superadmin claim when email in allowlist.
  - (Earlier commit in same batch) apps/api/App/Endpoints/NotificationsAdminEndpoints.cs — early foreign tenantId explicit 403 for non-superadmin.
- Rationale
  - Ensures security test fidelity by keeping deterministic seeding path authoritative and preventing silent authorization downgrades that could mask policy drift. Strengthens principle of explicit denial over silent narrowing for cross-tenant administrative queries.
- Quality Gates
  - Targeted test run: SuperAdminAllowlistTests PASS (2/2). Regression test (avatar upload) PASS. Build warnings unchanged (no new warnings introduced). No impact observed on unrelated suites (spot run subset only; full suite pending next batch).
- Follow-ups
  - Evaluate other admin listing endpoints (DLQ, replay) for consistent explicit foreign tenant guard semantics.
  - Run full suite to confirm absence of regressions.
  - Document potential standardization decision in SnapshotArchitecture if broader adoption pursued.
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

2025-09-22 — Auth/JWT / RDH: Story 2 Dev Header Guard Script Added — ✅ DONE

- Summary
  - Introduced a CI-oriented guard script `scripts/dev-header-guard.sh` that scans the repository for deprecated development authentication artifacts: `x-dev-user`, `x-tenant`, `DevHeaderAuthHandler`, `AuthTestClient.UseTenantAsync`, and the transitional composite scheme identifier `BearerOrDev`. The script fails (exit 1) if any matches are found outside an explicit allowlist (current allowlist: guard test file, negative dev header tests, and documentation folders). This establishes proactive enforcement to prevent reintroduction of legacy dev header pathways as migrations progress and before deprecation middleware (Story 3) physically removes the code. The guard will later be wired into CI (pre-merge task) and the allowlist narrowed once negative coverage is updated post-removal.
- Files changed
  - scripts/dev-header-guard.sh — new Bash script implementing pattern scan with allowlist and structured failure output.
  - devInfo/LivingChecklist.md — expanded RDH Story 2 line enumerating migrated suites (AgentTasks, Members*, Assignments, Audit*, Invites*, UserProfile*, UserAvatar) and remaining targets (TenantSettings, ToolCatalog, DevGrantRoles, DenominationsMetadata).
- Quality gates
  - Local execution reports OK (no forbidden artifacts) except those present solely in allowlisted negative/guard tests and docs. Grep spot checks confirm migrated suites no longer contain `UseTenantAsync` or dev header strings; remaining usages confined to planned targets and guard scaffolding.
- Rationale
  - Provides an automated safety net ensuring the codebase continues moving toward total removal of dev header authentication without regression. Early introduction reduces risk of accidental new references during concurrent story work and clarifies remaining migration surface.
- Follow-ups
  - Integrate guard script into CI pipeline (e.g., Makefile target invoked in test or lint stage).
  - After migrating remaining suites, tighten allowlist (remove `UseTenantAsync` guard test) and escalate dev header negative tests to assert 401/`dev_headers_deprecated` then 404/`dev_headers_removed` in later stories.
  - Remove `BearerOrDev` pattern from scan list once composite scheme decommissioned; add middleware deprecation code patterns if needed.
- Snapshot / Docs
  - LivingChecklist updated; SnapshotArchitecture to be updated at Story 2 closure summarizing guard enforcement layer.

2025-09-22 — Auth/JWT / RDH: Story 2 Final Test Migrations & Helper Removal — ✅ DONE (Tests Phase)

- Summary
  - Completed remaining migrations off legacy mint helper / shortcut auth for: `TenantSettingsEndpointsTests`, `ToolCatalogTests`, `DevGrantRolesEndpointTests`, and `DenominationsMetadataTests`. Each suite now seeds the default password (`Password123!`) via `IPasswordHasher` and authenticates through `AuthTestClientFlow.LoginAndSelectTenantAsync`, ensuring full traversal of `/api/auth/login` + `/api/auth/select-tenant` before exercising endpoints. Removed the obsolete `AuthTestClient.UseTenantAsync` helper method and converted the guard test `NoUseTenantAsyncLeftTests` from warning mode to a hard failing assertion (no allowlist). Added a Makefile integration that runs the dev header guard script (`scripts/dev-header-guard.sh`) in non-fatal mode as part of `make test`, plus a dedicated `guard-dev-headers` target for strict CI enforcement. This closes the “test migration” portion of RDH Story 2: all integration/E2E/domain tests now rely exclusively on real JWT flows (password hashing -> login -> tenant selection) without dev headers or mint shortcuts.
- Files changed
  - apps/api.tests/Api/TenantSettingsEndpointsTests.cs — added `SeedPasswordAsync`, replaced client helper with inline auth flow.
  - apps/api.tests/Api/ToolCatalogTests.cs — same migration pattern.
  - apps/api.tests/Api/DevGrantRolesEndpointTests.cs — added password seeding, replaced client factory wrapper.
  - apps/api.tests/Api/DenominationsMetadataTests.cs — added password seeding + flow auth; retained unauthorized test.
  - apps/api.tests/Auth/AuthTestClient.cs — removed `UseTenantAsync` method.
  - apps/api.tests/Guard/NoUseTenantAsyncLeftTests.cs — now fails if any legacy usage appears; empty allowlist.
  - Makefile — added guard invocation (non-fatal) to `test` target and new `guard-dev-headers` strict target.
- Quality gates
  - Grep for `UseTenantAsync(` returns only historical references in story log / docs; no code usages remain.
  - Guard test passes (zero matches), ensuring helper removal completeness.
  - Dev header guard script reports no forbidden artifacts outside allowlists (negative tests + docs) after migrations.
- Rationale
  - Establishes a single, production-authentication pathway across all tests prior to introducing deprecation middleware. Eliminates risk of unnoticed divergence hidden behind mint helpers and sets a clean baseline for measuring deprecation effects.
- Follow-ups
  - Story 2 closure tasks: update LivingChecklist line to mark test migrations fully complete; update SnapshotArchitecture (auth testing layer & guard enforcement); prepare Story 3 deprecation middleware PR.
  - Consolidate duplicated `SeedPasswordAsync` helpers into a shared test utility (post-middleware, low risk refactor).
  - Tighten guard script (remove non-fatal mode) once middleware lands and dev headers are blocked.
- Snapshot / Docs
  - LivingChecklist update pending (will mark Story 2 test migrations done). SnapshotArchitecture to receive final test-migration summary & dependency diagram update.

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

2025-09-22 — Auth/JWT / RDH: Story 4 Physical Removal of Dev Headers — ✅ DONE

- Summary
  - Physically removed legacy development header authentication path. Deleted `DevHeaderAuthHandler.cs`, removed composite policy scheme (`BearerOrDev`) and all conditional registration / flag logic from `Program.cs`, and stripped the `DevHeaders` Swagger security definition. Replaced deprecation middleware with a minimal permanent guard that returns 401 `{ code: "dev_headers_removed" }` whenever `x-dev-user` or `x-tenant` headers appear. Removed feature flag `AUTH__ALLOW_DEV_HEADERS` usage and the deprecation metric (`auth.dev_headers.deprecated_requests`) + increment helper from `AuthMetrics.cs`. Updated negative-path regression tests (`DevHeadersDisabledTests`, `DevHeadersRemovedTests`) to assert the final canonical error code. All other tests already migrated to real JWT flows in prior stories.
- Files changed
  - apps/api/Program.cs — removed Dev header scheme registration, composite scheme block, flag read, and Swagger security definition.
  - apps/api/App/Infrastructure/Auth/DevHeaderAuthHandler.cs — deleted.
  - apps/api/App/Middleware/DevHeadersDeprecationMiddleware.cs — simplified to unconditional 401 removal code (no metric/flag).
  - apps/api/Application/Auth/AuthMetrics.cs — removed `DevHeadersDeprecated` counter + increment method.
  - apps/api.tests/Auth/DevHeadersDisabledTests.cs & DevHeadersRemovedTests.cs — updated expectations from `dev_headers_deprecated` to `dev_headers_removed`; removed flag setup.
  - docs/auth-upgrade.md — excised flag references; added removal notice & rollback tag instructions.
  - devInfo/jwtRefactor/rdhSprintPlan.md — Story 4 acceptance items marked complete (runtime & tests); pending doc & tagging tasks annotated.
  - devInfo/LivingChecklist.md — Story 4 item checked off.
- Quality gates
  - API build succeeded (no errors; pre-existing ImageSharp vulnerability warnings only). Updated tests compile; full suite run pending before merge (will capture pass counts in final commit message). Grep confirmed no remaining runtime references to `DevHeaderAuthHandler`, `BearerOrDev`, or `AUTH__ALLOW_DEV_HEADERS`.
- Rationale
  - Achieves single authentication pathway (JWT) across all environments; eliminates dormant attack surface and removes configuration complexity tied to transitional flag. Permanent guard preserves deterministic failure signal for any stale tooling still sending legacy headers while avoiding reintroduction complexity.
- Rollback
  - Tag to be created immediately prior to merge: `before-dev-header-removal`. Rollback requires checking out the tag (flag no longer available post-removal). If temporary restoration needed, reapply handler & composite scheme block from tag as a hotfix with explicit TEMP comment.
- Follow-ups
  - Update `SnapshotArchitecture.md` to remove deprecation phase narrative & composite scheme references (in progress).
  - Optionally remove the temporary guard middleware entirely after short observation window if zero header attempts observed (future cleanup story).
  - Add CI enforcement (grep) to fail on reintroduction of removed identifiers (planned Story 5 item reuse of guard script with tightened allowlist).

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

  2025-09-22 — Auth/JWT: RDH Story 2 Phase A Final Non‑Guard UseTenantAsync Migrations — ✅ DONE
  - Summary
    - Completed migration of the final six non‑guard legacy auth usages (`AuthTestClient.UseTenantAsync`) to the real password + `/api/auth/login` + `/api/auth/select-tenant` flow via `AuthTestClientFlow.LoginAndSelectTenantAsync`. Updated: `AgentTasksEndpointsTests`, `NotificationsProdEndpointsTests` (tenant-owner paths only; superadmin path intentionally still uses `UseSuperAdminAsync` mint helper), `TenantSettingsEndpointsTests`, `ToolCatalogTests`, `DevGrantRolesEndpointTests`, and `DenominationsMetadataTests`. Re-ran targeted suites (20 tests) all PASS. Guard test now reports only itself as a remaining `UseTenantAsync` occurrence (warnings mode) confirming full Phase A deprecation of mint helper for tenant-scoped scenarios. Superadmin elevation remains an accepted temporary mint helper exception pending a future story introducing a password/elevation path. No production code changes required; purely test refactors.
  - Files changed
    - apps/api.tests/Api/AgentTasksEndpointsTests.cs — replaced tenant client helper with flow call; removed legacy comment.
    - apps/api.tests/Api/NotificationsProdEndpointsTests.cs — migrated tenant-owner scenarios; retained `UseSuperAdminAsync` with comment.
    - apps/api.tests/Api/TenantSettingsEndpointsTests.cs — swapped client factory to flow helper.
    - apps/api.tests/Api/ToolCatalogTests.cs — migrated auth setup to flow helper.
    - apps/api.tests/Api/DevGrantRolesEndpointTests.cs — replaced `UseTenantAsync` with flow helper and added documentation comment.
    - apps/api.tests/Api/DenominationsMetadataTests.cs — migrated to flow helper; removed legacy RDH comment.
  - Quality gates
    - Targeted run: 20/20 PASS post-migration. Guard test run: 1/1 PASS (warns only; lists itself). Build green; only pre-existing ImageSharp vulnerability warnings. No new analyzer warnings introduced.
  - Rationale
    - Eliminates dependency on test-only mint helper ensuring integration tests now exercise password verification, refresh issuance, and tenant token selection—critical before removing dev headers and mint infrastructure in later RDH phases. Confirms guard can be flipped to fail mode after superadmin replacement lands.
  - Follow-ups
    - Introduce superadmin real elevation path or temporary allowlist before converting guard to failure. Consolidate duplicated password seeding logic across tests (post Phase A cleanup). Update `SnapshotArchitecture.md` and `LivingChecklist` in upcoming RDH phase completion commit when guard enforcement changes.

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
  - Web tests: PASS (`pnpm --filter @appostolic/web test`). Coverage remains above thresholds; non-fatal MUI X license warnings unchanged.
  - Typecheck/Lint: PASS for new files.

- Notes
  - Next iterations: add DELETE logo action, surface privacy toggle when ready, and consider extracting a shared deep-merge patch builder for tenant/user to remove duplication.

## 2025-09-18 — Auth — Root route gating + Signup styling — ✅ DONE

## 2025-09-17 — Nav: TopBar Admin visibility fixes — ✅ DONE

- Summary
  - Resolved a regression where a non-admin user with a single tenant membership could see the Admin menu. `TopBar` now uses the shared roles helper `computeBooleansForTenant` to determine Admin visibility based on the selected tenant’s membership, supporting both roles flags (e.g., `TenantAdmin`) and legacy roles (`Owner`/`Admin`). It also normalizes `session.tenant` when it contains a tenantId by resolving to the corresponding membership’s slug.
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
  - devInfo/refLeg/UPGRADE-roles-migration.md (new) — full upgrade & ops guide.
  - scripts/rollback/restore_membership_role.sql (new) — rollback script for legacy column/type.
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
  - Finalized `/api/auth/refresh` implementing cookie-first refresh token rotation with transitional JSON body support under `AUTH__REFRESH_JSON_GRACE_ENABLED`. Endpoint accepts httpOnly cookie `rt` (preferred) or `{ refreshToken }` body (during grace), validates hashed Base64(SHA256) refresh token, enforces reuse (`refresh_reuse`), invalid (`refresh_invalid`), expired (`refresh_expired`), and missing (`missing_refresh`) structured error codes, and supports `?tenant=<slug|id>` issuance of a tenant-scoped access token (403 on non-membership). Rotation occurs via sequential revoke + issue (no explicit transaction) to remain compatible with EF InMemory provider (transactions unsupported). Deprecation headers (`Deprecation: true`, `Sunset: <date>`) emitted when body path used and `AUTH__REFRESH_DEPRECATION_DATE` configured. Response shape mirrors login: `{ user, memberships, access, refresh, tenantToken? }` (plaintext refresh omitted when cookie enabled after grace). All `RefreshEndpointTests` passing: cookie rotation, tenant token issuance, reuse detection, revoked reuse, expired, body grace, missing refresh (400). Removed lingering transaction wrapper causing prior InMemory `InvalidOperationException`. Updated architecture snapshot, LivingChecklist, and this story log entry.
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
