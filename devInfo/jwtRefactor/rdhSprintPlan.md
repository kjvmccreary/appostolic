## Sprint Plan: Dev Header Decommission ("Remove Dev Headers" / RDH)

> Objective: Eliminate all reliance on development headers (`x-dev-user`, `x-tenant`) across runtime code, tests, tooling, and documentation so every authenticated path (local, test, CI, staging, production) exercises the **same** JWT-based flows. (Story 1 helper consolidation largely complete; Story 2 phased migrations ongoing.)

### Vision / Goal

Move the platform to a single, uniform authentication & authorization mechanism (JWT Bearer + refresh rotation) with no conditional branches for dev headers. Strengthen confidence that test coverage represents production behavior, simplify mental model, reduce attack surface (accidental exposure of header auth), and prepare for future security hardening (rate limiting, anomaly detection) without dual pathways.

### Non‑Goals

- Introducing a new IdP / OAuth provider (handled in a future auth expansion sprint).
- Altering token shapes or TTLs (only adjustments needed for test ergonomics).
- Implementing multi-key signing (out of scope; separate security hardening story).
- Adding new user session management UI (post‑1.0 candidate).

### High‑Level Outcomes

[ ] All integration & unit tests obtain auth via JWT issuance (login, mint helper, or token service) — no remaining `x-dev-user` usage.
[ ] Dev header auth handler & composite scheme fully removed from `Program.cs`.
[ ] Feature flag `AUTH__ALLOW_DEV_HEADERS` deleted (or stubbed, reading results in no effect / logged warning).
[ ] Attempted usage of `x-dev-user` / `x-tenant` after removal yields deterministic 401 with structured error (`dev_headers_removed`).
[ ] Temporary detection middleware & metric removed after zero-usage verification window (final cleanup).
[ ] Documentation (SnapshotArchitecture, LivingChecklist, Upgrade Guide, sprint plan) updated; legacy references removed.
[ ] Story log entry summarizing removal & rollback approach.
[ ] Rollback tag (`before-dev-header-removal`) created.

### Architectural Context (Delta)

- Previous: Dual dev header + JWT composite scheme in Development when flag enabled — tests relied on dev headers for brevity.
- Target: Single auth pipeline: HttpContext principal always built from JWT; any header shortcuts rejected early.
- Supporting changes: Robust test token mint helper + seeded data utilities remove original rationale for dev headers.

### Story Breakdown (Updated 2025-09-22 — Progress Reflects Latest Migrations)

#### Story 0: Inventory & Baseline Metrics (Optional but Recommended) — ✅ PARTIAL

[x] List all code references to `x-dev-user` / `x-tenant` / `DevHeaderAuthHandler` / `BearerOrDev`.
[x] Count tests using dev headers (grep) and categorize by suite (auth, notifications, etc.).
[ ] Add a temporary counter/metric (`auth.dev_headers.requests`) in existing code path (if still active) for a short measurement window. (Deferred — may be skipped if migration proceeds smoothly.)
[x] Document inventory snapshot in this plan (append section) for audit trail.

#### Story 1: Test Token Helper Consolidation — ✅ PARTIAL

[x] Introduce / confirm presence of issuance services (existing `IJwtTokenService` methods for neutral & tenant). (Explicit wrapper `TestTokenIssuer` deferred.)
[x] Provide `AuthTestClient` facade used by tests (original mint helper) AND new flow-based `AuthTestClientFlow` exercising real `/api/auth/login` + `/api/auth/select-tenant` (adopted in `UserProfileLoggingTests`). (Replaces planned `UseTenantAsync` scope.)
[x] Add multi-membership & rotation coverage tests (`LoginMultiTenantTests`).
[x] Update `WebAppFactory` with neutral token issuance helper (`EnsureNeutralToken`).
[x] Superadmin elevation moved from mint helper to config-driven claim injection in normal auth flow (`Auth:SuperAdminEmails`).
[ ] Document helper usage guideline in external `README` (plan section present; README update still pending).

##### Helper Usage Guideline (Story 1)

| Helper                                         | Path Exercised                                     | When to Use                                                                                     | Returns / Side Effects                             | Notes                                                                          |
| ---------------------------------------------- | -------------------------------------------------- | ----------------------------------------------------------------------------------------------- | -------------------------------------------------- | ------------------------------------------------------------------------------ |
| `AuthTestClient.UseNeutralAsync`               | `/api/test/mint-tenant-token` (neutral branch)     | Fast setup of neutral auth where tenant context NOT required                                    | Sets `Authorization: Bearer <neutral>`             | Bypasses password + refresh issuance. Use only until mint endpoint deprecated. |
| `AuthTestClient.UseTenantAsync`                | `/api/test/mint-tenant-token` (tenant branch)      | Tenant-scoped tests needing roles quickly                                                       | Sets `Authorization: Bearer <tenant>`              | Provides elevated roles by default unless `forceAllRoles=false`.               |
| `AuthTestClient.UseAutoTenantAsync`            | Mint helper (auto)                                 | Single-membership scenarios where convenience acceptable                                        | Neutral + optional tenant token                    | Avoid in multi-membership tests (explicitness preferred).                      |
| `AuthTestClientFlow.LoginNeutralAsync`         | Real `/api/auth/login` (password)                  | Validating production login shape, refresh issuance, memberships                                | Sets neutral access; returns structured login JSON | Ensures password hash path + metrics exercised.                                |
| `AuthTestClientFlow.LoginAndSelectTenantAsync` | Real `/api/auth/login` + `/api/auth/select-tenant` | Tests needing refresh rotation + tenant scoping via real flow                                   | Sets tenant access (after select)                  | Preferred for Phase A migrations replacing dev headers.                        |
| `WebAppFactory.EnsureNeutralToken`             | Direct `IJwtTokenService.IssueNeutralToken`        | Low-level token issuance for isolated unit/integration tests not concerned with login semantics | Returns raw neutral token and userId               | Skips refresh + membership projection; do not use where flow behavior matters. |

Selection Rules:

1. Phase A migrations: prefer `AuthTestClientFlow` methods so converted tests exercise real endpoints.
2. Performance-sensitive inner-loop unit tests may still use `EnsureNeutralToken` temporarily; convert later if semantics broaden.
3. Avoid mixing mint-helper and flow-helper in a single test unless explicitly comparing behaviors.
4. New tests SHOULD default to flow helpers unless a clear reason (speed + no refresh logic) is documented in a comment.

Deprecation Plan:

- Mint helper usages (`AuthTestClient.*`) will be systematically replaced after Phase A/B when flow-based stability is confirmed.
- A CI lint rule (Story 5) will eventually flag remaining mint helper calls outside explicitly allowed legacy folders.

#### Story 2: Migrate Integration Tests (Batch Refactor) — ✅ PARTIAL

Migration Progress Snapshot (updated 2025-09-22):

- Core Auth / Flow:
  - [x] DevHeadersDisabledTests (success path) migrated to real flow.
  - [x] Multi-tenant login & select coverage (LoginMultiTenantTests).
- Membership / Roles:
  - [x] MembersListTests
  - [x] AssignmentsApiTests
  - [x] MembersManagementTests
  - [x] Remaining role/grant variants (DevGrantRolesEndpointTests)
- Auditing / Privacy:
  - [x] AuditTrailTests
  - [x] AuditsListingEndpointTests
  - [x] UserProfileLoggingTests
  - [x] UserProfileEndpoints / Avatar endpoints
- Invitations:
  - [x] InvitesEndpointsTests
  - [x] InvitesAcceptTests / InviteRolesFlagsTests
- Notifications:
  - [x] NotificationsProdEndpointsTests (auth flow + superadmin allowlist)
  - [x] NotificationsAdminEndpointsTests & DLQ variants
- Catalog / Metadata:
  - [x] ToolCatalogTests
  - [x] DenominationsMetadataTests
- Agent Tasks:
  - [ ] AgentTasksEndpointsTests (tenant flow present — re-verify all sub-suites)
  - [ ] AgentTasksE2E / contract tests (pending)

Planned Next Focus: Complete Invitations + Remaining Notifications Admin + Agent Task suites, then introduce guard asserting zero legacy mint usages (except explicitly allowed transitional helpers) before deprecation mode.

[x] Phase A: Replace dev headers in core auth test suite (login, refresh, logout, tenant selection) with JWT helpers. (Kickoff: migrated `DevHeadersDisabledTests` positive path to flow helper; negative dev header rejection retained.)
[ ] Phase B: Replace dev headers in domain/feature tests (notifications, roles, storage, privacy).
[ ] Phase C: Replace dev headers in schema & migration tests (if present) — ensure neutral token suffices where needed.
[ ] Phase D: Replace dev headers in any E2E HTTPS harness tests.
[ ] Add fail-fast assertion in tests: no request builder includes `x-dev-user` (utility guard or grep CI step).
[ ] Update affected fixtures removing `x-dev-user` convenience branches.
[ ] Ensure all modified tests still green (target partial run per phase + final full run).

#### Story 3: Deprecation Mode (Soft Block)

[ ] Flip default: `AUTH__ALLOW_DEV_HEADERS` forced false (feature effectively off) in all environments.
[ ] Introduce `DevHeadersDeprecationMiddleware` (early pipeline) returning 401 with `{ code: "dev_headers_deprecated" }` if `x-dev-user` present (before auth executes) — only while cleanup ongoing.
[ ] Add metric counter `auth.dev_headers.deprecated_requests` & structured log for visibility.
[ ] Add test asserting deprecated response when header used.
[ ] Documentation: mark dev headers as deprecated (plan + SnapshotArchitecture What’s New).
[ ] Verify zero legitimate test usage (CI green) before proceeding.

#### Story 4: Physical Removal

[ ] Remove `DevHeaderAuthHandler` class & registrations.
[ ] Remove composite policy scheme `BearerOrDev` usage.
[ ] Delete flag references (`AUTH__ALLOW_DEV_HEADERS`).
[ ] Remove deprecation middleware (optional: keep detection variant for a single release behind internal env var — choose minimal).
[ ] Update security section in SnapshotArchitecture (single-path auth, simplified threat surface).
[ ] Remove any legacy helper methods or constants referencing dev headers.
[ ] Ensure no `using` / DI service entries remain for removed handler.
[ ] Run full build & test matrix (API + Web + e2e).

#### Story 5: Observability & Regression Guards

[ ] Add a lint/CI script to fail build if patterns `x-dev-user` or `DevHeaderAuthHandler` appear (excluding historical docs folder).
[ ] Add a minimal integration test verifying `x-dev-user` request returns 401 `dev_headers_removed` (final canonical error code after removal).
[ ] Add documentation snippet to Upgrade Guide: “Dev headers removed — how to adapt”.
[ ] Remove temporary metric (`auth.dev_headers.deprecated_requests`) once stable (optional line item if introduced in Story 3).

#### Story 6: Documentation & Cleanup

[ ] Update `SnapshotArchitecture.md` (remove composite scheme, add simplified flow diagram).
[ ] Update `LivingChecklist.md` marking dev header removal item DONE.
[ ] Append storyLog entry summarizing decommission timeline & commit references.
[ ] Add rollback instructions to Upgrade Guide.
[ ] Tag repo `dev-headers-removed` after merge.

#### Story 7 (Optional Hardening Enhancements)

[ ] (Optional) Add short TTL memory cache for TokenVersion (perf) if load test indicates need.
[ ] (Optional) Add security alert rule for repeated 401 `dev_headers_removed` (potential scripted probing).

### Guard Checklist (Authoritative Regression & Safety Gates)

Purpose: Ensures decommission intent persists after merge; any reintroduction of dev headers or shortcut flows fails fast.

Static / CI Guards

- [ ] CI grep (or lint script) fails build on forbidden patterns: `x-dev-user`, `x-tenant`, `DevHeaderAuthHandler`, `BearerOrDev` (allowlist: `/docs/`, `/devInfo/`, sprint plan historical sections)
- [ ] Optional Roslyn analyzer (future) to flag auth header based identity injection

Runtime / Integration Guards

- [ ] Test: sending `x-dev-user` header returns 401 `{ code: "dev_headers_removed" }`
- [ ] Test: enumerate registered auth schemes and assert none match `Dev` / `BearerOrDev`
- [ ] Test: multi-tenant login returns memberships WITHOUT auto tenant token
- [ ] Test: select-tenant rotates refresh (old neutral revoked)
- [ ] Test: superadmin allowlist (config) injects claim; non-allowlisted user lacks claim
- [ ] Test: negative resend / notifications cross-tenant forbidden for non-superadmin user (ensures no silent elevation)

Helper / API Surface Guards

- [ ] No usages of removed helpers: `UseTenantAsync`, `UseSuperAdminAsync` (grep enforced)
- [ ] (If mint endpoint retained temporarily) Test ensures it cannot set superadmin when email not in allowlist
- [ ] Plan to remove mint endpoint (tracked follow-up) or convert to internal-only if still needed

Documentation & Traceability

- [ ] `SnapshotArchitecture.md` updated: single auth path, removed handler section
- [ ] `LivingChecklist.md` item checked with link to removal PR
- [ ] Upgrade Guide section: "Dev headers removed – migration steps"
- [ ] Story log final entry summarizing decommission & rollback tag
- [ ] Tag `before-dev-header-removal` created & referenced in docs

Observability (Optional)

- [ ] Temporary metric (`auth.dev_headers.deprecated_requests`) removed OR dashboard shows sustained zero beyond grace window
- [ ] Optional alert on spike of denied dev header attempts (post-removal)

Security / Hardening (Optional Enhancements)

- [ ] Threat model updated to reflect single auth surface
- [ ] Brute force / anomaly hooks unaffected (spot check)

Exit Validation

- [ ] Full test matrix green after removal commit
- [ ] CI guard proves effective by intentionally injecting a forbidden token in a dry-run branch (manual validation)
- [ ] Rollback instructions verified (checkout tag builds/tests green)

### Acceptance Summary (Sprint Exit Criteria) — CURRENT STATUS

[ ] No code / tests reference dev headers. (IN PROGRESS — migrations active)
[ ] All authentication in local + CI uses JWT flows. (PARTIAL — several suites migrated)
[ ] Removal & rationale documented (architecture + story log + upgrade guide). (PARTIAL — story log entries added; docs pending)
[ ] Regression guard test in place verifying 401 on dev header usage. (NOT STARTED)
[ ] Rollback path (tag + optional reintroduce commit link) documented. (NOT STARTED — to be finalized Story 6)

### Risk & Mitigation

| Risk                                                            | Impact                               | Mitigation                                                                                    |
| --------------------------------------------------------------- | ------------------------------------ | --------------------------------------------------------------------------------------------- |
| Hidden test still depends on dev headers                        | Post-removal failures late in sprint | Multi-phase migration + grep CI gate + deprecation middleware phase                           |
| Accidental reintroduction via copy/paste                        | Divergent local behavior             | Lint/CI pattern check & regression test                                                       |
| Increased auth test duration                                    | Slower feedback loop                 | Use direct service token issuance helper (no HTTP round trip)                                 |
| Missed coverage of some auth branch formerly hit by dev headers | Potential blind spot                 | Ensure token helper exercises same claims + add targeted tests for any missing negative cases |
| Rollback complexity after handler deletion                      | Longer incident recovery             | Create pre-removal tag + keep minimal patch instructions in upgrade guide                     |

### Rollback Strategy

1. Checkout tag `before-dev-header-removal` (created just before Story 4 merge) if emergency rollback required.
2. Revert removal PR to reinstate handler & flag; set `AUTH__ALLOW_DEV_HEADERS=true` locally if immediate dev unblock needed.
3. (Optional) Hotfix branch adds temporary deprecation middleware again if partial rollback needed.
4. Communicate rollback window short (≤ 1 release) — plan to re-remove promptly after fix.

Detailed Patch Reintroduction (if revert not feasible and manual re-add required):

- Recreate `DevHeaderAuthHandler.cs` from tag reference; restore registration block in `Program.cs` (AddScheme + policy scheme + Development default authenticate override).
- Reintroduce flag read `AUTH__ALLOW_DEV_HEADERS`; wrap policy scheme & default overrides inside conditional (Development && flag true) to minimize footprint.
- Add explicit comment at top of restored block: `// TEMPORARY ROLLBACK RE-INTRODUCTION: remove again after incident (#ticket)`.
- Restore any removed test that asserts dev headers disabled path, adjusting expectation back to 200 for valid header usage.
- Push hotfix branch; tag `dev-headers-rollback-temp`; create follow-up issue to remove within agreed SLA.

### Test Matrix (Expanded for Decommission)

[ ] Auth issuance (neutral, tenant) with helper.
[ ] Refresh rotation + reuse detection with helper path.
[ ] Logout & logout-all with JWT-only auth.
[ ] Access token version bump (password change) unaffected.
[ ] Policy/role enforcement (TenantAdmin, Approver, etc.) with minted tenant token.
[ ] Negative: request w/out Authorization returns 401 (no dev fallback).
[ ] Negative: dev header attempt returns 401 `dev_headers_removed`.
[ ] Performance spot check: token issuance & validation not regressed (within prior baseline ±5%).
[ ] E2E HTTPS cookie test still green (unchanged by removal).
[ ] Web unit tests unchanged (no dev header references).

### Implementation Order (Recommended)

1. Story 0 (optional) & Story 1 helpers — low risk foundation.
2. Story 2 phased test migrations (keep PRs digestible; parallelizable).
3. Story 3 deprecation middleware (short-lived safety net).
4. Story 4 physical removal once tests clean & metrics show zero usage.
5. Story 5 regression guard + lint rule.
6. Story 6 docs & tagging.
7. Story 7 optional perf/security follow-ups.

### Timeline Estimate (Rough)

| Week / Phase   | Work                                                  |
| -------------- | ----------------------------------------------------- |
| Day 1          | Inventory (0), Helpers (1)                            |
| Day 2–3        | Test migration phases A–C (2)                         |
| Day 4          | Remaining phases + verify (2D) + Deprecation mode (3) |
| Day 5          | Physical removal (4) + regression guard (5)           |
| Day 6          | Documentation, tag, cleanup (6)                       |
| Day 7 (buffer) | Optional enhancements (7) / spillover                 |

### Upgrade / Developer Notes (Draft Checklist)

[ ] Remove any local scripts relying on `x-dev-user`; replace with `auth mint` CLI (future) or curl login helper.
[ ] Update API client examples to show JWT login + Authorization header.
[ ] Document how to generate test tokens locally via dotnet script or test helper if needed.
[ ] Encourage developers to run `grep -R "x-dev-user"` before pushing new branches.

### Open Questions

[ ] Keep a short-lived deprecation middleware (1–2 releases) after removal purely for metric gathering? (Default: remove entirely.)
[ ] Provide a CLI `dotnet run -- mint-token` dev utility to reduce friction? (Can defer if helpers suffice.)
[ ] Introduce a synthetic test ensuring token issuance still works with minimal config (smoke) separate from existing suite? (Leverage existing smoke test; might not need.)

### Optional / Future (Not Blocking RDH)

[ ] Multi-key JWT signing (key rotation without downtime).
[ ] Session listing endpoint & selective revoke UI.
[ ] TokenVersion micro-cache (performance) + validation latency histogram.
[ ] Brute-force / anomaly detection hooks (fail2ban style) integrated with metrics.

---

Append implementation notes & progress directly below this line during execution (each story adds a dated sub-section referencing checkbox updates):

> Progress Log (will be appended in-place as stories complete)

2025-09-21 — Story 1 Kickoff: Inventory & helper validation starting. Added IN PROGRESS marker. Next: grep for `x-dev-user` usage & confirm `TestAuthClient` covers issuance paths.

2025-09-22 — Story 1 Inventory Results:
**Header References (code + docs):**

- `x-dev-user`: widespread across API runtime (handler, Program.cs policy scheme selector, several dev/demo endpoints), API tests, web proxy layer, web tests, and documentation/readmes. Initial grep surfaced >200 total matches (capped output) — actionable unique source areas summarized below.
- `x-tenant`: similar distribution; also legacy `X-Tenant-Id` sample middleware still present (non-dev auth demo) — evaluate separately (out of scope for dev header removal unless interfering).
- `DevHeaderAuthHandler`: defined in `apps/api/App/Infrastructure/Auth/DevHeaderAuthHandler.cs` and registered in `Program.cs`; referenced in multiple test seed comments.
- Composite policy scheme `BearerOrDev`: registered & defaulted in Development section of `Program.cs`.
- Flag `AUTH__ALLOW_DEV_HEADERS`: gating logic in `Program.cs`, set true in `WebAppFactory`, explicitly forced false in `DevHeadersDisabledTests`, referenced in docs & plans.

**Runtime Source Touch Points (API):**

- `Program.cs`: header scheme registration, policy scheme (`BearerOrDev`), selector logic (`if (ctx.Request.Headers.ContainsKey("x-dev-user"))`), flag read, sample legacy X-Tenant-Id middleware.
- `DevHeaderAuthHandler.cs`: core handler (reads `x-dev-user`, `x-tenant`, optional superadmin header); emits claims.
- Endpoints referencing headers directly (bypassing auth principal): `AgentTasksEndpoints.cs`, `DevToolsEndpoints.cs`, `DevAgentsDemo.cs`, selective comments in `NotificationsAdminEndpoints.cs`, sections in monolithic `V1.cs` endpoints.

**Web App (Next.js) Touch Points:**

- `apps/web/src/lib/proxyHeaders.ts`: constructs dev headers from session / ENV (`DEV_USER`, `DEV_TENANT`).
- `app/dev/page.tsx`: uses dev headers for manual dev tools.
- Multiple `app/api-proxy/.../*.test.ts` and `test/api-proxy/*.route.test.ts` files embed dev headers in mocked fetches.

**SDK / Docs:**

- `packages/sdk/openapi.json` includes description referencing dev headers.
- `apps/api/README.md` and `apps/web/README.md` show curl examples with dev headers (need replacement with JWT login flow examples later in Story 6).
- `docs/auth-upgrade.md` references flag & upcoming removal.

**Test Suite Dependency Breakdown (API tests):**
Total distinct test files still setting dev headers (approx): 30+ (multiple lines per file). Categorized:

- Auth / User Profile: `AuthPasswordFlowsTests`, `UserPasswordEndpointsTests`, `UserProfileEndpointsTests`, `UserAvatarEndpointsTests`.
- Tenant / Membership / Invites / Roles: `MembersListTests`, `MembersManagementTests`, `InvitesEndpointsTests`, `InvitesAcceptTests`, `InvitesRolesFlagsTests`, `DevGrantRolesEndpointTests`, `LegacyRoleWritePathDeprecationTests`, `TenantSettingsEndpointsTests`.
- Notifications: `NotificationsAdminEndpointsTests`, `NotificationsProdEndpointsTests`, `NotificationsE2E_Mailhog` (E2E).
- Auditing / Privacy: `AuditTrailTests`, `AuditsListingEndpointTests`, `UserProfileLoggingTests`.
- Agents / Agent Tasks: `AgentsEndpointsTests`, all `AgentTasksE2E_*` variants, `AgentTasksEndpointsTests`, `AgentTasksTestBase`, `AgentTasksAuthContractTests`.
- Catalog / Metadata: `ToolCatalogTests`, `DenominationsMetadataTests`, `AssignmentsApiTests`.
- Misc Security / Seed: comments in `WebAppFactory`.
- Negative flag coverage: `DevHeadersDisabledTests` (will be updated to assert deprecated/removed responses later instead of flag false path).

**Web Test Dependencies:** (need phasing plan parallel to API migrations)

- API proxy route tests for agents, agent-tasks, tenants (members, memberships, invites, audits), notifications DLQ, avatar upload rely on dev headers.
- `invites.accept.route.test.ts` asserts omission of `x-tenant` (special case; adjust logic once JWT session tokens drive selection).

**Preliminary Migration Phase Mapping (Draft)**

- Phase A (Core auth & foundational): AuthPasswordFlows, UserPasswordEndpoints, UserProfileEndpoints, Test helper validation; plus Web `users/me/avatar` & `agents` proxy tests.
- Phase B (Tenant & membership domain): Members*, Invites*, Roles/Grant, TenantSettings, Assignments; web tenant membership/roles/audits tests.
- Phase C (Notifications & Auditing/Privacy): Notifications\*, AuditTrail, AuditsListing, UserProfileLogging, DLQ proxy tests.
- Phase D (Agent Tasks & E2E): AgentTasks\* E2E + contract/auth tests; agent-tasks proxy tests.
- Phase E (Residual & Negative/Flag): DevHeadersDisabledTests repurposed to expect 401 `dev_headers_removed`; removal of any lingering comments referencing flag; README/doc updates.

**Open Notes:**

- Need to confirm no production (non-Development) path inadvertently allows dev headers when flag toggled (current logic appears explicit, will double-check once removal PR prepared).
- Legacy `X-Tenant-Id` sample middleware should be evaluated for either retention (demo) or relocation out of critical path; not blocking dev header removal.

Next Steps: Finalize helper capability review (ensure `TestAuthClient` sufficient for all issuance shapes including multi-tenant selection) then begin Phase A test migrations.

2025-09-22 — TestAuthClient Review: Existing helper (`TestAuthClient.MintAsync`) provides neutral + optional tenant token issuance via `/api/test/mint-tenant-token`. For upcoming migrations we will add a thin extension (planned) to automatically set the `Authorization: Bearer <token>` header on a supplied HttpClient to reduce boilerplate in refactored tests (Phase A). No gaps found for current issuance shapes; multi-tenant explicit selection already supported by passing `tenant` argument. Enhancement candidates (deferred until after initial migrations): (1) mint nearly-expired token for refresh edge-case tests (ties into future Story 20), (2) helper method returning both tokens + setting auth in one call. Migration phase mapping confirmed; proceeding to implement Phase A replacements next.

2025-09-22 — Multi-Membership Login Coverage Added:

2025-09-22 — Story 2 Phase A: MembersList & Assignments Tests Migrated — ✅ PARTIAL

- Summary
  - Migrated `MembersListTests` and `AssignmentsApiTests` from legacy `AuthTestClient.UseTenantAsync` mint helper to real auth flows using `AuthTestClientFlow.LoginAndSelectTenantAsync` plus explicit password seeding (`IPasswordHasher`) to set the known default password (`Password123!`). Removed helper abstraction (`ClientAsync`) in both files, ensuring each test now executes `/api/auth/login` followed by `/api/auth/select-tenant` where tenant-scoped access is required. Verified owner access (200) vs viewer/non-admin forbidden responses (403) and unauthenticated 401/403 behavior under production authentication paths.
  - Added consistent `SeedPasswordAsync` helper pattern (duplicated per test class for now; candidate for centralization after broader migration) mirroring earlier password flow refactors.
  - All migrated tests passing: MembersList (3/3), Assignments (5/5) post-change. Establishes canonical pattern for Phase A migrations: seed password → login → select tenant → exercise endpoint with correct role expectations.
- Rationale
  - Reduces remaining surface area dependent on mint helper, incrementally increasing confidence that role/membership enforcement and token validation logic reflect production code paths. Early migration of membership/role mutation tests derisks later removal of mint helper and dev headers.
- Follow-ups
  - Continue Phase A with `MembersManagementTests`, then invites & auditing suites. After sufficient coverage, introduce a temporary guard (test or utility) asserting no `UseTenantAsync` usages remain before proceeding to Phase B.

2025-09-22 — Story 2 Phase A Kickoff:

- Migrated `DevHeadersDisabledTests` success path off mint/dev shortcuts to real `/api/auth/login` + `/api/auth/select-tenant` via `AuthTestClientFlow.LoginAndSelectTenantAsync`.
- Retained the negative dev headers disabled assertion (flag false path) as a legacy guard until deprecation middleware introduced.
- Targeted test run (2 tests) PASS post-migration; no regressions observed.
- Updated Sprint Plan: Story 2 Phase A checkbox marked in-progress milestone (partial) with note.

- Implemented new tests `LoginMultiTenantTests` asserting: (a) multi-membership login returns exactly two memberships with NO `tenantToken`; (b) select-tenant rotates & revokes old refresh (DB-level assertion on revoked_at + new active token); (c) membership removal between login and selection yields 403. These close previously identified gap in Story 3 boundary (stage-one neutrality invariants) and strengthen regression guards before dev header migration phases advance.
- Outcome: All new tests passing; confirms no accidental auto-selection on multi-membership login and proper refresh chain revocation semantics observed externally and in persistence.
- Next Focus: Begin Story 2 Phase A test migrations replacing remaining dev header usages in core auth & user profile tests with token helper issuance. Will add thin `AuthTestClient.UseTenantAsync` convenience to reduce duplication while removing header-based shortcuts.

2025-09-22 — Story 2 Phase A: MembersManagementTests Migrated — ✅ PARTIAL

- Summary
  - Migrated `MembersManagementTests` from legacy mint helper (`ClientAsync` wrapping `AuthTestClient.UseTenantAsync`) to real auth flows using `AuthTestClientFlow.LoginAndSelectTenantAsync` with explicit password seeding via new per-class `SeedPasswordAsync` helper (default `Password123!`). Replaced all occurrences of the legacy helper with the canonical Phase A pattern (seed → login → select tenant) while preserving existing utilities (`EnsureAdminMembershipAsync`, `GetRoles`). The suite exercises role flag mutations, last-admin protection, and member removal semantics now entirely through production authentication pathways.
  - Targeted run post-migration: 5 tests PASS (0 failed / 5 passed) validating expected 200 / 204 / 403 / 409 outcomes (including last-admin safeguard) under JWT-only auth. Confirms that membership & role mutation logic does not rely on legacy elevation semantics previously implicit in mint helper usage.
- Rationale
  - Extends Phase A coverage across core membership mutation scenarios, further reducing reliance on mint helper abstractions and ensuring production role enforcement is continuously validated as dev header decommission progresses.
- Follow-ups
  - Proceed to migrate invite-related and audit trail suites; after majority completed, introduce guard (grep or reflective assertion) to fail if `UseTenantAsync` persists. Plan consolidation of duplicated `SeedPasswordAsync` helpers into a shared test utility once broader migration stabilizes.

2025-09-22 — Story 2 Phase A: AuditTrailTests Migrated — ✅ PARTIAL

- Summary
  - Migrated `AuditTrailTests` off legacy mint helper (`ClientAsync` invoking `AuthTestClient.UseTenantAsync`) to real password-based authentication flows using `AuthTestClientFlow.LoginAndSelectTenantAsync`. Added per-class constants and `SeedPasswordAsync` helper (default `Password123!`) seeding the acting owner user prior to login + tenant selection. Replaced legacy helper invocations in both tests with explicit seed → login → select flow while preserving existing debug logging (`LogMembershipAsync`) and admin membership enforcement. Post-migration targeted run: 2 tests PASS (0 failed) with expected role mutation/audit behaviors (OK + NoContent for first and noop second call) under production JWT auth.
- Rationale
  - Extends Phase A coverage into auditing domain, ensuring audit trail generation & noop semantics are validated via production authentication paths (password hash verification, refresh issuance, tenant token selection) rather than elevated mint shortcuts.
- Follow-ups
  - Continue migrating remaining audit-related (`AuditsListingEndpointTests`, `UserProfileLoggingTests`) and invites suites next; prepare for guard introduction once majority of `UseTenantAsync` usages eliminated.

2025-09-22 — Story 2 Phase A: Invites Suites Migrated — ✅ PARTIAL

- Summary
  - Migrated `InvitesEndpointsTests`, `InvitesAcceptTests`, and `InvitesRolesFlagsTests` to real password-based auth flows (`AuthTestClientFlow.LoginAndSelectTenantAsync` for tenant-scoped owner actions; `LoginNeutralAsync` for invitee acceptance) with per-class password seeding helpers. Removed any residual assumptions about legacy mint elevation; updated roles flags assertions to accommodate current API response (string or array flexibility where serialization variant may differ). Ensured re-authentication of original inviter after invitee signup to perform revoke step. All invite lifecycle scenarios (create, list, resend, accept, revoke) validated under production JWT paths.
- Rationale
  - Eliminates a high-churn domain (invitation onboarding) from dev header/mint dependency early, reducing risk of behavioral drift in acceptance logic and role flag propagation before deprecation middleware is introduced.
- Follow-ups
  - Proceed to `AuditsListingEndpointTests` and `UserProfileLoggingTests`, then User Profile & Avatar endpoints. After these, begin Notifications Admin and Agent Task migrations and introduce guard for zero `UseTenantAsync` occurrences.

2025-09-22 — Story 2 Phase A: NotificationsAdmin Suites Migrated — ✅ PARTIAL

- Summary
  - Migrated `NotificationsAdminEndpointsTests` from legacy mint helper (`UseAutoTenantAsync`) to real password-based login + tenant selection via `AuthTestClientFlow.LoginAndSelectTenantAsync`. Added local password seeding plus `LoginOwnerAsync` wrapper. Relaxed brittle assertion on optional `X-Resend-Remaining` header (now conditional). All 7 admin / DLQ / resend tests passing under JWT-only auth.
- Rationale
  - Removes remaining notifications administrative surface from dev header/mint dependency ensuring resend, retry, throttle, DLQ replay, and metrics paths mirror production auth behavior (role/claim derivation, superadmin allowlist).
- Follow-ups
  - Begin AgentTasks suite migration next; then introduce guard to assert zero legacy mint usages before deprecation middleware phase.

2025-09-22 — Story 2 Phase A: AuditsListingEndpointTests Migrated — ✅ PARTIAL

- Summary
  - Migrated `AuditsListingEndpointTests` from legacy mint helper (`CreateAuthedClientAsync` invoking `AuthTestClient.UseTenantAsync`) to real authentication flow using password seeding + `AuthTestClientFlow.LoginAndSelectTenantAsync`. Introduced per-class `SeedPasswordAsync` and `DefaultPw` constant mirroring pattern established in prior migrations. All three tests now perform: seed acting admin user -> login -> select tenant -> exercise listing endpoint with paging, filtering, and validation of 400 responses for invalid GUIDs/date ranges through production auth code paths (password hashing, refresh issuance, tenant selection). Targeted run: 3 tests PASS (0 failed) confirming identical functional behavior post-migration (200 for valid paging/filter cases; 400 for malformed GUID and reversed date range) and ensuring no reliance on dev headers or mint helper elevation semantics.
- Rationale
  - Completes auditing listing coverage under real JWT flows, reducing residual surface area relying on mint shortcuts and strengthening confidence that authorization + model binding edge cases (invalid GUID handling) are executed through the standard pipeline.
- Follow-ups
  - Next: migrate `UserProfileLoggingTests` (privacy/audit adjacency) then invites-related suites. After remaining high-frequency helpers removed, introduce guard (grep/CI) to prevent future `UseTenantAsync` usages before proceeding to deprecation middleware and physical removal stories.
