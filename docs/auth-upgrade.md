# Auth Upgrade & Operations Guide (JWT Rollout 2025-09)

Purpose: Provide engineers and operators with a concise, actionable reference for enabling, migrating to, operating, and (if required) rolling back the JWT + refresh cookie authentication system. This complements `SnapshotArchitecture.md` (architecture focus) and the sprint plans (`devInfo/jwtRefactor/*`).

## 1. Prerequisites & Environment Variables

Minimum required variables (API):

```
Auth__Jwt__Issuer=appostolic
Auth__Jwt__Audience=appostolic
Auth__Jwt__SigningKey=<Base64 256-bit secret>
Auth__Access__Minutes=15               # (example) short-lived access
Auth__Refresh__Days=30                  # (example) refresh lifetime
Auth__PasswordPepper=<High-entropy secret>
AUTH__REFRESH_COOKIE_ENABLED=true       # Enable httpOnly cookie delivery (rt)
AUTH__REFRESH_JSON_GRACE_ENABLED=true   # Transitional body + plaintext path ON initially
AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT=false # Suppress plaintext refresh.token in JSON (recommended steady state)
# AUTH__REFRESH_DEPRECATION_DATE=Mon, 29 Sep 2025 00:00:00 GMT   # (set when announcing body path sunset)
// Dev headers path removed (RDH Story 4); flag AUTH__ALLOW_DEV_HEADERS retired.
```

Recommended staging/production add-ons:

```
OTEL_EXPORTER_OTLP_ENDPOINT=<collector>
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true   # if behind reverse proxy
```

## 2. Generating a Strong Signing Key

Generate a 256-bit (32 byte) random key and Base64 encode it:

```
openssl rand -base64 32 | tr -d '\n'
```

Store the value in secret management (e.g., 1Password / Vault) and inject via environment (`Auth__Jwt__SigningKey`). Do NOT commit test or fallback keys. Production startup fails fast if the key is missing.

### Rotation (Interim Manual Procedure)

Until multi-key validation is implemented:

1. Reduce access token lifetime temporarily (e.g., 5 min) to shrink exposure window.
2. Pick a low-traffic window.
3. Deploy with a new signing key; all existing access tokens immediately fail (expected) and clients silently refresh (if within refresh validity) or re-login.
4. Restore standard access lifetime.

If emergency compromise suspected: (a) rotate key (above); (b) optionally bulk revoke refresh tokens (`DELETE FROM app.refresh_tokens`) forcing full re-auth.

## 3. Transitional Feature Flags

| Flag                                  | Default (dev) | Purpose                                                   | Steady State        | Rollback Action                                   |
| ------------------------------------- | ------------- | --------------------------------------------------------- | ------------------- | ------------------------------------------------- |
| AUTH\_\_REFRESH_COOKIE_ENABLED        | true          | Issues httpOnly `rt` cookie                               | true                | Set false to revert to JSON-only (discouraged)    |
| AUTH\_\_REFRESH_JSON_GRACE_ENABLED    | true          | Accepts JSON `{ refreshToken }` body + may emit plaintext | false               | Set true to re-allow body path                    |
| AUTH\_\_REFRESH_JSON_EXPOSE_PLAINTEXT | false         | Emits plaintext `refresh.token` when true                 | false               | Set true temporarily if legacy client needs token |
| AUTH\_\_REFRESH_DEPRECATION_DATE      | unset         | Adds `Deprecation` + `Sunset` headers on body usage       | unset after Phase 3 | N/A (remove date)                                 |
| (removed) AUTH\_\_ALLOW_DEV_HEADERS   | (n/a)         | Legacy dev header auth (removed Story 4)                  | n/a                 | n/a (rollback via tag, not flag)                  |

Phases (Refresh Token Transport):

1. Phase 1 (Dual): cookie + body accepted; plaintext optionally emitted.
2. Phase 2 (Deprecation): set `AUTH__REFRESH_DEPRECATION_DATE`; body still accepted but responses include deprecation headers when body used.
3. Phase 3 (Cookie-Only): set `AUTH__REFRESH_JSON_GRACE_ENABLED=false`; body rejected (400 `refresh_body_disallowed`); plaintext omitted regardless of expose flag (unless explicitly re-enabled in emergency).

## 4. Rollout Strategy (Recommended)

1. Enable cookie + keep grace ON in staging. Verify silent refresh loop works (frontend auto refresh before expiry).
2. Set `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT=false` early to surface any lingering client dependencies.
3. Observe logs/metrics for plaintext emissions (`auth.refresh.plaintext_emitted`). If zero for ≥ one release cycle, proceed.
4. Announce deprecation: set `AUTH__REFRESH_DEPRECATION_DATE` (RFC1123 date) → monitors show body usage volume.
5. Flip `AUTH__REFRESH_JSON_GRACE_ENABLED=false` → enforce cookie-only.
6. After quiet period (no body attempts, no plaintext emission), schedule removal of body parsing branch & plaintext flag (Story 11 / RDH follow-up).

## 5. Rollback Playbook

Scenario: regression in refresh rotation or cookie transport.

Steps:

1. Set `AUTH__REFRESH_JSON_GRACE_ENABLED=true` and (if needed) `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT=true`.
2. (Optional – legacy) If absolutely necessary pre-removal tag rollback, checkout tag `before-dev-header-removal`; dev headers cannot be re-enabled by flag.
3. Capture metrics: `auth.refresh.failure`, `auth.refresh.reuse_denied`, logs with codes.
4. Reproduce & patch; revert flags to steady state afterwards.

Emergency Key Compromise:

1. Rotate signing key (Section 2).
2. (Optional) Purge refresh tokens (forces sign-in): `DELETE FROM app.refresh_tokens;` (irreversible) — weigh user impact.
3. Monitor for lingering reuse attempts (metric `auth.refresh.reuse_denied`).

## 6. Error Codes Reference (401/400)

| Code                    | Meaning                      | Typical Cause                | Action                                        |
| ----------------------- | ---------------------------- | ---------------------------- | --------------------------------------------- |
| token_version_mismatch  | Access token version stale   | Password change / logout-all | Trigger silent refresh; if persists, re-login |
| refresh_reuse           | Old refresh token replayed   | Token rotation reuse/replay  | Force re-login; investigate potential theft   |
| refresh_expired         | Refresh token past expiry    | Long inactivity              | Re-login                                      |
| refresh_invalid         | Token not found / wrong user | Malformed or forged token    | Re-login; watch for abuse                     |
| missing_refresh         | No cookie & no body token    | Missing credentials          | Client bug; ensure cookie included            |
| refresh_body_disallowed | Body path disabled (Phase 3) | Deprecated client            | Update client to rely on cookie               |

## 7. Metrics & Observability

Meter: `Appostolic.Auth`

Key counters:

- `auth.login.success` / `auth.login.failure`
- `auth.refresh.success` / `auth.refresh.failure`
- `auth.refresh.rotations`
- `auth.refresh.reuse_denied`
- `auth.refresh.expired`
- `auth.refresh.plaintext_emitted` (TEMP) / `auth.refresh.plaintext_suppressed`
- `auth.logout.single` / `auth.logout.all`

Histograms:

- `auth.login.duration_ms` (attr: outcome)
- `auth.refresh.duration_ms` (attr: outcome)

Operational Dashboards (suggested PromQL pseudocode):

- Refresh Success Rate: sum(rate(auth_refresh_success[5m])) / sum(rate(auth_refresh_success + auth_refresh_failure[5m]))
- Reuse Spike Alert: increase(auth_refresh_reuse_denied[15m]) > 5
- Plaintext Regression: sum(rate(auth_refresh_plaintext_emitted[5m])) > 0 (should trend to zero)

Logs (structured): `auth.refresh.rotate`, `auth.refresh.reuse_denied`, `auth.logout.single`, etc.— use user_id + refresh_id for correlation (no plaintext tokens).

## 8. Architecture (High-Level Flow)

See diagram: `docs/diagrams/auth-flow.mmd` (Mermaid) or rendered `auth-flow.v1.svg`.

Flow Summary:

1. Login / Magic Consume → Issue neutral access (short-lived) + refresh (persisted hashed) + cookie `rt`.
2. (Optional) Tenant Selection → Rotate neutral refresh (old revoked) + tenant-scoped access token.
3. Silent Refresh (frontend timer) → `/api/auth/refresh` rotates refresh + issues new access (and optional tenant token if query param) prior to expiry.
4. Logout → Revoke single refresh (cookie cleared).
5. Logout All → Revoke all user refresh tokens + bump TokenVersion (invalidate all access tokens immediately).

## 9. Writing Authenticated Tests

Use the test token mint helper (`TestAuthClient`) rather than scripting full login+selection unless expressly validating those flows. Helper issues neutral + (optional) tenant token and refresh cookie under test configuration flags, reducing boilerplate and speeding integration tests. Avoid re-introducing dev headers; those will be fully removed in the RDH sprint.

## 10. Security Rationale

- Refresh cookie (httpOnly + SameSite=Lax + Secure over HTTPS) limits XSS exfiltration.
- Access token in-memory only (ephemeral) reduces persistence attack surface.
- Rotation & single active chain thwart replay of old refresh tokens (`refresh_reuse`).
- TokenVersion provides instant global revocation (password change / logout-all) without blacklist overhead.
- Transitional flags + metrics create a safe, observable deprecation path.

## 11. Forward Looking (RDH Sprint & Post‑1.0)

Dev Header Decommission COMPLETE: Development headers (`x-dev-user`, `x-tenant`), the composite scheme, and feature flag have been removed. Any request sending those headers now receives 401 `{ "code": "dev_headers_removed" }`. Rollback path: checkout tag `before-dev-header-removal` if emergency reintroduction required (short-term only). See `devInfo/jwtRefactor/rdhSprintPlan.md` Story 4 entry for details.

### 11.1 Dev Headers Removal (RDH) – Migration Steps

What changed:

- Removed: `DevHeaderAuthHandler`, composite policy scheme `BearerOrDev`, feature flag `AUTH__ALLOW_DEV_HEADERS`.
- Added: Permanent guard rejecting any request containing `x-dev-user` or `x-tenant` with 401 `{ code: "dev_headers_removed" }`.
- Unified: All environments (dev/test/CI/prod) now exercise the same JWT + refresh rotation path.

How to adapt local workflows:

1. Use real login + (optional) select-tenant in manual cURL or API client flows.
2. Replace any scripts that previously set `x-dev-user` / `x-tenant` with a small login helper capturing the returned access token.
3. For integration tests: ensure they use flow helpers (`AuthTestClientFlow.LoginAndSelectTenantAsync`) or deterministic token issuance helpers rather than header injection.
4. Remove any `.env` vars like `DEV_USER`, `DEV_TENANT` (no longer used by web proxy routes).
5. Confirm guard tests/grep pass (`scripts/dev-header-guard.sh`).

Rollback (LAST RESORT, short window only):

1. `git checkout before-dev-header-removal` (tag created pre-physical removal) OR revert the removal commit series.
2. Rebuild & run tests; dev headers will function again under that snapshot.
3. Forward path: re-apply required commits excluding dev header code if reverting only to investigate a regression.

Operational considerations:

- Metrics related to deprecation (`auth.dev_headers.deprecated_requests`) have been removed; any dashboards referencing them should be updated.
- Security posture improved by eliminating an alternate implicit auth path; review any pending tooling that assumed header-based impersonation.
- Monitor for unexpected 401 `dev_headers_removed` occurrences shortly after deployment; they indicate stale clients or scripts.

Reference Files:

- `devInfo/jwtRefactor/rdhSprintPlan.md`
- `SnapshotArchitecture.md` (Auth & Flags sections)
- `scripts/dev-header-guard.sh`

Planned Post‑1.0 Enhancements:

- Multi-key signing & automated key rotation window.
- Session enumeration endpoint (`/api/auth/sessions`) + selective logout.
- CSRF token strategy if `SameSite=None` required.
- Rate limiting enforcement (beyond metrics stub) for refresh storms.
- Metrics dashboard templates (Grafana JSON) + alert rules.

## 12. Quick Verification Checklist

- [ ] Access token contains claim `v` (matches DB TokenVersion).
- [ ] `Set-Cookie: rt=...; HttpOnly; SameSite=Lax; Secure` (when HTTPS) present on login.
- [ ] Refresh rotation: old refresh token rejected (401 `refresh_reuse`).
- [ ] Silent refresh renews access prior to expiry (no user action).
- [ ] Plaintext refresh.token absent when `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT=false`.
- [ ] Body refresh disabled returns 400 `refresh_body_disallowed` when grace off.
- [ ] Logout clears cookie; logout/all bumps TokenVersion (old access 401).
- [ ] Metrics counters increment (login/refresh/logout) in collector.

## 13. Support Troubleshooting Map

Symptom → Checks:

- Frequent `refresh_reuse` → Investigate client concurrency (multiple tabs) or theft attempt; check IP/user-agent clustering.
- High `refresh_failure` ratio → Inspect logs for error codes; ensure DB latency normal; verify cookie transmitted (browser devtools / network).
- Unexpected `token_version_mismatch` floods → Possible forced logout-all event or race; confirm no rogue admin tool incrementing versions.
- Plaintext emissions reappear → Flag toggled or rogue environment file; verify `AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT`.

## 14. Appendix: Sample Curl Flow (Dev)

(Ensure API running; adapt port as needed.)

```
# Signup
curl -i -X POST http://localhost:5198/api/auth/signup \
  -H 'Content-Type: application/json' \
  -d '{"email":"user+demo@example.com","password":"Passw0rd!"}'

# Extract Set-Cookie rt=...
# Simulate refresh (cookie included automatically if using browser/fetch with credentials)

curl -i -X POST http://localhost:5198/api/auth/refresh \
  -H 'Cookie: rt=<refreshTokenFromCookie>'
```

---

Last updated: 2025-09-22 (post dev header removal)
