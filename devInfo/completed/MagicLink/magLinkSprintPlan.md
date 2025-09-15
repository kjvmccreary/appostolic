# Sprint Plan — Magic Link Authentication

## Overview

Add passwordless “Magic Link” signup/login alongside the existing password flow. Reuse the Notifications outbox for email delivery and Mailhog (SMTP) in Development. Web integrates via the existing NextAuth Credentials provider (no new adapter).

## Objective

A user can request a magic link, receive it in Mailhog, click it, and land signed‑in at `/select-tenant` (new users get a personal tenant). Tokens are single‑use, expire in 15 minutes, and are stored as SHA‑256 hashes at rest.

## Guidelines

- With each story completion, update /Users/kevinmccreary/appostolic/devInfo/storyLog.md with the exact summary that you display in the Github Copilot chat window. Updates are additive; no destruction. Include a header row for each addition showing the story ID and Name.
- At the end of each story, update SnapshotArchitecture.md if appropriate. Updates should be additive. Only destructive items allowed are instances where the item is outdated or superseded.
- Commit w message and sync at conclusion of each story.
- Update the story references below each time a story is complete. Use ✅ (DONE).

## Stories

### ✅ (DONE) Auth-ML-01 — DB: Login Tokens (single-use, expiring)

- Create table `app.login_tokens`: `id`, `email` (citext), `token_hash` (sha256), `purpose` ('magic'), `expires_at`, `consumed_at`, `created_at`, optional `created_ip`/`created_ua`, `tenant_hint` (nullable).
- Indexes: unique `(token_hash)`; `(email, created_at DESC)`; partial index on `consumed_at IS NULL` for cleanup.
- Acceptance criteria:
  - New row on request; raw token is never stored.
  - PK/unique/indexes exist; migration applies cleanly.
- Tests:
  - Mapping + constraints (insert/select); unique `(token_hash)` enforced.
  - (Optional) cleanup job ignores unconsumed, deletes expired.

### ✅ (DONE) Auth-ML-02 — API: Request Magic Link

- `POST /api/auth/magic/request { email }`
  - Always `202 Accepted`; regardless of user existence, enqueue email with verify link.
  - Token TTL 15m; store `token_hash`.
- Acceptance criteria:
  - Enqueues Notifications outbox row (EmailKind=MagicLink) with absolute link `http://localhost:3000/magic/verify?token=…` (dev base).
  - Basic rate-limit: max 5 pending tokens per email per hour (or coalesce by invalidating prior tokens).
- Tests:
  - 202 path creates `login_tokens` row (hashed) and an outbox row.
  - Respects rate-limit/coalesce behavior.

### ✅ (DONE) Auth-ML-03 — API: Consume Magic Link

- `POST /api/auth/magic/consume { token }` (or GET `?token=…`)
  - Validate via hash; ensure not expired and not consumed; mark `consumed_at`.
  - If user missing, create user + personal tenant + Owner membership.
  - Return minimal user payload for NextAuth `authorize` (id/email).
- Acceptance criteria:
  - Single-use: second consume returns 400/409.
  - New users get unique personal tenant slug (`localpart-personal`, de-duped to `-2`, `-3`, ...).
- Tests:
  - Happy path (existing user).
  - Happy path (new user creates tenant/membership).
  - Expired/invalid/replayed token → 400/404/409.

### ✅ (DONE) Auth-ML-04 — Notifications: Magic Link Email

- Add `EmailKind: MagicLink`; template with “Sign in” button linking to verify URL.
- Development uses SMTP (Mailhog). SendGrid templating is not required for dev.
- Acceptance criteria:
  - Subject/body snapshots generated; logs contain no raw token (hash at rest; token only in email link).
- Tests:
  - Outbox render snapshot (subject/text/html) present; logging redaction verified.

### ✅ (DONE) Auth-ML-05 — Web: Request Page

- Route: `/login/magic` (public). Form: email → `POST /api-proxy/auth/magic/request`.
- Confirmation screen: “Check your email.”
- Acceptance criteria:
  - Same-origin proxy; no CORS.
  - Handles 202 and shows confirmation; shows basic error on network failure.
- Tests:
  - Renders form; submits; displays confirmation on 202.

### ✅ (DONE) Auth-ML-06 — Web: Verify Page + NextAuth integration

- Route: `/magic/verify` (public). Reads `?token=…`; calls `signIn('credentials', { magicToken, redirect: false })`.
- NextAuth Credentials `authorize()` posts to API `/api/auth/magic/consume` in magic-token mode.
- On success: redirect to `/select-tenant` (honor optional `?next=`).
- Acceptance criteria:
  - Works for new and existing users; ends at `/select-tenant`, then `/studio/agents`.
- Tests:
  - Valid token: triggers signIn and redirects.
  - Invalid/expired: shows friendly error with link back to request page.

### ✅ (DONE) Auth-ML-07 — Web: Credentials authorize() dual-mode

- Extend existing Credentials provider to accept either `{ email, password }` or `{ magicToken }`.
- Acceptance criteria:
  - Branches correctly; no password required in magic mode.
  - Sessions include email and standard claims.
- Tests:
  - Unit: authorize routes to proper API endpoint; handles success/failure.

### ✅ (DONE) Auth-ML-08 — Proxy Routes (avoid CORS)

- Add web routes:
  - `POST /api-proxy/auth/magic/request` → API `/api/auth/magic/request`
  - `POST /api-proxy/auth/magic/consume` → API `/api/auth/magic/consume`
- Acceptance criteria:
  - Forward dev headers/cookies as needed; endpoints allow anonymous access.
- Tests:
  - 200-path proxying; surfaces network errors.

### Auth-ML-09 — Mailhog E2E (Dev)

- Script/test to request a magic link, poll Mailhog HTTP API, extract verify URL, open it (or call verify route), and assert session established then `/select-tenant` loads.
- Acceptance criteria:
  - End-to-end works reliably with Docker Mailhog; retries handle eventual consistency.
- Tests:
  - Playwright e2e or API-level harness:
    - Request link → fetch from Mailhog → hit verify → assert redirect to `/select-tenant`.

### Auth-ML-10 — Security & Abuse Guards

- Token TTL=15m; single-use; prevent replay.
- Basic per-email rate-limit; generic 202 on request to avoid user enumeration.
- Acceptance criteria:
  - No logs print token; only hash stored.
  - API returns 202 without leaking account existence.
- Tests:
  - Rate-limit behavior; logger capture asserts no raw token is logged.

### Auth-ML-11 — Docs & Runbook

- README “Authentication (dev)” add Magic Link steps.
- RUNBOOK “Magic Link (operations)”: Dev via Mailhog; future Prod notes.
- SnapshotArchitecture “What’s new” + Auth updates; Master Guide & Story Log entries.

## Dev setup (Mailhog)

- API: `Email:Provider=smtp`, `Smtp:Host=127.0.0.1`, `Smtp:Port=1025`.
- Web: no additional config; new proxy routes under `/api-proxy/auth/magic/*`.
- Test: Mailhog UI `http://localhost:8025`; API `http://localhost:5198`; Web `http://localhost:3000`.

## Notes

- Magic Link is optional; existing password flow remains unchanged.
- Invite acceptance remains a signed‑in flow; magic link is for signup/login (can be extended later).
- No NextAuth DB adapter required; reuse Credentials provider with `magicToken` branch.
