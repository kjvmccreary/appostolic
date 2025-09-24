# Auth Follow-Up Triage & Next Sprint Backlog (Post JWT Story 11)

Generated: 2025-09-21
Baseline Tag: `jwt-auth-rollout-complete`
Scope: Optional / deferred / follow-up items extracted from `jwtSprintPlan.md` and related story notes. Focused on security hardening, operability, deprecation closure, and session management foundations.

## Methodology

Each follow-up was:

1. Extracted & deduplicated
2. Categorized (Security, Observability, Performance, DX, Product, Infra, Cleanup)
3. Assigned Impact (H/M/L), Effort (XS/S/M/L), and a rough Priority Order (lower = sooner)
4. Promoted to a structured Story if Priority ≤ 16 (initial sprint consideration)

Impact Heuristic:

- H: Security posture / data protection / production incident prevention
- M: Operational visibility / performance / reliability
- L: Developer ergonomics / secondary UX polish

Effort Bands (engineering days): XS < 0.5, S ≤ 1, M 2–3, L 4–6, XL > 6 (none scheduled this sprint)

---

## Triage Table

| Follow-Up                                                    | Category      | Impact | Effort | Priority | Notes                                                                                                           |
| ------------------------------------------------------------ | ------------- | ------ | ------ | -------- | --------------------------------------------------------------------------------------------------------------- |
| Dev header decommission & regression guard                   | Security      | H      | M      | 1        | Removes legacy path; must retain rollback note — ✅ DONE 2025-09-23                                             |
| Plaintext refresh flag retirement & TEMP metric plan         | Security      | H      | S      | 2        | Flip defaults, observe, schedule deletion — ✅ DONE 2025-09-23                                                  |
| Refresh rate limiting (middleware + config + alerts)         | Security      | H      | M      | 3        | Prevent brute-force/abuse; reuse spike synergy — 🔒 Bundled: Security Hardening Sprint — ✅ DONE 2025-09-23     |
| Dual-key signing grace window (key rotation)                 | Security      | H      | M      | 4        | Enables zero-downtime signing key rotation — 🔒 Bundled: Security Hardening Sprint — ✅ DONE 2025-09-23         |
| Tracing span enrichment (auth.\* attrs)                      | Observability | M      | S      | 5        | Enables per-event correlation & latency root cause — 🔒 Bundled: Security Hardening Sprint — ✅ DONE 2025-09-23 |
| Structured security event log (SIEM feed)                    | Security      | H      | S      | 6        | Minimal JSON lines export + docs — 🔒 Bundled: Security Hardening Sprint — ✅ DONE 2025-09-23                   |
| Grafana dashboards & alert rules as code                     | Observability | M      | S      | 7        | Implements previously documented panels/alerts — 🔒 Bundled: Security Hardening Sprint — ✅ DONE 2025-09-23     |
| Session enumeration backend (fingerprint + list)             | Security      | H      | M      | 8        | Foundation for session UI + investigations — 🔒 Bundled: Security Hardening Sprint — ✅ DONE 2025-09-23         |
| Admin forced logout & bulk tenant invalidate                 | Security      | H      | S      | 9        | Incident containment (compromised tenant/user) — 🔒 Bundled: Security Hardening Sprint — ✅ DONE 2025-09-23     |
| TokenVersion cache + validation latency metric               | Performance   | M      | M      | 10       | Reduce DB reads; add metric to observe gain — ✅ DONE 2025-09-23                                                |
| Sliding refresh expiration + absolute lifetime cap           | Security      | M      | M      | 11       | Limits long-lived dormant tokens; UX smoothing — ✅ DONE 2025-09-23                                             |
| CSRF strategy & SameSite=None readiness design               | Security      | H      | M      | 12       | Pre-req if cross-site embedding emerges                                                                         |
| Remove JSON body refresh path & dead code                    | Cleanup       | M      | S      | 13       | After grace disabled & adoption confirmed                                                                       |
| Emergency JWT rollback kill-switch flag                      | Security      | M      | XS     | 14       | Lightweight, improves rollback posture                                                                          |
| Playwright browser security validation (HttpOnly + SameSite) | Security      | M      | M      | 15       | Validates real browser constraints beyond server tests                                                          |
| Security reuse anomaly alert tuning                          | Observability | M      | XS     | 16       | Threshold fine-tune + dashboard annotation                                                                      |
| Session management UI (list & revoke)                        | Product       | M      | L      | 17       | Requires session enumeration backend                                                                            |
| Device display name capture (frontend + store)               | Product       | L      | S      | 18       | Builds on fingerprinting                                                                                        |
| Roles label array in neutral token                           | DX            | L      | XS     | 19       | Improves developer clarity                                                                                      |
| Expand TestAuthClient (expired token generation)             | DX            | L      | S      | 20       | Facilitates edge-case tests                                                                                     |
| Optional SSR access cookie strategy evaluation               | Product       | L      | M      | 21       | Only if SSR auth friction encountered                                                                           |
| nginx reference / security headers sample                    | Infra         | L      | S      | 22       | Optional if ingress lacks parity                                                                                |
| Caddy alternative config                                     | Infra         | L      | S      | 23       | Simpler local TLS option                                                                                        |
| Remove transitional flags & dead code sweep                  | Cleanup       | M      | S      | 24       | Post-retirement consolidation                                                                                   |
| Automated key rotation simulation harness                    | Security      | M      | S      | 25       | Tests dual-key correctness & rollback                                                                           |
| Dashboard provisioning automation (CI apply)                 | Observability | L      | S      | 26       | Declarative dashboards drift guard                                                                              |
| Token validation observability enhancements (post-cache)     | Observability | M      | XS/S   | 27       | Story 10 delivered base latency + hit/miss; add derived ratio + optional eviction metric                        |
| Session list pagination & indexing                           | Performance   | M      | S      | 28       | Scale follow-up                                                                                                 |
| Derived success ratio metrics publication                    | Observability | L      | XS     | 29       | Export pre-computed gauges                                                                                      |
| Replay / IP pattern correlation enhancements                 | Security      | M      | M      | 30       | Phase 2 after base alert stable                                                                                 |
| Plaintext counters final deletion (post quiet)               | Cleanup       | M      | XS     | 31       | Second-phase after Story 2 quiet window                                                                         |

---

## Prioritized Sprint Candidate Stories (1–16)

### Story 1: Dev Header Decommission & Regression Guard — ✅ DONE (2025-09-23)

Goal: Fully remove dev header authentication (handler, composite wiring, flag) with a safe rollback toggle.
Acceptance:

- Remove DevHeaderAuthHandler & composite scheme; retain pure Bearer.
- Add feature flag `AUTH__JWT_EMERGENCY_REVERT` (default false) that, when true, re-registers legacy handler (documented as temporary).
- Update tests: migrate any relying on dev headers to real JWT flows/TestAuthClient.
- Add regression test asserting dev headers rejected when revert flag false.
- Docs: upgrade guide + SnapshotArchitecture + LivingChecklist + storyLog updated; triageOptions references closed.
- Rollback documented (set emergency flag true + redeploy).
  Success Metrics: All auth tests green; no reliance on dev headers in CI.

### Story 2: Plaintext Refresh Flag Retirement (Phase-Out) — ✅ DONE (2025-09-23)

Goal: Enforce cookie-only refresh and retire plaintext emission path.
Acceptance:

- Set default `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT=false` and (if still enabled) `AUTH__REFRESH_JSON_GRACE_ENABLED=false` in non-dev.
- Remove plaintext field emission logic; keep instrumentation counting suppression until metrics show zero emission for 2 releases.
- Add integration test: ensures `refresh.token` absent.
- Add dashboard note: plaintext panel expected flatline.
- Docs updated + schedule final deletion story.
  Success Metrics: No emitted plaintext events; suppression counter increments only (optional) for final window.

### Stories 3–9: Security Hardening Sprint (Extracted) ✅ DONE

The detailed plan for the bundled Security Hardening Sprint (Stories 3–9) has been moved to `bdlSprintPlan.md` to reduce duplication and centralize shared configuration, metrics taxonomy, and rollout strategy.

Reference: See `devInfo/jwtRefactor/bdlSprintPlan.md` for full goals, acceptance criteria, metrics, risk matrix, and rollout/rollback procedures for:

- Story 3: Refresh Rate Limiting & Abuse Protection
- Story 4: Dual-Key Signing Grace Window
- Story 5: Tracing Span Enrichment
- Story 6: Structured Security Event Log
- Story 7: Grafana Dashboards & Alert Rules as Code
- Story 8: Session Enumeration Backend
- Story 9: Admin Forced Logout & Bulk Tenant Invalidate

Rationale: These stories share overlapping instrumentation, configuration flags, and coordinated release value; centralizing avoids drift.

### Story 10: TokenVersion Cache & Validation Latency Metric — ✅ DONE (2025-09-23)

Goal: Reduce DB load on high-volume auth traffic by short‑circuiting repeated TokenVersion lookups during JWT bearer validation while preserving revocation correctness.

Implementation Summary:

- Introduced `ITokenVersionCache` + `InMemoryTokenVersionCache` (TTL default 30s) with config: `AUTH__TOKEN_VERSION_CACHE_ENABLED`, `AUTH__TOKEN_VERSION_CACHE_TTL_SECONDS`.
- Wired cache into `OnTokenValidated` in `Program.cs`: attempt cache read → on miss query EF → populate cache → record latency.
- Added metrics:
  - `auth.token_version.cache_hit` / `auth.token_version.cache_miss`
  - `auth.token_validation.latency_ms` (histogram)
- Added tests (`TokenVersionCacheTests`):
  - Warm path (second request serves from cache)
  - Invalidation path (simulate TokenVersion bump → invalidate → next request 401 `token_version_mismatch`).
- Safe rollback: disable via `AUTH__TOKEN_VERSION_CACHE_ENABLED=false`.

Acceptance Mapping:

- In-memory cache with TTL & explicit invalidate ✅
- Metrics (hit, miss, latency histogram) ✅
- Config toggles ✅
- Tests for hit/miss + invalidation ✅ (expiry & disable toggle can be covered later if needed)
- Revocation correctness preserved (401 on stale token after version bump) ✅

Deferred / Follow-Up:

- Story 27 (Validation latency & cache hit histogram) partially satisfied (core metrics emitted). Remaining possible scope: derived hit ratio gauge or bucketed latency by outcome (optional). Consider collapsing Story 27 or refining its scope.

Success Metrics (Initial Baseline Targets):

- Hit ratio improves over first few authenticated requests for active users (observable via counters).
- No increase in false positive `token_version_mismatch` errors post‑deployment.

### Story 11: Sliding Refresh Expiration + Absolute Cap — ✅ DONE (2025-09-23)

Goal: Balance UX (active users stay signed in) with security.
Acceptance (Implemented):

- Config: `AUTH__REFRESH_SLIDING_WINDOW_DAYS`, `AUTH__REFRESH_MAX_LIFETIME_DAYS` ✅
- Sliding extension logic applied in `/auth/refresh` and `/auth/select-tenant` rotations ✅
- Absolute lifetime enforced via new `original_created_at` column + migration; denial returns 401 `refresh_max_lifetime_exceeded` ✅
- Metric: `auth.refresh.max_lifetime_exceeded` emitted on denial ✅
- Tests: sliding extension, clamped extension, and exceeded lifetime denial (`SlidingRefreshTests`) ✅
- Rollback: set both env vars to 0 (disables sliding & cap) ✅
  Success Metrics: Expiries extend only within window; no rotations beyond absolute max; denial metric low & expected.

### Story 12: CSRF Strategy & SameSite=None Readiness — 🚧 IN PROGRESS (2025-09-23 kickoff)

Goal: Prepare for potential cross-site embedding.
Acceptance:

- Design decision doc (double-submit vs signed header token) with tradeoffs.
- Prototype implementation (feature-flagged) for chosen approach (e.g., anti-CSRF nonce cookie + header).
- Tests: refresh fails without header when protection enabled; passes with valid token.
- Docs: enable sequence & threat model.
  Success Metrics: Protection toggle works; minimal false positives under same-site usage.

Progress (Kickoff + Metrics):

- Implementation scaffolding added: `CsrfOptions`, `ICsrfService`/`CsrfService`, GET `/api/auth/csrf`, validation integrated into login, select-tenant, logout, logout/all, refresh endpoints (double-submit cookie pattern).
- Feature flags/env vars: `AUTH__CSRF__ENABLED`, `AUTH__CSRF__COOKIE_NAME`, `AUTH__CSRF__HEADER_NAME`, `AUTH__CSRF__AUTO_ISSUE`, `AUTH__CSRF__COOKIE_TTL_MINUTES`.
- Tests (`CsrfTests`): disabled mode, missing cookie, missing header, mismatch, success (login+refresh), GET issuance, logout negative, logout success.
- Metrics implemented: `auth.csrf.failures{reason}` & `auth.csrf.validations`.
  Remaining:
- Architecture snapshot bullet (Security Features) — pending final wording.
- Story log entry on completion.
- Optional: dashboard panel & alert (mismatch spike) — not required for acceptance.

### Story 27 (Refined): Token Validation Observability Enhancements (Post-Cache)

Context: Core instrumentation shipped with Story 10 (`auth.token_validation.latency_ms`, `auth.token_version.cache_hit|miss`). Remaining value is in higher-level ratios & eviction visibility rather than raw latency plumbing.

Refined Scope:

- Add calculated gauge (or periodic counter->gauge translation) `auth.token_version.cache_hit_ratio` (exported via background hosted service every N seconds) OR document PromQL expression in dashboards instead (choose one: prefer docs-only for simplicity).
- Optional counter: `auth.token_version.cache_evicted` (expired entries actively removed) if we introduce active trimming logic beyond opportunistic removal.
- Dashboard panel additions: hit ratio %, P95 token_validation latency slice.
- Alert rule (optional): low hit ratio (<40% sustained 10m) indicating misconfiguration (cache disabled unintentionally) or extreme churn.

Out of Scope / Already Done:

- Base latency histogram & hit/miss counters (Story 10) ✅
- Per-request outcome tagging (not needed; low dimensionality kept intentionally) ❌ (not planned)

Acceptance:

- Either documented PromQL or emitted gauge for hit ratio.
- Dashboard updated referencing ratio.
- (If chosen) eviction counter increments on explicit trim path & test validates increment.
  Success Metrics: Operators can view real-time cache efficiency without ad-hoc queries.

### Story 13: Remove JSON Body Refresh Path & Dead Code

Goal: Finalize deprecation after adoption stats confirm.
Acceptance:

- Delete body parsing branch; remove related error codes (`refresh_body_disallowed`).
- Remove grace flag config & docs references.
- Tests updated (no body tests remain; negative ensures body rejected with 400 unknown or missing_refresh logic if still triggered).
  Success Metrics: Simplified code; tests green.

### Story 14: Emergency JWT Rollback Kill-Switch

Goal: Provide rapid fallback path if undiscovered regression emerges.
Acceptance:

- Flag `AUTH__JWT_EMERGENCY_REVERT` enabling minimal legacy fallback (documented constraints) OR disables new rate limiting/CSRF guard.
- Clear documented blast radius & limitations.
- Test ensures flag toggles code paths.
  Success Metrics: Toggled behavior verified; default off.

### Story 15: Browser Security Validation (Playwright)

Goal: Validate runtime cookie security in real browser context.
Acceptance:

- Playwright test: login → `document.cookie` does NOT expose `rt`; subsequent refresh succeeds automatically.
- (If SameSite=None scenario prototyped) cross-site iframe test that refresh blocked without CSRF token.
- CI integration optional (tagged e2e-slow).
  Success Metrics: Test passes locally; gated optional in CI.

### Story 16: Reuse Anomaly Alert Tuning

Goal: Calibrate alert noise vs signal.
Acceptance:

- Implement dynamic threshold suggestions based on baseline (e.g., 95th percentile reuse rate over 7d + margin).
- Update alert rule file with tuned static threshold; commit diff.
- Add runbook paragraph (investigation steps).
  Success Metrics: Rule deployed; runbook updated.

---

## Lower Priority & Backlog (17+)

Summarized; promote in later sprints as capacity allows.

- Session Management UI & Device Display Names (stories 17–18)
- Roles Label Array (Story 19)
- Expanded TestAuthClient (Story 20)
- SSR Access Cookie Evaluation (Story 21)
- Ingress Config Samples (Stories 22–23)
- Transitional Flag Final Deletion (Story 24 & 31 consolidation possible)
- Key Rotation Simulation Harness (Story 25)
- Dashboard Provision Automation (Story 26)
- Validation Latency & Cache Hit Histogram (Story 27 — follows Story 10)
- Session Pagination & Indexing (Story 28)
- Derived Success Ratio Metrics (Story 29)
- Replay / IP Correlation Enhancements (Story 30)
- Plaintext Counters Final Deletion (Story 31 — depends on Story 2 quiet window)

---

## Dependency Notes

### Security Hardening Sprint Bundle (Stories 3–9)

Detailed plan moved to `bdlSprintPlan.md` (central source for acceptance criteria, metrics taxonomy, rollout & risk). This triage file now tracks only ordering & dependency relationships.

Reference: `devInfo/jwtRefactor/bdlSprintPlan.md`

Summary: Stories 3–9 share configuration, metrics, and coordinated release value (abuse protection, trace enrichment, security events, dashboards, session control, forced logout). They will release together with per-story feature flags for granular rollback.

- Story 13 depends on Story 2 completion + adoption verification.
- Story 15 (cross-site branch) depends on Story 12 outcome if enabling SameSite=None.
- Story 27 depends on Story 10 (cache introduction).
- Story 24/31 scheduled only after metrics confirm stable zero plaintext emission.
- Story 9 (admin forced logout) enhances incident response synergy with Stories 3 & 6.

## Risk Mitigation Overview

- Key Rotation: Introduce dual-key BEFORE operational need; test harness (Story 25) reduces rotation risk.
- Rate Limiting False Positives: Start with generous thresholds + metric-only dry-run toggle for staging.
- Session Enumeration Privacy: Return minimal metadata; exclude IP/device until privacy review.
- CSRF Strategy: Feature-flag early; fail closed only when adoption confirmed.

## Rollback Strategy Summary

- Each story introduces explicit config flag to disable new behavior (where applicable) until validated in staging.
- Tag after completion of Stories 1–6 to anchor post-hardening baseline.

## Next Steps

1. Approve ordering & scope.
2. Create `rdhSprintPlan.md` referencing Stories 1–8 as MVP set.
3. Begin with Story 1 branch once plan merged.

---

_End of triageOptions.md_
