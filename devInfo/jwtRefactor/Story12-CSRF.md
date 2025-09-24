# Story 12: CSRF Strategy & SameSite=None Readiness

Status: IN PROGRESS (kickoff 2025-09-23)
Owner: Auth Hardening Stream

## 1. Goal

Provide an opt-in CSRF mitigation layer for state-changing auth endpoints (login, refresh, select-tenant, logout) to support future scenarios where SameSite=Lax may be insufficient (cross-site embedding, multi-domain deployments, or if moving refresh to SameSite=None for specialized UX flows).

## 2. Scope

In-scope endpoints (currently cookie-dependent auth mutations):

- POST /api/auth/login
- POST /api/auth/refresh
- POST /api/auth/select-tenant
- POST /api/auth/logout
- POST /api/auth/logout/all

Out-of-scope (idempotent or bearer-only GETs) unless future forms are introduced.

## 3. Selected Approach: Double-Submit Cookie (Stateless)

We generate a random base64url token (16 bytes entropy) and set a non-HttpOnly cookie (default name `csrf`). Clients must mirror its value in a custom header (`X-CSRF`). Server validates equality using timing-safe comparison. Implementation is deliberately stateless (no server store / binding) for low overhead.

### Rationale

- Stateless: no DB/cache writes per request; horizontally scalable.
- Simple client logic: read cookie → set header.
- Adequate for preventing cross-site form POST & automatic credentialed fetches since attacker cannot read cookie to supply header.
- Allows future binding (HMAC(userId|nonce|secret)) if replay reduction needed without redesign.

### Alternatives Considered

| Approach                           | Pros                               | Cons                                            | Decision                      |
| ---------------------------------- | ---------------------------------- | ----------------------------------------------- | ----------------------------- |
| Rely on SameSite=Lax only          | Zero extra logic                   | Fails if we move to None (iframe, cross-domain) | Rejected (future flexibility) |
| Synchronizer (server-stored) token | Strong replay tracking             | Server storage & invalidation complexity        | Overkill now                  |
| Signed timestamped token (HMAC)    | Replay window limiting w/o storage | Slightly more client complexity                 | Possible phase 2              |
| Per-request nonce (rotating)       | Strongest replay defense           | Requires session binding & state                | Overkill for current threat   |

## 4. Threat Model (Simplified)

| Threat                                         | Mitigated By                                               | Notes                                                   |
| ---------------------------------------------- | ---------------------------------------------------------- | ------------------------------------------------------- |
| Cross-site form POST (login/refresh)           | Header requirement (not set by form)                       | Attacker cannot read cookie value                       |
| Cross-site img/script tag GET                  | Not state-changing                                         | N/A                                                     |
| Malicious embedded iframe auto-submitting form | Missing header                                             | Blocked                                                 |
| Replay of captured token via XSS               | Not mitigated by CSRF layer                                | Requires separate XSS defenses                          |
| Subdomain cookie fixation                      | Random token regenerated if absent; not security sensitive | Consider prefix isolation if multi-subdomain deployment |

## 5. Configuration

Env Vars:

- AUTH**CSRF**ENABLED (bool, default false)
- AUTH**CSRF**COOKIE_NAME (default csrf)
- AUTH**CSRF**HEADER_NAME (default X-CSRF)
- AUTH**CSRF**AUTO_ISSUE (bool, default true) – auto-issue on login success & GET /auth/csrf
- AUTH**CSRF**COOKIE_TTL_MINUTES (default 120; <=0 session cookie)

## 6. Implementation Summary (Current)

- `CsrfOptions`, `ICsrfService`, `CsrfService` added.
- GET /api/auth/csrf endpoint issues token & cookie when enabled (404 when disabled to hide surface).
- Validation inserted (guard clause) at start of each protected endpoint; on failure returns 400 `{ code: <csrf_error> }` where `<csrf_error>` ∈ {csrf_missing_cookie, csrf_missing_header, csrf_mismatch}.
- Auto-issue on login success if enabled & AutoIssue=true (convenience for SPA initial load).

## 7. Tests (Added in `CsrfTests`)

Scenarios covered:

1. Disabled allows login without header.
2. Enabled missing cookie -> 400 csrf_missing_cookie.
3. Enabled missing header -> 400 csrf_missing_header.
4. Enabled mismatch -> 400 csrf_mismatch.
5. Enabled success path login + refresh.
6. GET /auth/csrf issues token & cookie.

Pending (optional):

- Refresh denial when header omitted (redundant with login test but adds breadth).
- Logout path CSRF negative test.

## 8. Metrics (Planned / Optional)

Potential counters:

- auth.csrf.failures{reason}
- auth.csrf.validations{outcome=success}

Decision: Defer until operational need demonstrated (low cardinality reasons already visible via structured logs if we add them). If added, integrate in `CsrfService.Validate`.

## 9. Rollout Plan

1. Land implementation + tests with flag disabled by default.
2. Enable in staging with instrumentation (consider temporary debug logging of failures). Monitor for false positives.
3. Enable in production for auth endpoints after 0 false positive window (e.g., 48h) confirmed.
4. If future SameSite=None migration for refresh cookie occurs, this becomes mandatory.

Rollback: Set AUTH**CSRF**ENABLED=false (validation short-circuits true).

## 10. Future Enhancements

- Optional HMAC binding: token = base64url(userId|nonce|ts|HMAC(secret, userId|nonce|ts)) with rolling window validation.
- Per-path enforcement customization (if some endpoints exempted for performance).
- Add CSP & Secure cookie alignment doc references.
- Browser matrix validation (Story 15) verifying header inclusion across Safari/Firefox/Chromium.

## 11. Completion Criteria

- Decision doc (this) merged.
- SnapshotArchitecture updated (Security Features bullet concise) once final.
- Story log entry appended with summary.
- triSprintPlan Story 12 marked ✅ DONE.

## 12. Open Questions

- Do we need to guard magic link consumption flows? (Not currently in scope; may add if they acquire state-changing side-effects.)
- Should we emit structured security events for CSRF failures? (Probably only if trend appears; low signal expected initially.)

---

Appendendum: No user identifiers stored in CSRF token; treat as low sensitivity. Rotation occurs only if absent (persist between tabs). Clients may proactively call GET /api/auth/csrf on app bootstrap.
