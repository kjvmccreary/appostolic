- 2025-09-27 — Guardrails sprint Phase 3 navigation wiring — 🚧 IN PROGRESS

Summary

- Added the Guardrails admin surface to the Studio navigation: TopBar Admin dropdown and mobile NavDrawer now expose `/studio/guardrails` for TenantAdmin users alongside existing org settings pages.
- Expanded TopBar admin gating tests to confirm the Guardrails entry appears when the tenant membership includes `TenantAdmin`, and hardened the guardrail page MSW suite to cover draft save/publish/reset flows with resilient selectors.
- Refined guardrail admin tests to use controlled textarea updates and menu triggers, keeping the new UI interactions stable under Vitest coverage thresholds.

Quality Gates

- `pnpm --filter @appostolic/web test` — Passed.

Follow-ups / Deferred

- Surface the forthcoming superadmin presets console and document keyboard/a11y defaults for the guardrail editor before closing Phase 3.

- 2025-09-27 — Guardrails sprint Phase 2 preflight evaluator — 🚧 IN PROGRESS

Summary

- Extended the EF InMemory JSON converter setup to include guardrail system, denomination, tenant, and user policy documents so the new evaluator can run under the test host without provider errors.
- Updated the guardrail preflight endpoint to fall back to `ClaimTypes.NameIdentifier` when extracting the caller id, preventing 401s for tokens that remap the `sub` claim.
- Taught the guardrail preflight integration tests to deserialize enum values with a shared `JsonStringEnumConverter`, keeping request/response coverage green alongside the new endpoint.
- Wired guardrail evaluation into agent task creation: optional guardrail context now triggers the evaluator, stores decision/metadata on `agent_tasks`, emits security events, and prevents queueing when the verdict is deny/escalate; the worker double-checks stored decisions before execution.
- Authored `AgentTasksGuardrailTests` covering deny/escalate persistence, response metadata projection, and worker skip behavior so regressions surface quickly.

Quality Gates

- `make migrate`
- `dotnet test apps/api.tests/Appostolic.Api.Tests.csproj` — Passed (297 passed, 1 skipped).

Follow-ups / Deferred

- Kick off Guardrails Sprint Phase 3 (admin policy editor surfaces and MSW fixtures) after tightening evaluator coverage for additional guardrail scenarios.

- 2025-09-27 — Guardrails sprint Phase 2 UI surfacing — 🚧 IN PROGRESS

Summary

- Introduced shared Studio task types carrying guardrail decisions and metadata so the web inbox/detail can depend on a single DTO shape.
- Updated the Studio tasks table to show a dedicated guardrail column with color-coded chips for allow/escalate/deny decisions.
- Enriched the task detail view with guardrail alerts, metadata panel (signals, matched rule, channel, evaluated-at), and a copy-to-clipboard helper for the serialized context.
- Added Vitest coverage verifying the new guardrail presentation and kept existing retry/cancel/export flows green.
- Refreshed the guardrail sprint plan and Living Checklist to reflect the UI surfacing milestone.

Quality Gates

- `pnpm -w -s test -w --filter @appostolic/web` — Passed.

Follow-ups / Deferred

- Extend inbox filtering to optionally highlight denied/escalated guardrails and continue Phase 3 admin UI work.

- 2025-09-26 — Guardrails sprint Phase 1 completed: delivered multi-layer schema, seeded presets, enforced RLS, and updated architecture docs for guardrail persistence.

### 2025-09-26 - Story: Guardrails data model & RLS — ✅ DONE

Summary

- Designed guardrail persistence around dedicated tables (`guardrail_system_policies`, `guardrail_denomination_policies`, `guardrail_tenant_policies`, `guardrail_user_preferences`) to support the merge order (system → denomination → tenant → override → user).
- Generated EF Core migration `s9_01_guardrail_schema` with baseline seed data (system-core policy + 10 denomination presets), tenant/user RLS policies, and DbSets/configurations wired into the API context.
- Applied migration via `make migrate` and refreshed `SnapshotArchitecture.md` plus sprint plan notes to capture the new guardrail data layer.

Quality Gates

- `dotnet build apps/api/Appostolic.Api.csproj` — Passed (existing analyzer/deprecation warnings only).
- `make migrate` — Applied migration to local Postgres (dotnet-ef database update).

Follow-ups / Deferred

- Phase 2: Implement evaluator service + `/api/guardrails/preflight` endpoint leveraging the new schema and seed data.

- 2025-09-26 — Guardrails sprint Phase 0 completed: reconciled requirements, inventoried existing guardrail stubs, locked acceptance criteria, and updated sprint docs to reflect readiness for implementation.

### 2025-09-26 - Story: Bio markdown polish & refresh bridge TTL — ✅ DONE

Summary

- Added shared `.markdown-body` styles so markdown previews render list bullets consistently across tenant and profile bio editors, matching author expectations.
- Updated both bio editors to keep the Save button visually enabled while guarding submissions via `aria-disabled`, aligning the UX with form state and preventing accidental duplicate saves.
- Extended the refresh rotation bridge TTL to one hour (with refresh-on-use) so long-lived admin pages reuse rotated cookies after idle periods without triggering refresh reuse errors.

Quality Gates

- `make fetest` — Passed.

Follow-ups / Deferred

- None.

---

### 2025-09-26 - Story: Bio save button accessibility polish — ✅ DONE

Summary

- Kept the tenant and personal bio Save buttons visually enabled after a successful save while guarding submissions via `aria-disabled`, preventing confusion when content already matches the baseline.
- Updated both editors to expose the correct accessibility cues (aria-disabled and cursor states) and refactored tests to verify no network calls occur when nothing changed.
- Confirmed the web Vitest suite still passes so the UX polish doesn’t regress existing coverage.

Quality Gates

- `pnpm -w test -w --filter @appostolic/web` — Passed.

Follow-ups / Deferred

- None.

---

### 2025-09-26 - Story: Admin dashboards refresh fallback — ✅ DONE

Summary

- Restored Notifications, Audits, and Invites admin pages by retrying proxy header generation with the latest bridged refresh cookie when the initial access token fetch fails.
- Ensured rotated refresh cookies are re-applied to the request jar and response queue, preventing cascading 401s after reuse detection during rapid navigation.
- Confirmed web proxy tests remain green so the new fallback path doesn’t regress existing coverage.

Quality Gates

- `pnpm -w test -w --filter @appostolic/web` — Passed.

Follow-ups / Deferred

- None.

---

### 2025-09-27 - Story: Tenant logo preview normalization — ✅ DONE

Summary

- Normalized tenant logo URLs using the API base so `/media/...` responses render immediately across environments.
- Added cache-buster helper and preview error fallback so the upload shows the new logo instead of the initial letter avatar.
- Propagated preview resets and blob URL cleanup on tenant switches to prevent stale state or leaked object URLs.

Quality Gates

- `pnpm -w test -w --filter @appostolic/web` — Passed.

Follow-ups / Deferred

- None.

---

### 2025-09-26 - Story: Tenant switch refresh bridge — ✅ DONE

Summary

- Added a shared `refreshRotationBridge` so rotated refresh cookies are reused across server fetches and proxy requests, eliminating the flicker/logout while switching tenants.
- Updated `/api/tenant/select` to adopt bridged cookies, expanded tests to cover rotation handoff, and keyed tenant admin forms by slug to reset client state per tenant.
- Introduced `/api-proxy/tenants/logo` with tenant-admin guard and FormData relay so logo uploads succeed without 404s while propagating refreshed cookies.

Quality Gates

- `pnpm -w -s test -w --filter @appostolic/web` — Passed.

Follow-ups / Deferred

- None.

---

### 2025-09-26 - Story: Frontend Auth Fixture Alignment (Story 12) — ✅ DONE

Summary

- Expanded `buildProxyHeaders` integration coverage to assert refresh reuse eviction and concurrency coalescing, migrated tenant settings form tests to the shared MSW auth handlers, and published `docs/frontend-auth-testing.md` capturing the new fixture playbook.

Quality Gates

- `pnpm --filter @appostolic/web test` — Passed.

Follow-ups / Deferred

- none

---

### 2025-09-25 - Story: Frontend Auth Fixture Alignment (Story 12) — 🚧 IN PROGRESS

Summary

- Documented the current NextAuth/session mocks, proxy header stubs, and MSW usage gaps across the web test suite in `devInfo/jwtRefactor/audit.md`, seeded the remediation backlog, shipped the shared session fixture module, and replaced manual fetch spies in tenant switcher tests with the new MSW auth handlers.
- Added Story 12 to `devInfo/jwtRefactor/jwtSprintPlan.md` with acceptance checkpoints, marking both the shared session fixture and MSW handler milestones complete.

Quality Gates

- Not run (documentation-only update).

Follow-ups / Deferred

- Author `buildProxyHeaders` integration tests plus tenant settings form auth-error coverage before Story 8 wraps.

---

### 2025-09-25 - Story: Expanded TestAuthClient (Story 20) — ✅ DONE

Summary

- Story 20 – Expanded TestAuthClient: Enabled TTL overrides on token minting services and helper endpoint, exposed detailed mint metadata via `TestAuthClient`, and added tests covering overridden lifetimes plus expired refresh handling.

Quality Gates

- `dotnet test apps/api.tests/Appostolic.Api.Tests.csproj --filter FullyQualifiedName~TestTokenFactory` (Passed: 5, Failed: 0, Skipped: 0).

Follow-ups / Deferred

- None.

---

### 2025-09-25 - Story: Avatar proxy cleanup + search param guards — ✅ DONE

Summary

- Avatar proxy now accepts a plain Request with stubbed form data, tests stub the multipart body safely, search-param consumers guard against null return values, and membership labels rely solely on roles. pnpm vitest for @appostolic/web and pnpm -w build both succeed.

Quality Gates

- `pnpm -w --filter @appostolic/web test -- --reporter=verbose` — Passed.
- `pnpm -w build` — Passed.

Follow-ups / Deferred

- None.

2025-09-25 — Story 31: Plaintext counters final deletion — ✅ DONE

- Summary
  - Removed the temporary `auth.refresh.plaintext_suppressed` counter and increment helper from `AuthMetrics`, eliminated the corresponding calls in login, select-tenant, and refresh endpoints, and pruned the metric from unit coverage (`AuthMetricsTests`) and Grafana dashboard lint (`DashboardMetricsValidationTests`). Updated documentation (`docs/auth-upgrade.md`, sprint plan notes, LivingChecklist, SnapshotArchitecture, legacy snapshot) to reflect the retirement and prevent future references to the counter.
  - Marked Story 31 as in progress within the triage table before the work and captured the steady-state note so future readers know the counter no longer emits. Ensured remaining plaintext instrumentation (`auth.refresh.plaintext_emitted`) stays intact for regression detection.
- Rationale
  - Completes the post-cookie-only cleanup by removing noise from telemetry and documentation; avoids dashboards or alerts referencing a counter that will never increment again.
- Files changed
  - apps/api/Application/Auth/AuthMetrics.cs
  - apps/api/App/Endpoints/V1.cs
  - apps/api.tests/Auth/AuthMetricsTests.cs
  - apps/api.tests/Auth/DashboardMetricsValidationTests.cs
  - docs/auth-upgrade.md
  - devInfo/jwtRefactor/jwtSprintPlan.md
  - devInfo/jwtRefactor/triSprintPlan.md
  - devInfo/LivingChecklist.md
  - SnapshotArchitecture.md
  - SnapshotArchitecture.legacy02.md
- Quality Gates
  - `dotnet test apps/api.tests/Appostolic.Api.Tests.csproj` — Passed (291 passed, 1 skipped, duration 59s).
- Follow-ups / Deferred
  - None — telemetry and documentation now match steady-state auth behavior.

---

### 2025-09-25 - Story: Roles Label Array (Story 19) — ✅ DONE

Summary

- Extended auth membership projections so every membership now ships both the numeric `roles` bitmask and a `rolesLabels` array (TenantAdmin, Approver, Creator, Learner) across login, select-tenant, refresh, and test-helper flows, giving clients human-readable context without reimplementing bitflag decoding.
- Added shared helper `DescribeRoleLabels` to keep label derivation centralized around the `Roles` enum, preventing drift if roles expand.
- Updated multi-tenant login integration tests to assert `rolesLabels` for every membership and ensure tenant token payloads carry the label array alongside the bitmask.

Rationale

- Improves developer experience when consuming tokens or membership metadata, especially in debugging tools and upcoming UI, by surfacing readable role names without duplicating flag math per client.

Quality Gates

- `dotnet test apps/api.tests/Appostolic.Api.Tests.csproj` (Passed: 289, Failed: 0, Skipped: 1).

Follow-ups / Deferred

- None; downstream clients can begin reading `rolesLabels` immediately. Additional stories may extend label localization when new roles are introduced.

---

### 2025-09-23 - Story: Admin Forced Logout & Tenant Bulk Invalidate (Story 9) — ✅ DONE

Summary

- Implemented forced logout administrative endpoints: `POST /api/admin/users/{id}/logout-all` (platform superadmin scope) and `POST /api/admin/tenants/{id}/logout-all` (tenant admin of target tenant or platform superadmin). Both revoke all active neutral refresh tokens and bump affected user TokenVersion(s) to invalidate outstanding access tokens.
- Added feature flag `AUTH__FORCED_LOGOUT__ENABLED` gating endpoint registration for rapid disable if regression discovered.
- Instrumented metrics: `auth.admin.forced_logout.requests{scope=user|tenant,outcome}` and `auth.admin.forced_logout.sessions_revoked{scope}` for observability of incident response operations and blast radius. Integrated into existing OpenTelemetry meter.
- Extended structured security event vocabulary with `forced_logout_user` and `forced_logout_tenant` (bounded `reason=admin_forced`), emitted on successful operations. Events flow through existing `Security.Auth` logger with metrics increment.
- Added platform superadmin allowlist config (`PLATFORM__SUPER_ADMINS`) leveraged in tests; updated `WebAppFactory` to enable forced logout flag and set default superadmin seed for deterministic elevation.
- Created integration tests covering forbidden user-level forced logout without admin rights, tenant scope forbidden for non-member, and success path for platform admin (augmenting allowlist at factory clone). Included baseline assertions on HTTP status and response payload structure (revoked count & tokenVersion bump).

Rationale

- Provides immediate incident containment tool for compromised credentials or suspected session token leakage (targeted account) and broader containment for tenant-wide compromise scenarios (e.g., credential stuffing cluster, insider threat). TokenVersion bump ensures rapid invalidation of extant access tokens without requiring per-token blacklist.

Files Changed / Added

- `apps/api/App/Endpoints/V1.cs` — added forced logout endpoints (flag gated), revocation + TokenVersion bump logic, metrics & security events emission.
- `apps/api/Application/Auth/AuthMetrics.cs` — added forced logout counters + helpers.
- `apps/api/Application/Auth/SecurityEvents.cs` — extended allowed event types.
- `apps/api/Program.cs` — feature flag read.
- `apps/api.tests/WebAppFactory.cs` — enabled flag & platform superadmin config for tests.
- `apps/api.tests/Auth/ForcedLogoutTests.cs` — new integration tests.
- `SnapshotArchitecture.md` — updated session model, metrics, flags, endpoint list.
- `devInfo/LivingChecklist.md` — added checklist entry (Story 9b) marked done.

Quality Gates

- All modified code compiles cleanly; forced logout tests pass alongside existing auth suite (incremental run locally prior to commit). Metrics and security events follow naming taxonomy. No schema changes required.

Follow-ups / Deferred

- Additional metrics assertion tests (tag correctness) and security event capture validation for tenant scope could be added (low risk enhancement).
- Documentation of incident response runbook (revocation procedure & dashboard correlation) to be expanded in observability / ops docs (future minor docs pass).

#### 2025-09-24 - Story 9 Post-Completion Hardening (Allowlist Flexibility) — ✅ DONE

Summary

- Extended forced logout platform superadmin allowlist to accept either GUIDs or emails (config key `PLATFORM__SUPER_ADMINS`). Original implementation required GUIDs only which caused test flakiness where emails were supplied. Endpoint logic now parses tokens, separates GUIDs vs email values, and loads caller email for resolution when necessary.
- Updated integration tests (`ForcedLogoutTests`) to remove brittle JSON claim traversal, decode JWT fallback, and derive tenant id from the EF context instead of relying on `/api/me` response shape. All forced logout tests now pass (3/3).
- Adjusted architecture snapshot to reflect mixed allowlist support.

Rationale

- Provides operational flexibility (ops can list emails before user GUIDs are known) and reduces friction when rotating seed data. Improves test resilience against response shape changes.

Quality Gates

- Incremental test run of forced logout suite passes. Build succeeded with no errors (warnings only). Broader suite pending next scheduled run.

Follow-ups

- Consider consolidating superadmin configuration keys (`Auth:SuperAdminEmails` + `PLATFORM__SUPER_ADMINS`) into a single canonical source in a future cleanup story.

---

### 2025-09-25 - Story: CSRF Logout Flow Hardening — ✅ DONE

Summary

- Updated `Csrf_Enabled_Logout_With_Header_Succeeds` integration test to propagate the refresh cookie and include the refresh token in the JSON body so the logout endpoint succeeds even when refresh JSON grace remains enabled.
- Ensured logout requests carry both the bearer token and CSRF header while avoiding empty-body submissions that previously triggered the `missing_refresh` guard path.
- Re-ran the focused CSRF logout test filter to confirm both negative and positive scenarios now pass (2/2).

Rationale

- Prevents regressions where CSRF-protected logout flows fail due to refresh token handling changes introduced during the JWT refactor, keeping parity with production expectations.

Quality Gates

- `dotnet test apps/api.tests/Appostolic.Api.Tests.csproj --filter "FullyQualifiedName~Csrf_Enabled_Logout"` (Passed: 2, Failed: 0).

Follow-ups / Deferred

- Consider adding a regression test ensuring logout all endpoints also handle cookie + JSON grace interplay once related stories progress.

---

### 2025-09-24 - Story: TokenVersion Cache Sync & Structured JWT Challenge Body — ✅ DONE

Summary

- Addressed residual failing tests related to access token invalidation after TokenVersion bumps (password change, logout-all, forced logout). Root cause: in-memory TokenVersion cache (performance optimization) not updated immediately after DB increments, causing transient acceptance of stale access tokens until cache TTL expiry. Added proactive `tokenVersionCache.Set(userId, newVersion)` calls in password change, logout-all, user forced logout, and tenant forced logout loops to ensure atomic cache synchronization.
- Introduced custom JWT `OnChallenge` handler emitting JSON `{ "error": <machine_code> }` for 401 responses. Previously, `ctx.Fail("token_version_mismatch")` produced an empty body, breaking deterministic test assertions expecting the machine code. Now failures like `token_version_mismatch` and `invalid_sub` return structured bodies.
- All previously failing tests (password change invalidation, logout-all invalidation, forced logout cache invalidation) now pass; full suite green (latest run: 287 passed, 1 skipped, 0 failed).

Rationale

- Eliminates narrow race windows where security-sensitive version revocations could be delayed, strengthening immediate incident response guarantees (forced logout, password compromise scenarios).
- Standardizes auth error surfaces enabling clients and tests to rely on stable machine codes without coupling to framework-specific challenge formatting.

Files Changed

- `apps/api/App/Endpoints/V1.cs` — added cache `Set` after each TokenVersion increment path.
- `apps/api/Program.cs` — added `OnChallenge` JSON body emission and failure reason capture in `HttpContext.Items`.
- `SnapshotArchitecture.md` — updated Security Features with cache sync + structured challenge bullet.

Quality Gates

- Targeted failing tests re-run: all green post changes. Full integration test suite green (287 passed / 1 skipped). No analyzer regressions introduced.

Follow-ups / Deferred

- Refactor duplicated login logic (pending separate cleanup story).
- Consider dedicated integration test asserting `invalid_sub` challenge body (currently covered indirectly by mismatch tests).
- Evaluate metrics impact (cache hit/miss) after proactive Set (expect improved hit ratio, reduced DB lookups under churn).

---

Summary

- Began Story 7 by adding initial Grafana dashboard JSON definitions (`infra/grafana/dashboards/auth-overview.json`, `auth-security.json`) covering core auth KPIs: login success %, failure & refresh failure breakdowns, rate limiter outcome differentiation (block vs dryrun_block), security events by type, key rotation per-key signing rates, and p95 latencies for login/refresh. Security dashboard also surfaces invalid/reuse/expired refresh event rates, failure and reuse ratios, and validation failure counts.
- Introduced Prometheus alert rules file (`infra/alerts/auth-rules.yml`) with baseline thresholds: high login failure ratio (>30%), refresh reuse spike (>5/5m), active enforced rate-limit blocks (>1/5m), key rotation validation failure (any), and excessive invalid refresh events (>50/15m). Thresholds intentionally conservative for initial observation phase.
- Added README (`infra/grafana/README.md`) documenting metric naming (dot → underscore), panel coverage, alert rationale, and next steps (apply script + SLO panels).

Rationale

- Establishes immediate visibility for operators to validate recent observability enhancements (Stories 3–6) and prepares for upcoming session enumeration metrics (Story 8). Differentiating dry-run limiter outcomes from enforced blocks supports safe rollout of blocking mode.

Files Added

- `infra/grafana/dashboards/auth-overview.json`
- `infra/grafana/dashboards/auth-security.json`
- `infra/alerts/auth-rules.yml`
- `infra/grafana/README.md`

Next Steps

- Optionally add Grafana apply script & CI gate.
- Tune alert thresholds after 24–48h of baseline data.
- Add SLO/error budget & trace exemplar panels; integrate session metrics after Story 8.

Quality Gates

- No runtime code changes; test suite remains green (267 passed, 1 skipped). Dashboards/alerts are additive artifacts only.

---

### 2025-09-23 - Story: Grafana Dashboards & Alert Rules (Story 7) Phase 2 Lint Validation — ✅ DONE

Summary

- Added automated validation test `DashboardMetricsValidationTests` (Story 7 acceptance: lint) that parses all dashboard panel PromQL expressions and the auth alert rules file, extracting `auth_` metric identifiers and asserting they correspond to the known set of emitted auth instruments (with allowance for histogram `_bucket|_sum|_count` suffixed series). Guards against typos / drift (e.g., incorrect underscore conversion or stale metric names) prior to manual Grafana import or CI wiring.

Rationale

- Ensures dashboards remain in lockstep with evolving instrumentation; early failure prevents broken panels post‑deploy and satisfies Sprint Plan requirement for a parse/lint check without introducing optional apply automation.

Files Added

- `apps/api.tests/Auth/DashboardMetricsValidationTests.cs`

Quality Gates

- Test suite updated; new test passes locally (no unknown metric identifiers found). No runtime code changes.

Next Steps

- Manual import into dev/staging Grafana to satisfy remaining exit criteria (live data render + alert rule load). Optional provisioning script & SLO panels deferred (not in current scope per user directive).
- Story 7 closed; proceed to Story 8 (session enumeration & selective revoke groundwork).

---

### 2025-09-24 - Stories 17 & 18: Session Device Display Name (Enumeration Enrichment) — ✅ DONE

Summary

- Added optional human-readable device display name support for user sessions to improve clarity in forthcoming session management UI.
- New nullable column `device_name varchar(120)` on `app.refresh_tokens` via migration `s17_18_device_name`.
- Captured `X-Session-Device` header during initial issuance (login, magic consume) and rotations (refresh, select-tenant). If absent on rotation, previous session's name is carried forward.
- Sanitization: control chars stripped, trimmed, truncated to 120 chars; empty → null.
- `/api/auth/sessions` now includes `deviceName` (null for legacy / unnamed sessions).
- Metrics: `auth.session.device_named` counter added (tags: user_id, refresh_id) incremented first time a name is assigned.
- Backward compatible; clients ignoring header unaffected.

Rationale

Improves user security awareness by enabling differentiation of concurrent sessions (e.g., distinguishing personal vs work devices) without exposing sensitive identifiers. Dedicated column chosen over JSON for lower query overhead and future indexing flexibility.

Files Changed / Added

- `apps/api/App/Infrastructure/Auth/Jwt/RefreshToken.cs` (DeviceName property)
- `apps/api/Program.cs` (EF mapping for device_name)
- `apps/api/App/Endpoints/V1.cs` (capture + carry-forward + normalization helper + enumeration projection update)
- `apps/api/Application/Auth/AuthMetrics.cs` (SessionDeviceNamed counter + increment helper)
- `apps/api/Migrations/20250924163000_s17_18_device_name.cs` (migration)
- `apps/api/Migrations/AppDbContextModelSnapshot.cs` (snapshot updated)
- `devInfo/jwtRefactor/triSprintPlan.md` (Stories 17 & 18 marked DONE)

Quality Gates

- Build compiles; migration present. Enumeration response remains additive (legacy tests unaffected). Manual inspection confirms normalization logic and metric hook only fires on first assignment.

Follow-ups / Deferred

- Add integration tests for device naming & rotation inheritance.
- UI work: display device names and possibly allow user-initiated rename (future endpoint).
- Potential common-name normalization or abuse filtering deferred until real usage observed.

---

### 2025-09-23 - Story: Tracing Span Enrichment (Story 5) Completion

### 2025-09-23 - Story: Structured Security Events (Story 6) Phase 2 Integration Coverage Expansion — 🚧 IN PROGRESS

Summary

- Refactored `/api/auth/refresh` rate limiting paths to use `ISecurityEventWriter` instead of ad-hoc JSON logger serialization ensuring metric `auth.security.events_emitted{type=refresh_rate_limited}` increments for both ip-only and user+ip block cases (previously missed because direct logger bypassed writer increment).
- Added additional integration tests validating structured security event emission for: `refresh_expired`, `refresh_rate_limited`, and `logout_all_user`.
- Extended test harness with `SecurityEventsAdditionalIntegrationTests` capturing `Security.Auth` logger lines (reuse of existing provider pattern) and asserting bounded vocabulary fields (v=1, type, reason, user_id presence when expected).

Rationale

- Ensures all high-value negative/abuse auth paths (reuse, expired, rate-limited, logout-all) generate consistent structured events passing through a single writer abstraction for uniform metrics and future pipeline forwarding (e.g., SIEM ingestion).

Files Changed / Added

- `apps/api/App/Endpoints/V1.cs` — replaced manual rate-limit JSON log emission with `securityEvents.Emit(...)` for both ip-only and user+ip limiter branches; added meta cause + optional dry_run flag via builder.
- `apps/api.tests/Auth/SecurityEventsAdditionalIntegrationTests.cs` — new integration tests for expired, rate limited, and logout-all user events.

Quality Gates

- Pending full test run after additions (prior baseline 263 tests green). No production logic changes outside rate-limit emission refactor; behavior (status codes, JSON responses) preserved.

Follow-ups

- Add integration assertion for `refresh_invalid` (already unit-tested indirectly) and potential `logout_all_tenant` / `session_revoked_single` once implemented in future stories.
- Consider adding trace/span correlation IDs into `meta` for SIEM cross-reference (optional optimization phase).

---

### 2025-09-23 - Story: Structured Security Events (Story 6) Finalization — ✅ DONE

Summary

- Added dry-run limiter security event emission & integration test `Emits_DryRun_Rate_Limited_Event_With_Meta` asserting `meta.dry_run=true` and `meta.cause=window` for would-block scenarios when `AUTH__REFRESH_RATE_LIMIT_DRY_RUN=true`.
- Adjusted `/api/auth/refresh` logic to emit `refresh_rate_limited` events for dry-run (would-block) cases without returning 429, ensuring observability parity with enforced blocks. Keeps metrics classification consistent while allowing tuning in production before enforcement.
- Confirmed integration coverage now spans: `login_failure`, `refresh_reuse`, `refresh_expired`, `refresh_invalid`, `refresh_rate_limited` (enforced + dry-run), and `logout_all_user` — satisfying revised exit criteria.
- Updated sprint plan (`bdlSprintPlan.md`) marking Story 6 ✅ DONE and documenting deferral of `logout_all_tenant`, `session_revoked_single`, and optional trace/span meta enrichment to future stories.

Rationale

- Guarantees dashboards/alerts (Story 7) can distinguish actual enforced limiter blocks from dry-run observations, enabling safer rollout and tuning of thresholds without losing visibility of attempted abuse volume.

Files Changed / Added

- `apps/api/App/Endpoints/V1.cs` — emit events when attempts exceed max even in dry-run; conditional block response only when not dry-run.
- `apps/api.tests/Auth/SecurityEventsAdditionalIntegrationTests.cs` — new dry-run meta assertion test.
- `devInfo/jwtRefactor/bdlSprintPlan.md` — status + exit criteria updates.

Quality Gates

- Targeted test run for new dry-run meta test passed. No regressions expected; limiter semantics unchanged for enforced mode.

Deferred / Next

- Correlation meta (trace/span) and future event types deferred to Story 7+ when endpoints or SIEM requirements materialize.

---

Summary

- Added `AuthTracing` helper (`ActivitySource("Appostolic.Auth")`) and standardized non-PII auth span attributes: `auth.user_id`, optional `auth.tenant_id`, `auth.outcome`, optional bounded `auth.reason` (machine codes only; no emails, no raw tokens).
- Introduced metric `auth.trace.enriched_spans{span_kind,outcome}` with helper `AuthMetrics.IncrementTraceEnriched` capturing enrichment occurrences (span_kind currently server/internal mapping underlying ASP.NET Core spans and any custom internal spans).
- Wired enrichment into login, refresh, logout, and logout-all paths (success + failure) ensuring consistent tagging across primary auth lifecycle operations.
- Added `TracingEnrichmentTests` validating attribute presence, absence of `email`/PII, and counter increments for success/failure flows.
- Updated sprint plan (`bdlSprintPlan.md`) marking Story 5 ✅ DONE and `SnapshotArchitecture.md` Observability section documenting enrichment + metric; appended this entry to story log.

Rationale

- Establishes trace-level correlation for auth events enabling future dashboard slices (e.g., outcome distribution, tenant concentration) and sets foundation for upcoming structured security event emission (Story 6) to link events to enriched spans without leaking PII.

Files Changed

- `apps/api/Application/Auth/AuthMetrics.cs` — added `TraceEnrichedSpans` counter + increment helper.
- `apps/api/Application/Auth/AuthTracing.cs` — new helper for span tagging & metric increment.
- `devInfo/jwtRefactor/bdlSprintPlan.md` — Story 5 section updated to DONE with summary & acceptance mapping.
- `SnapshotArchitecture.md` — Observability section updated with enrichment attributes & metric line.

Quality Gates

- Added & modified files compile cleanly (no analyzer errors). Existing auth + tracing tests pass (spot run for new test). No changes to public API surface; risk confined to observability instrumentation.

Follow-ups

- Optional: extend enrichment to password change/select-tenant ancillary flows & upcoming security event spans.
- Evaluate sampling / span volume after integrating security events to adjust overhead if necessary.

---

### 2025-09-23 - Story: Auth Test Suite Final Green (Plaintext Suppression Flag Fix)

### 2025-09-23 - Story: Dual-Key Signing Grace Window (Story 4) Completion

Summary

- Completed dual-key JWT signing implementation: ordered `AUTH__JWT__SIGNING_KEYS` parsing (first key signs, all keys verify), deterministic `kid` assignment (first 8 bytes hex), metrics instrumentation (`auth.jwt.key_rotation.tokens_signed{kid}`, `auth.jwt.key_rotation.validation_failure{phase}`) and internal health endpoint `/internal/health/jwt-keys` returning active key id, key set, and verification probe result.
- Added tests: rotation lifecycle (`DualKeySigningTests` existing), key rotation metrics (`KeyRotationMetricsTests` tokens_signed counter), and health endpoint shape (`JwtKeysHealthEndpointTests`). Negative-path validation failure test deferred (needs DI seam to force deterministic probe failure) — documented.
- Removed any residual references suggesting metrics/endpoint pending; SnapshotArchitecture observability section updated; sprint plan marked DONE with embedded rotation runbook; this story log entry closes Story 4.

Rationale

- Enables safe signing key rotation with observable per-key usage and early detection of configuration errors via validation failure counters + probe health. Provides structured operational checklist (overlap window, success criteria, rollback path) reducing risk of mass 401 incidents.

Files Changed

- `apps/api/Application/Auth/AuthMetrics.cs` — added key rotation counters + helpers.
- `apps/api/App/Infrastructure/Auth/Jwt/JwtTokenService.cs` — added issuance counter increment + refined VerifyAllSigningKeys probe phases.
- `apps/api/Program.cs` — added `/internal/health/jwt-keys` endpoint.
- `apps/api.tests/Auth/KeyRotationMetricsTests.cs`, `apps/api.tests/Auth/JwtKeysHealthEndpointTests.cs` — new tests.
- `SnapshotArchitecture.md`, `devInfo/jwtRefactor/bdlSprintPlan.md` — documentation updates.

Quality Gates

- New tests passing locally (metrics + endpoint). Existing rotation tests remain green. No build or analyzer regressions. (Full suite run scheduled at start of Story 5.)

Runbook (Condensed)

1. Add new key (A -> A,B). Deploy.
2. Observe both kids counters increment; probe_result true; zero validation_failure.
3. After access TTL + buffer, remove old key (A,B -> B). Deploy.

---

### 2025-09-26 - Story: Admin Org Settings Proxy Fix — ✅ DONE

Summary

- Added tenant admin-protected proxy route `/api-proxy/tenants/settings` (GET/PUT) to forward settings reads and updates with cookie bridging, closing the loop that previously booted admins back to login during Org Settings navigation.
- Introduced `/api-proxy/metadata/denominations` handler to expose metadata without a tenant requirement, ensuring the settings form can hydrate denomination options.
- Documented new handlers inline and covered them with Vitest suites asserting guard behavior, tenantless proxy context, and successful upstream forwarding.

Quality Gates

- `pnpm -C apps/web test` — Passed.

Follow-ups / Deferred

- None.

---

### 2025-09-25 - Story: Tenant redirect loop debugging — 🚧 IN PROGRESS

Summary

- Propagate the API-issued `rt` refresh cookie to browser clients during both credential and magic-link sign-ins by syncing `Set-Cookie` headers inside the NextAuth authorize flow, ensuring proxy requests can immediately refresh neutral and tenant tokens.
- Centralized cookie header parsing in `cookieUtils` and reused it from the proxy header builder, hardening fallback handling when multiple cookies arrive via a single header string and adding unit coverage for the parser.
- Confirmed `/api-proxy` routes can now establish access tokens post-login without 401 loops, eliminating the runaway profile hydration retries that triggered “Maximum update depth exceeded.”

Quality Gates

- `pnpm -w test -w --filter @appostolic/web` — Passed (66 files, 209 tests).

Follow-ups / Deferred

- Monitor the live login flow to verify the tenant selector no longer re-enters the loop and add an end-to-end test once the regression harness is available.

4. Verify only kid=B increments; probe_result true; 401 rate stable.
5. If anomaly (401 spike or validation_failure>0) revert to previous list and investigate.
6. After two clean rotations, securely destroy retired key material.

Follow-ups

- Story 5 tracing enrichment (`auth.trace.enriched_spans`).
- Consider DI seam for forced probe failure if operational need arises.
- Potential security event emission threshold for repeated validation failures.

Summary

- Resolved the sole remaining failing test (`Login_Omits_Plaintext_When_Disabled`) caused by implicit assumption that the transitional flag `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` defaulted to `false` after earlier rollback to `true` for compatibility. Patched `RefreshPlaintextExposedFlagTests` so every suppression scenario explicitly overrides the flag to `false` via `WebAppFactory.WithSettings` (keeps cookie + grace settings explicit where relevant). Re‑ran the entire solution test suite: 0 failed / 253 passed / 1 skipped (254 total, ~38s local). Confirms no other suites depended on implicit plaintext suppression and that deterministic auth seeding + focused flow tests coexist cleanly with transitional exposure default.

Rationale

- Makes suppression tests self-describing and immune to future default flips; eliminates hidden coupling on global factory configuration while we retain plaintext emission in other auth tests during the staged deprecation window.

Files Changed

- `apps/api.tests/Auth/RefreshPlaintextExposedFlagTests.cs` — added explicit `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT="false"` in all three tests plus clarifying comments.

Quality Gates

- Full solution test run green (0 failed, 253 passed, 1 skipped). No production code touched; only test harness modifications. Auth flow, refresh rotation, logout, and suppression scenarios all validated concurrently.

Follow-ups

- Later story: flip global default to `false` after verifying no client reliance on plaintext remains; then remove flag entirely. Optional guard test to assert absence of `refresh.token` when default suppressed.

---

### 2025-09-23 - Story: Finalize Auth Test Migration (Contract Test Refactor & Plaintext Suppression Scenarios)

Refactored `AgentTasksAuthContractTests` off `AuthTestClientFlow` to deterministic tenant token issuance (seeding tenant, user, membership and minting JWT in-process) eliminating 400 select-tenant failure. Retained default `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT=true` in `WebAppFactory` to avoid breaking existing tests that still validate body `refresh.token` during grace. Instead, updated `RefreshPlaintextExposedFlagTests` to explicitly disable the flag per test (renamed methods) to assert suppression behavior. Next step (future story): flip global default to false once all legacy token-body dependencies are removed.
2025-09-23 — Auth/JWT: Flow Test Regression Triage & Stabilization — ✅ DONE

- Summary
  - After migrating non-flow endpoint tests to deterministic token issuance, a second wave of 11 auth flow test failures surfaced (login, logout, select-tenant, refresh grace/rotation) caused by prematurely setting the test default `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT=false`. Core flow tests still asserted presence/rotation of `refresh.token` in JSON responses during the transitional grace period. Restored the default to `true` inside `WebAppFactory` with an explanatory comment and re-ran targeted failing tests (`Login_ReturnsNeutralAccessAndRefreshToken`, `SelectTenant_Succeeds_RotatesRefreshToken`) followed by the full auth namespace (21 tests) — all passing (0 failures). Confirms no underlying rotation/logout logic regression; failures were configuration misalignment.
- Rationale
  - Maintain transitional behavior until every test and client code path no longer inspects plaintext refresh tokens. Avoids false-negative regression noise while continuing incremental migration (per-test suppression remains covered by `RefreshPlaintextExposedFlagTests`).
- Files changed
  - apps/api.tests/WebAppFactory.cs — reset `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT` default to `true` with transitional comment.
- Quality Gates
  - Targeted and namespace-scoped test runs green (21/21 auth tests). No compilation or analyzer errors introduced.
- Follow-ups
  - Proceed to remove remaining legacy body-token dependencies; then flip default to `false` and update suppression tests accordingly.

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
  - Migrated AgentTasks authentication and list/filter pagination tests away from legacy shortcut token bootstrap (`EnsureTokens` + static `TenantToken`) and brittle dev-header influenced assertions to real production flows using `AuthTestClientFlow.LoginAndSelectTenantAsync` with password seeding and membership creation. `AgentTasksAuthContractTests` now performs password seeding plus `AuthTestClient.LoginAndSelectTenantAsync` for tenant-scoped access instead of relying on a pre-minted static token. In `AgentTasksListFilterPaginationTests`, removed non-deterministic assertions that assumed ≥2 tasks and email-based free-text matches (side-effects of dev header / seeding shortcuts). Rewrote free-text search test to assert only created task input substring presence and agent filter test to count actual `agentId` matches in returned JSON. Ensures AgentTasks suite fully exercises production JWT issuance (password hash verification, refresh issuance, tenant selection) with deterministic, data-owned assertions.
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

  ### 2025-09-24 - Story: Sliding Refresh Window & Absolute Max Lifetime (Story 11 Enhancement) — ✅ DONE

  Summary
  - Added missing sliding window + absolute max lifetime semantics to the general `/api/auth/refresh` endpoint (previously only present in the `/auth/select-tenant` path). Logic now extends refresh expiry by up to `SlidingWindowDays` from current time while never surpassing the original session anchor (`OriginalCreatedAt` or `CreatedAt`) plus `MaxLifetimeDays`. If cap reached/exceeded, endpoint returns 401 `{ code: "refresh_max_lifetime_exceeded" }` with metrics & security event instrumentation.
  - Standardized refresh TTL sourcing for initial issuance (login, invite/magic consume, select-tenant) to prefer IConfiguration (`AUTH__JWT__REFRESH_TTL_DAYS`) with environment fallback, ensuring test `WithSettings` overrides control initial expiry (fixing earlier failing sliding extension assertions caused by hardcoded env 30-day default).
  - Implemented configuration-first TTL + sliding logic comments referencing Story 11 for maintainability.
  - All `SlidingRefreshTests` now pass (3/3): extension within window, clamped extension near cap, denial after exceeding absolute lifetime. Previous failures (2/3) stemmed from initial TTL mismatch.
  - Metrics: Reused `AuthMetrics.IncrementRefreshMaxLifetimeExceeded`; failure reason `refresh_max_lifetime_exceeded` now increments generic refresh failure counter and duration histogram with enriched trace span outcome tags.

  Rationale
  - Ensures consistent session longevity semantics across all rotation paths, preventing indefinite extension via frequent refresh while maintaining user-friendly sliding behavior within allowed window.

  Files Changed
  - `apps/api/App/Endpoints/V1.cs` — Added sliding & cap logic to general refresh; updated login and related issuance blocks to configuration-first TTL.
  - (Prior fix) `apps/api.tests/Auth/SlidingRefreshTests.cs` — Service provider corrected to cloned factory to avoid cross-factory in-memory DB mismatch.

  Quality Gates
  - Targeted run: Sliding refresh suite PASS (3/3) post-changes.
  - No other auth suite regressions observed in spot runs (avatar/password endpoints still green). Full suite run pending CI (low risk change surface confined to TTL computation & conditional failure path).

  Follow-ups / Deferred
  - Potential helper extraction (ComputeEffectiveRefreshTtl) to DRY logic between select-tenant & refresh endpoints (deferred to avoid churn ahead of stabilization).
  - Add explicit metric assertion test for max lifetime exceeded path (optional hardening).
  - Evaluate need for sub-day precision if future shorter refresh TTLs adopted.

  Architecture / Docs
  - `SnapshotArchitecture.md` to be updated to note universal sliding window + cap enforcement (pending routine story closure update).
  - `LivingChecklist.md` timestamp updated; Story 11 already marked complete (this enhancement finalizes semantics rather than adding a new checklist item).

  ***

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
  - Establishes clear roadmap for deprecation and removal of dev header support, minimizing risk of regression or confusion from residual code. Phased approach allows incremental migration and verification of test coverage parity with production auth flows.
- Follow-ups
  - Story 0: Consolidate test token helpers to remove dev header dependencies.
  - Story 1: Migrate integration tests to use JWT helpers; introduce deprecation middleware.
  - Story 2: Remove DevHeaderAuthHandler and composite scheme; update documentation and rollback tag.
  - Story 3: Final regression guard and closure tasks.

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
  - Establishes clear roadmap for deprecation and removal of dev header support, minimizing risk of regression or confusion from residual code. Phased approach allows incremental migration and verification of test coverage parity with production auth flows.
- Follow-ups
  - Story 0: Consolidate test token helpers to remove dev header dependencies.
  - Story 1: Migrate integration tests to use JWT helpers; introduce deprecation middleware.
  - Story 2: Remove DevHeaderAuthHandler and composite scheme; update documentation and rollback tag.
  - Story 3: Final regression guard and closure tasks.

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
  - Establishes clear roadmap for deprecation and removal of dev header support, minimizing risk of regression or confusion from residual code. Phased approach allows incremental migration and verification of test coverage parity with production auth flows.
- Follow-ups
  - Story 0: Consolidate test token helpers to remove dev header dependencies.
  - Story 1: Migrate integration tests to use JWT helpers; introduce deprecation middleware.
  - Story 2: Remove DevHeaderAuthHandler and composite scheme; update documentation and rollback tag.
  - Story 3: Final regression guard and closure tasks.

2025-09-26 — Auth/JWT: Story 8 Cookie-only Refresh Enforcement — ✅ DONE

- Summary
  - Hardened `/api/auth/refresh` to reject JSON bodies whenever `AUTH__REFRESH_JSON_GRACE_ENABLED` is disabled, returning `refresh_body_disallowed` prior to rate limit checks, and refactored refresh integration suites to drive cookie-only requests via a shared helper.
  - Removed the legacy empty JSON payload from the web refresh clients (`authClient` and proxy headers) and expanded their unit tests to assert bodyless POSTs with cookie credentials only.
  - Updated `devInfo/jwtRefactor/jwtSprintPlan.md` to mark Story 8 complete with the cookie-only enforcement summary.
  - Regression checked with `dotnet test apps/api.tests/Appostolic.Api.Tests.csproj --no-build` and `CI=1 pnpm --filter @appostolic/web test`.
- Files changed
  - `apps/api/App/Endpoints/V1.cs` — Guarded refresh endpoint against JSON payloads when grace disabled; maintained metrics/limiter flow.
  - `apps/api.tests/Auth/*.cs` — Updated refresh, rate limit, logout, session tests to post cookie-only requests and assert new guard codes.
  - `apps/web/src/lib/authClient.ts` & `.test.ts` — Removed placeholder JSON body and validated bodyless fetch; ensured retry path still green.
  - `apps/web/src/lib/proxyHeaders.ts` & `.test.ts` — Stopped sending `Content-Type`/body, added helpers verifying cookie-only refresh behaviour.
  - `devInfo/jwtRefactor/jwtSprintPlan.md` — Marked Story 8 ✅ with completion summary.
- Quality gates
  - `dotnet test apps/api.tests/Appostolic.Api.Tests.csproj --no-build`
  - `CI=1 pnpm --filter @appostolic/web test`
- Follow-ups
  - Monitor rollout before flipping `AUTH__REFRESH_JSON_GRACE_ENABLED` default and pruning residual body parsing code.

### 2025-09-26 - Story: Tenant settings tenant switch reset — 🚧 IN PROGRESS

Summary

- Reset tenant settings client forms when initial props change after a tenant switch so the Organization Settings, Guardrails, Bio, and Logo UI immediately reflect the newly selected tenant and clear stale submission/loading state.
- Normalized social/contact defaults in TenantSettingsForm (shared SOCIAL_KEYS) to avoid undefined fields and ensure merge patches stay minimal.
- Prefer the freshly issued `selected_tenant` cookie when building proxy headers and computing the effective slug for the admin settings page so newly selected tenants render their data immediately; log any session/cookie mismatches for diagnostics.

Quality Gates

- `pnpm --filter @appostolic/web test` — Passed (217 tests).

Follow-ups / Deferred

- Investigate 401 responses when saving immediately after a tenant switch to confirm proxy token refresh timing.

---
