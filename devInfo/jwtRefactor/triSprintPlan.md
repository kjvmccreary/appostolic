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

| Follow-Up                                                    | Category      | Impact | Effort | Priority | Notes                                                                                      |
| ------------------------------------------------------------ | ------------- | ------ | ------ | -------- | ------------------------------------------------------------------------------------------ |
| Dev header decommission & regression guard                   | Security      | H      | M      | 1        | Removes legacy path; must retain rollback note — ✅ DONE 2025-09-23                        |
| Plaintext refresh flag retirement & TEMP metric plan         | Security      | H      | S      | 2        | Flip defaults, observe, schedule deletion — ✅ DONE 2025-09-23                             |
| Refresh rate limiting (middleware + config + alerts)         | Security      | H      | M      | 3        | Prevent brute-force/abuse; reuse spike synergy — 🔒 Bundled: Security Hardening Sprint     |
| Dual-key signing grace window (key rotation)                 | Security      | H      | M      | 4        | Enables zero-downtime signing key rotation — 🔒 Bundled: Security Hardening Sprint         |
| Tracing span enrichment (auth.\* attrs)                      | Observability | M      | S      | 5        | Enables per-event correlation & latency root cause — 🔒 Bundled: Security Hardening Sprint |
| Structured security event log (SIEM feed)                    | Security      | H      | S      | 6        | Minimal JSON lines export + docs — 🔒 Bundled: Security Hardening Sprint                   |
| Grafana dashboards & alert rules as code                     | Observability | M      | S      | 7        | Implements previously documented panels/alerts — 🔒 Bundled: Security Hardening Sprint     |
| Session enumeration backend (fingerprint + list)             | Security      | H      | M      | 8        | Foundation for session UI + investigations — 🔒 Bundled: Security Hardening Sprint         |
| Admin forced logout & bulk tenant invalidate                 | Security      | H      | S      | 9        | Incident containment (compromised tenant/user) — 🔒 Bundled: Security Hardening Sprint     |
| TokenVersion cache + validation latency metric               | Performance   | M      | M      | 10       | Reduce DB reads; add metric to observe gain                                                |
| Sliding refresh expiration + absolute lifetime cap           | Security      | M      | M      | 11       | Limits long-lived dormant tokens; UX smoothing                                             |
| CSRF strategy & SameSite=None readiness design               | Security      | H      | M      | 12       | Pre-req if cross-site embedding emerges                                                    |
| Remove JSON body refresh path & dead code                    | Cleanup       | M      | S      | 13       | After grace disabled & adoption confirmed                                                  |
| Emergency JWT rollback kill-switch flag                      | Security      | M      | XS     | 14       | Lightweight, improves rollback posture                                                     |
| Playwright browser security validation (HttpOnly + SameSite) | Security      | M      | M      | 15       | Validates real browser constraints beyond server tests                                     |
| Security reuse anomaly alert tuning                          | Observability | M      | XS     | 16       | Threshold fine-tune + dashboard annotation                                                 |
| Session management UI (list & revoke)                        | Product       | M      | L      | 17       | Requires session enumeration backend                                                       |
| Device display name capture (frontend + store)               | Product       | L      | S      | 18       | Builds on fingerprinting                                                                   |
| Roles label array in neutral token                           | DX            | L      | XS     | 19       | Improves developer clarity                                                                 |
| Expand TestAuthClient (expired token generation)             | DX            | L      | S      | 20       | Facilitates edge-case tests                                                                |
| Optional SSR access cookie strategy evaluation               | Product       | L      | M      | 21       | Only if SSR auth friction encountered                                                      |
| nginx reference / security headers sample                    | Infra         | L      | S      | 22       | Optional if ingress lacks parity                                                           |
| Caddy alternative config                                     | Infra         | L      | S      | 23       | Simpler local TLS option                                                                   |
| Remove transitional flags & dead code sweep                  | Cleanup       | M      | S      | 24       | Post-retirement consolidation                                                              |
| Automated key rotation simulation harness                    | Security      | M      | S      | 25       | Tests dual-key correctness & rollback                                                      |
| Dashboard provisioning automation (CI apply)                 | Observability | L      | S      | 26       | Declarative dashboards drift guard                                                         |
| Validation latency + cache hit histogram                     | Observability | M      | S      | 27       | After cache introduced                                                                     |
| Session list pagination & indexing                           | Performance   | M      | S      | 28       | Scale follow-up                                                                            |
| Derived success ratio metrics publication                    | Observability | L      | XS     | 29       | Export pre-computed gauges                                                                 |
| Replay / IP pattern correlation enhancements                 | Security      | M      | M      | 30       | Phase 2 after base alert stable                                                            |
| Plaintext counters final deletion (post quiet)               | Cleanup       | M      | XS     | 31       | Second-phase after Story 2 quiet window                                                    |

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

### Stories 3–9: Security Hardening Sprint (Extracted)

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

### Story 10: TokenVersion Cache & Validation Latency Metric

Goal: Reduce DB load on high-volume auth traffic.
Acceptance:

- Add in-memory cache (ConcurrentDictionary or MemoryCache) keyed by userId storing version + TTL (e.g., 30s).
- Fallback to DB on miss; update metric `auth.token_validation.latency_ms` (histogram) + `auth.token_version.cache_hit` counter.
- Config toggles to disable cache.
- Tests: cache hit path, miss path, expiry, disable toggle.
  Success Metrics: Hit ratio metric recorded; no stale acceptance after version bump (invalidate on bump).

### Story 11: Sliding Refresh Expiration + Absolute Cap

Goal: Balance UX (active users stay signed in) with security.
Acceptance:

- Config: `AUTH__REFRESH_SLIDING_WINDOW_DAYS`, `AUTH__REFRESH_MAX_LIFETIME_DAYS`.
- On refresh: if within sliding window and not exceeding max, extend expires_at; else rotate without extending.
- Tests: extension occurs under threshold; no extension after max; expiry enforced.
- Docs: security rationale & rollback.
  Success Metrics: Correct expiry math; no extension beyond max.

### Story 12: CSRF Strategy & SameSite=None Readiness

Goal: Prepare for potential cross-site embedding.
Acceptance:

- Design decision doc (double-submit vs signed header token) with tradeoffs.
- Prototype implementation (feature-flagged) for chosen approach (e.g., anti-CSRF nonce cookie + header).
- Tests: refresh fails without header when protection enabled; passes with valid token.
- Docs: enable sequence & threat model.
  Success Metrics: Protection toggle works; minimal false positives under same-site usage.

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
