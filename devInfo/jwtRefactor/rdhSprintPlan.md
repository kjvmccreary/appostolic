## Sprint Plan: Dev Header Decommission ("Remove Dev Headers" / RDH)

> Objective: Eliminate all reliance on development headers (`x-dev-user`, `x-tenant`) across runtime code, tests, tooling, and documentation so every authenticated path (local, test, CI, staging, production) exercises the **same** JWT-based flows.

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

### Story Breakdown

#### Story 0: Inventory & Baseline Metrics (Optional but Recommended)

[ ] List all code references to `x-dev-user` / `x-tenant` / `DevHeaderAuthHandler` / `BearerOrDev`.
[ ] Count tests using dev headers (grep) and categorize by suite (auth, notifications, etc.).
[ ] Add a temporary counter/metric (`auth.dev_headers.requests`) in existing code path (if still active) for a short measurement window.
[ ] Document inventory snapshot in this plan (append section) for audit trail.

#### Story 1: Test Token Helper Consolidation

[ ] Introduce / confirm presence of `TestTokenIssuer` (internal) exposing: `IssueNeutralAsync(userSeedSpec)`, `IssueTenantAsync(userSeedSpec, tenantSlug)`.
[ ] Provide `AuthTestClient` facade used by tests (wraps creation + Authorization header injection).
[ ] Add unit tests for `AuthTestClient` (neutral issuance, tenant issuance, rotation reuse denial scenario if needed).
[ ] Update `WebAppFactory` to make password hashing available for seeded users (if not present) OR bypass via direct service token issuance.
[ ] Document helper usage guideline in plan and `README` (test section).

#### Story 2: Migrate Integration Tests (Batch Refactor)

[ ] Phase A: Replace dev headers in core auth test suite (login, refresh, logout, tenant selection) with JWT helpers.
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

### Acceptance Summary (Sprint Exit Criteria)

[ ] No code / tests reference dev headers.
[ ] All authentication in local + CI uses JWT flows.
[ ] Removal & rationale documented (architecture + story log + upgrade guide).
[ ] Regression guard test in place verifying 401 on dev header usage.
[ ] Rollback path (tag + optional reintroduce commit link) documented.

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
