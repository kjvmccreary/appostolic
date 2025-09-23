# Security Hardening Sprint Plan (Stories 3–9)

Generated: 2025-09-23
Source Extraction: See `triSprintPlan.md` (Stories 3–9 removed and centralized here)
Baseline Tag Before Sprint: `jwt-auth-post-retire` (to be created immediately before merge of Story 3)

## Sprint Objective

Deliver a cohesive set of security & operability enhancements that strengthen abuse protection, rotation safety, traceability, incident response, and user session control while sharing a unified configuration & metrics taxonomy for long‑term maintainability.

## In-Scope Stories

Progress: 1 / 7 stories complete, 1 in progress.

1. Story 3: Refresh Rate Limiting & Abuse Protection ✅ DONE (2025-09-23)
2. Story 4: Dual-Key Signing Grace Window 🚧 IN PROGRESS
3. Story 5: Tracing Span Enrichment (auth.\* attributes)
4. Story 6: Structured Security Event Log
5. Story 7: Grafana Dashboards & Alert Rules as Code
6. Story 8: Session Enumeration Backend
7. Story 9: Admin Forced Logout & Bulk Tenant Invalidate

## Unified Configuration Surface (Proposed)

| Purpose                                     | Name                                      | Type           | Default (Dev)  | Default (Prod) | Notes                              |
| ------------------------------------------- | ----------------------------------------- | -------------- | -------------- | -------------- | ---------------------------------- |
| Refresh limiter window                      | `AUTH__REFRESH_RATE_LIMIT_WINDOW_SECONDS` | int            | 60             | 60             | Sliding window length              |
| Refresh limiter max ops                     | `AUTH__REFRESH_RATE_LIMIT_MAX`            | int            | 30             | 20             | Revisit after live metrics         |
| Rate limiter dry-run (no block)             | `AUTH__REFRESH_RATE_LIMIT_DRY_RUN`        | bool           | true           | false          | Allows initial staging observation |
| Signing keys (ordered)                      | `AUTH__JWT__SIGNING_KEYS`                 | string (comma) | single dev key | rotated set    | First = active signer              |
| Security event log enabled                  | `AUTH__SECURITY_EVENTS__ENABLED`          | bool           | false          | true           | Avoid local noise                  |
| Security event log min level                | `AUTH__SECURITY_EVENTS__LEVEL`            | enum           | Information    | Information    | Could raise in high-volume         |
| Session enumeration enabled                 | `AUTH__SESSIONS__ENUMERATION_ENABLED`     | bool           | true           | true           | Feature flag for rollout           |
| Forced logout enabled                       | `AUTH__FORCED_LOGOUT__ENABLED`            | bool           | true           | true           | Allows quick disable if regression |
| Token session fingerprint header (optional) | `AUTH__SESSIONS__FINGERPRINT_HEADER`      | string         | `X-Session-Fp` | `X-Session-Fp` | Client-provided stable device hint |
| Security dashboard provisioning gate        | `AUTH__DASHBOARDS__APPLY_ENABLED`         | bool           | false          | true           | CI or ops task gate                |

## Metrics Taxonomy (Prefix: `auth.`)

| Metric                                      | Type      | Labels                              | Emitted By | Description                       |
| ------------------------------------------- | --------- | ----------------------------------- | ---------- | --------------------------------- | ------------------------- |
| `auth.refresh.rate_limited`                 | counter   | reason (window), user_present (y/n) | Story 3    | Count of blocked refresh attempts |
| `auth.refresh.limiter.evaluation_ms`        | histogram | outcome (hit/miss/block)            | Story 3    | Latency impact of limiter         |
| `auth.jwt.key_rotation.tokens_signed`       | counter   | key_id                              | Story 4    | Tokens signed per active key id   |
| `auth.jwt.key_rotation.validation_failure`  | counter   | phase                               | Story 4    | Failures during multi-key verify  |
| `auth.trace.enriched_spans`                 | counter   | span_kind                           | Story 5    | Number of auth spans enriched     |
| `auth.security.events_emitted`              | counter   | type                                | Story 6    | Security events produced          |
| `auth.session.enumeration.requests`         | counter   | outcome                             | Story 8    | Session list API usage            |
| `auth.session.revoke.requests`              | counter   | outcome                             | Story 8    | Per-session revoke attempts       |
| `auth.admin.forced_logout.requests`         | counter   | scope (user                         | tenant)    | Story 9                           | Forced logout invocations |
| `auth.admin.forced_logout.sessions_revoked` | counter   | scope                               | Story 9    | Count of sessions invalidated     |

All metrics should include `service` resource attribute and rely on existing OTLP exporters.

## Structured Security Events Schema (Story 6)

Versioned JSON line: `{ "v": 1, "ts": ISO8601, "type": <event_type>, "user_id?": <guid>, "tenant_id?": <guid>, "ip?": <string>, "refresh_id?": <guid>, "reason?": <string>, "meta?": { ... } }`

Event Types (initial): `login_failure`, `refresh_reuse`, `refresh_expired`, `refresh_rate_limited`, `logout_all_user`, `logout_all_tenant`, `session_revoked_single`.

Rules:

- No emails or plaintext tokens.
- `reason` constrained to controlled vocabulary (documented enum).
- Schema stability guaranteed within major version (v=1) — additive only.

## Rollout & Sequencing Strategy

Recommended order balances dependency & risk:

1. Story 4 first (Dual-Key) OR Story 3 first? Decision: Start with Story 4 to de-risk rotation early; minimal surface area. Tag `hardening-pre-limiter`.
2. Story 3 (Rate Limiting) initially in dry-run mode (no blocks) + dashboard panel.
3. Story 5 (Tracing Enrichment) — low risk instrumentation.
4. Story 6 (Security Events) — enables downstream alert rules.
5. Story 7 (Dashboards & Alerts) — codify panels now that metrics/events exist.
6. Story 8 (Session Enumeration) — schema + API (impacts persistence; earlier than Story 9 to allow reuse).
7. Story 9 (Forced Logout) — leverages enumeration model + events.

After Story 3 validated (dry-run), flip `AUTH__REFRESH_RATE_LIMIT_DRY_RUN=false` in staging, observe, then production.

## Risk Matrix

| Risk                                              | Stories | Mitigation                                            | Rollback                                     |
| ------------------------------------------------- | ------- | ----------------------------------------------------- | -------------------------------------------- |
| Key rotation mis-validation causing 401 spike     | 4       | Add multi-key health probe & integration test harness | Revert config to prior single key string     |
| Over-aggressive rate limits causing user friction | 3       | Start in dry-run; generous defaults; metrics review   | Set DRY_RUN=true or raise MAX                |
| PII leakage in events                             | 6       | Explicit schema gate & unit test snapshot             | Disable `AUTH__SECURITY_EVENTS__ENABLED`     |
| Dashboard drift vs metrics names                  | 7       | Declare metrics first & lock naming                   | Adjust provisioning script & re-import       |
| Session enumeration performance regression        | 8       | Add index on `user_id` + `revoked_at`                 | Disable feature flag                         |
| Forced logout revokes incomplete set              | 9       | Integration test covers chain & tokenversion          | Disable feature flag, manual incident script |

## Testing Strategy Overview

| Story | Test Types                                                                           |
| ----- | ------------------------------------------------------------------------------------ |
| 3     | Unit limiter algorithm, integration refresh flood, boundary window reset             |
| 4     | Unit key parsing, integration sign/verify pre & post rotation, health endpoint probe |
| 5     | Span listener asserts attributes, negative test ensures no PII                       |
| 6     | Event emission snapshot, schema validation helper, disable flag test                 |
| 7     | JSON dashboard lint, rule file parse, optional smoke metric mapping                  |
| 8     | Migration test, list & revoke integration, ownership enforcement                     |
| 9     | User & tenant forced logout integration, idempotency, metrics increments             |

Add helper utilities under `apps/api.tests/` as needed (`AuthTestHelpers/` folder) to share fixture code.

## Story Details

### Story 3: Refresh Rate Limiting & Abuse Protection ✅ DONE (2025-09-23)

Goal: Mitigate brute-force / token reuse storm attack surface.
Acceptance:

- Sliding window or token bucket limiter keyed by user_id + IP (configurable thresholds).
- Config: `AUTH__REFRESH_RATE_LIMIT_WINDOW_SECONDS`, `AUTH__REFRESH_RATE_LIMIT_MAX`, `AUTH__REFRESH_RATE_LIMIT_DRY_RUN`.
- On exceed (non-dry-run): 429 JSON `{ code: "refresh_rate_limited" }`; increments metric & emits security event (type `refresh_rate_limited`).
- Dry-run path still records evaluation metrics & event with `meta.dry_run=true`.
- Tests: under threshold OK; boundary case; exceed; per-user isolation; window reset; dry-run no 429.
- Docs: tuning guidance & ops playbook.
  Success Metrics: Rate limit block ratio <0.1% normal traffic; evaluation latency p95 <1ms.

Implementation Note (Phase 1): Limiter will be in-memory per application instance (thread-safe structure) which is sufficient while a single API instance handles the majority of auth traffic. A follow-up (not in this sprint) may introduce a distributed backend (Redis) if horizontal scaling introduces bypass potential.

Final Implementation Summary:

- Configuration surface implemented: `AUTH__REFRESH_RATE_LIMIT_WINDOW_SECONDS`, `AUTH__REFRESH_RATE_LIMIT_MAX`, `AUTH__REFRESH_RATE_LIMIT_DRY_RUN`.
- In-memory sliding window limiter (`InMemoryRefreshRateLimiter`) keyed by `userId+ip` (falls back to ip-only) registered singleton.
- Endpoint refined to a single limiter evaluation (after token lookup for valid refresh; ip-only on invalid) simplifying attempt semantics.
- Returns structured 429 JSON `{ code: "refresh_rate_limited", retryAfterSeconds }` when not in dry-run.
- Added histogram metric `auth.refresh.limiter.evaluation_ms{outcome=hit|block|dryrun_block}` and latency recorded for each evaluation.
- Security event (schema v1) emitted for both real block and dry-run would-block cases with `meta.dry_run=true` in dry-run.
- Counters wired: `auth.refresh.rate_limited`, `auth.refresh.failure` (reason=refresh_rate_limited) maintained.
- Unit tests: limiter algorithm (boundary, exceed, reset, dry-run, isolation) added and passing.
- Integration tests: blocking, dry-run no-block, per-user isolation all passing under single evaluation logic.
- Documentation & Snapshot updates pending (next tasks) to reflect single-evaluation design; tuning guidance to be added with docs pass.

Operational Notes:

- Single evaluation reduces confusion vs earlier double increment design and aligns metrics with intuitive per-request counting.
- Dry-run emission allows early observability without user impact; Grafana dashboard can distinguish `block` vs `dryrun_block` outcomes.

Distributed (Redis) Upgrade Considerations (Deferred):

- Use Redis fixed-window or sliding-window Lua script; include key structure: `rl:refresh:{userId}:{ip}`.
- Add jitter to expiry to avoid thundering window resets.
- Include `AUTH__REFRESH_RATE_LIMIT_MODE=memory|redis` for pluggability.
- Metrics parity: same counters; add label `backend` (memory|redis) if introduced.
- Migration path: deploy in shadow mode reading Redis config but not enforcing until metrics confirm alignment (<2% variance vs memory sampler in staging).

### Story 4: Dual-Key Signing Grace Window

Status: 🚧 IN PROGRESS (2025-09-23)

Implementation Progress

- [x] Multi-key configuration parsing (`AUTH__JWT__SIGNING_KEYS`) with backward compatibility
- [x] Active signer = first key; all keys verify (IssuerSigningKeyResolver)
- [x] `kid` header assigned (first 8 bytes hex) for observability
- [x] Rotation integration tests (A -> A,B -> B) passing
- [x] Health verification method `VerifyAllSigningKeys()` implemented (internal call)
- [ ] Metrics: `auth.jwt.key_rotation.tokens_signed{key_id}`
- [ ] Metrics: `auth.jwt.key_rotation.validation_failure{phase}`
- [ ] Internal health endpoint `/internal/health/jwt-keys` (optional but planned)
- [ ] Story log entry (final) + SnapshotArchitecture update (metrics + endpoint)
- [ ] Upgrade/rotation runbook documentation (overlap procedure & rollback)

Next Steps

1. Wire signing + validation metrics emission inside `JwtTokenService` & key verification path.
2. Add minimal internal health endpoint returning per-key verification status & active key id.
3. Add tests covering metric counters (signing increments, simulated failure increments validation_failure).
4. Update `SnapshotArchitecture.md` and append storyLog entry marking completion.
5. Draft rotation runbook section (baseline overlap timeline, rollback path, monitoring signals) and link from plan.

Exit Criteria (to mark ✅ DONE)

- Metrics appear in local OTLP export with expected labels.
- Health endpoint returns 200 with all keys valid (staging test) and fails gracefully if a key corrupt.
- Story log & architecture snapshot updated; plan checklist items above all checked.

Notes

- We intentionally deferred metrics until limiter (Story 3) stabilized to avoid overlapping instrumentation churn.
- Validation failures should be rare; consider emitting structured security event only if repeated (>1 per interval) to avoid noise (future enhancement, not in scope here).

Goal: Allow seamless signing key rotation.
Acceptance:

- Support ordered keys via `AUTH__JWT__SIGNING_KEYS` (comma list). First signs; all verify.
- Fallback to legacy single key var if multi absent (deprecation warning logged once).
- Health probe endpoint (internal) issues token with current key and verifies all keys round‑trip.
- Rotation integration test: (A) -> (A,B) -> (B) with no invalidation of A tokens during overlap.
- Metrics: `auth.jwt.key_rotation.tokens_signed{key_id}` increments; failures logged & counted.
  Success Metrics: Rotation test passes; zero unexpected 401s in simulated overlap.

Implementation Status (2025-09-23):

- Multi-key config parsing (`AUTH__JWT__SIGNING_KEYS`) implemented with backward compatible single-key support.
- Active key = first; all keys verify via `IssuerSigningKeyResolver`.
- `kid` header added (first 8-byte hex slice) for observability.
- Rotation test added (`DualKeySigningTests`) covering A → A,B → B (legacy token invalid once key removed) and health validation method.
- Health probe method `VerifyAllSigningKeys()` available (no public endpoint yet).

Remaining (to fully close story):

- Emit metrics (`auth.jwt.key_rotation.tokens_signed`, `auth.jwt.key_rotation.validation_failure`).
- Optional internal health endpoint (`/internal/health/jwt-keys`).
- Story log & architecture snapshot update (pending after metrics wiring).
- Docs: rotation procedure (overlap window, cutover checklist, rollback steps).

### Story 5: Tracing Span Enrichment (auth.\* Attributes)

Goal: Add fine-grained auth context to traces.
Acceptance:

- Enrich login/refresh/logout spans with attributes: `auth.user_id`, `auth.outcome`, `auth.reason?`, `auth.tenant_id?`.
- No emails or PII values.
- Unit test using ActivityListener asserts presence & absence (no email).
- Metric counts enriched spans.
  Success Metrics: Attributes visible & consistent across envs; test snapshot stable.

### Story 6: Structured Security Event Log

Goal: Emit machine‑consumable JSON security events.
Acceptance:

- Logger category `Security.Auth` produces schema v1 events.
- Events for: login_failure, refresh_reuse, refresh_expired, refresh_rate_limited, logout_all_user, logout_all_tenant, session_revoked_single.
- Config flag enable/disable.
- Tests: snapshot events; invalid field injection prevented.
  Success Metrics: Events consumed by sample parser script; no PII.

### Story 7: Grafana Dashboards & Alert Rules as Code

Goal: Codify and version control key auth security panels & alerts.
Acceptance:

- Add JSON dashboards referencing metrics defined above.
- Add Prometheus alert & recording rules in `infra/observability/alerts/auth.rules.yaml`.
- Lint script or CI check verifies parse.
  Success Metrics: Manual import yields no missing metric errors.

### Story 8: Session Enumeration Backend

Goal: Provide API to list active sessions (refresh chains) per user.
Acceptance:

- Migration adds columns: `fingerprint` (nullable), `last_used_at`.
- `GET /api/auth/sessions` returns sanitized list (id, createdAt, lastUsedAt, expiresAt, revoked, tenantId?).
- `POST /api/auth/sessions/{id}/revoke` revokes single session chain (idempotent).
- Tests: ownership; revoke effect; pagination not required initial.
  Success Metrics: Typical response <50ms; correct filtering; metrics increment.

### Story 9: Admin Forced Logout & Bulk Tenant Invalidate

Goal: Provide incident containment tool.
Acceptance:

- Endpoint (admin) `POST /api/admin/users/{id}/logout-all` -> bump TokenVersion + revoke refreshes.
- Endpoint (tenant admin) `POST /api/admin/tenants/{id}/logout-all` -> revoke all tenant refresh tokens; optionally bump.
- Tests: user scope, tenant scope, unauthorized, idempotent repeat, metrics.
  Success Metrics: All integration tests pass; events emitted for each forced logout.

## Documentation & Artifacts To Update Per Story

- `SnapshotArchitecture.md` (architecture delta & config additions)
- `devInfo/storyLog.md` (story summary on completion)
- `devInfo/LivingChecklist.md` (tick readiness items; add new ones if needed)
- Runbook sections (Security / Incident Response / Observability) updated incrementally

## Rollback Strategy

Per-story feature flags + ability to revert config to previous keys or dry-run mode. Baseline tag before sprint start; optional mid-sprint tag after Stories 3–6 if needing partial release.

## Done Definition for Sprint

- All seven stories merged & feature flags defaulted to intended production state.
- Dashboards display live data for new metrics.
- Rotation simulation documented & reproducible.
- Incident response: forced logout procedure documented & tested.
- No open TODO comments referencing temporary instrumentation.

## Open Questions / Decisions To Confirm Early

| Topic                                            | Decision Needed By | Notes                                            |
| ------------------------------------------------ | ------------------ | ------------------------------------------------ |
| Limiter algorithm (sliding vs token bucket)      | Start of Story 3   | Choose based on complexity vs precision          |
| Key ID encoding (kid header vs first bytes hash) | Story 4 dev        | For metrics clarity; propose KID header          |
| Session fingerprint source                       | Story 8 dev        | Accept client header only vs server-derived hash |
| Dashboards deployment path                       | Story 7 dev        | Manual import vs IaC apply script                |

## Next Action

Approve plan; create branch `sprint/security-hardening` to begin Story 4 implementation (dual-key) unless preference to start with rate limiting.

---

_End of bdlSprintPlan.md_
