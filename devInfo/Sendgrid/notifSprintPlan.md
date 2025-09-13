# Notifications Sprint Plan — Executable Stories

This plan turns the SendGrid notifications design into concrete stories. Prefix: Notif-xx.

## ~~Notif-01 — Notifications scaffolding and options~~

Summary

- Create the notifications folder structure, core interfaces, and options bindings.

Acceptance Criteria

- Folders/files exist under `apps/api/App/Notifications/*`
- Interfaces defined: `IEmailQueue`, `IEmailSender`, `ITemplateRenderer`
- Options defined and bound: `SendGridOptions`, `EmailOptions`, `SmtpOptions`
- Program.cs registers options (no runtime errors in Development)

Key Tasks

- Add `App/Notifications` with placeholder interfaces and records
- Add options classes and `BindConfiguration` wiring

## ~~Notif-02 — Template renderer (Scriban)~~

Summary

- Implement file/embedded template rendering for Verification and Invite (subject/text/html).

Acceptance Criteria

- `ScribanTemplateRenderer` renders all 3 parts given an `EmailMessage`
- Templates include absolute links using `EmailOptions.WebBaseUrl`
- Unit tests cover basic render outputs (subject + link placeholder replacement)

Key Tasks

- Add renderer + simple templates for Verify/Invite
- Add minimal unit tests

## ~~Notif-03 — Email queue and dispatcher service~~

Summary

- Introduce `EmailQueue` (Channel) and `EmailDispatcherHostedService` with retry/backoff and metrics hooks.

Acceptance Criteria

- Background service consumes queued messages and calls renderer + sender
- Retries up to 3 times with exponential backoff on transient failures
- Emits structured logs (kind, to, tenantId/userId when provided)

Key Tasks

- Implement queue, hosted service, and backoff policy
- Wire hosted service in DI

## ~~Notif-04 — SendGrid provider~~

Summary

- Implement `SendGridEmailSender` as the primary provider.

Acceptance Criteria

- Uses API key from options; sends text+html
- Throws on 4xx/5xx responses; logs error snippet
- Sandbox toggle optional via config

Key Tasks

- Implement sender and basic error handling
- Add a small unit test with a mocked client (or interface seam)

## ~~Notif-05 — SMTP dev fallback (Mailhog)~~

Summary

- Implement `SmtpEmailSender` to allow local delivery via Mailhog.

Acceptance Criteria

- Configurable host/port; defaults to 127.0.0.1:1025 in Development
- Successful send to Mailhog in dev environment

Key Tasks

- Implement SMTP sender (System.Net.Mail or MailKit)
- Verify end-to-end with local Mailhog

## ~~Notif-06 — DI selection and safety checks~~

Summary

- Select provider via `Email:Provider` and guard startup when misconfigured.

Acceptance Criteria

- Development uses smtp by default; Production requires sendgrid with non-empty API key
- SendGrid API key is provided via environment variable `SendGrid__ApiKey` (preferred for .NET options binding)
- Optional convenience: if `SENDGRID_API_KEY` is present, map it to configuration key `SendGrid:ApiKey` at startup before binding
- Clear startup error when required config missing in Production

Key Tasks

- Add provider switch in Program.cs
- Pre-bind shim: if `SENDGRID_API_KEY` exists and `SendGrid:ApiKey` is empty, set `SendGrid:ApiKey` in configuration
- Add guard logic and friendly error message

Status

- Completed: Provider switch implemented with environment-driven defaults (smtp in Development, sendgrid otherwise), SENDGRID_API_KEY shim, and production guard for missing key. Unit tests added for DI selection; full test suite green.

## ~~Notif-07 — Signup verification hook~~

Summary

- Add enqueue call in signup flow to send verification email (Auth story will create endpoint and token).

Acceptance Criteria

- Given a created user and verification token, enqueue EmailMessage with correct link
- Unit/integration test uses a test double for IEmailSender to assert enqueue

Key Tasks

- Add helper method `QueueVerificationAsync(user, token)`
- Write test around the helper

Status

- Completed: Implemented `NotificationEnqueuer.QueueVerificationAsync` building the verification link from `EmailOptions.WebBaseUrl`; registered in DI; added unit tests covering absolute/relative link and fields. Full test suite green.

## ~~Notif-08 — Invite creation hook~~

Summary

- Add enqueue call when an invite is created to send invite email.

Acceptance Criteria

- Enqueues EmailMessage with tenant, role, inviter and accept link
- Test uses a test double for IEmailSender / asserts queue usage

Key Tasks

- Add helper `QueueInviteAsync(invite, tenant, inviter, token)`
- Write test around the helper

Status

- Completed: Implemented `NotificationEnqueuer.QueueInviteAsync` to build the invite accept link from `EmailOptions.WebBaseUrl` and include `tenant`, `role`, and `inviter` in the message data. Added unit tests covering absolute and relative link generation and field assertions. Full test suite green.

## ~~Notif-09 — Observability (metrics + logs)~~

Summary

- Add OTEL counters/gauges and structured logging enrichment.

Acceptance Criteria

- Metrics: emails_sent_total, emails_failed_total
- Logs include correlation fields (userId, tenantId, inviteId)

Key Tasks

- Add metric instruments and log scopes

Status

- Completed: Added counters `email.sent.total` and `email.failed.total` (tagged by `kind`) in `EmailMetrics` and wired increments from `EmailDispatcherHostedService`. Enriched dispatcher logs with correlation fields when present on `EmailMessage.Data` (userId, tenantId, inviteId; plus tenant, inviter fallbacks). Added a unit test to capture logger scopes and verify correlation keys are included. Full suite green.

## ~~Notif-10 — Docs and environment wiring~~

Summary

- Document configuration and update infra env files.

Acceptance Criteria

- README/RUNBOOK/SnapshotArchitecture updated with Email section
- Document config schema and exact key names:
  - Email: { Provider: 'smtp' | 'sendgrid', WebBaseUrl: '...' }
  - SendGrid: { ApiKey: '...' }
- Persistence guidance by environment:
  - Local Development: default to `Email:Provider=smtp` (Mailhog). For testing SendGrid locally, developers export `SendGrid__ApiKey` in their shell or use a non-committed `.env.local` file. Do not commit real keys.
  - CI/CD & Staging/Prod: store the secret in the platform’s secret manager (e.g., GitHub Actions, cloud provider) and inject as environment variable `SendGrid__ApiKey` for the API service.
- `infra/docker/.env` gains non-secret defaults (no API key) and comments indicating to set `SendGrid__ApiKey` out-of-band when needed.

Key Tasks

- Update docs and env sample values
- Add examples showing `export SendGrid__ApiKey=...` for local testing and secret wiring for deployments (without committing real values)

Status

- Completed: Added Email section to README and RUNBOOK with dev Mailhog flow, provider switching, config keys, and troubleshooting. Updated `.env.example` with `Email__*`, `Smtp__*`, and a commented `SendGrid__ApiKey`. Exposed Mailhog SMTP port 1025 in `infra/docker/compose.yml` so the API can send via 127.0.0.1:1025. Note: `infra/docker/.env` is not used by our Makefile/Compose invocation (root `.env` is passed via `--env-file .env`), so we did not add secrets there.

## ~~Notif-11 — E2E dev verification (Mailhog)~~

Summary

- Verify an end-to-end flow delivers an email to Mailhog.

Acceptance Criteria

- Local run with provider=smtp sends email captured by Mailhog with expected subject/link

Key Tasks

- Add a tiny dev endpoint or test hook to enqueue a sample message and manually verify

Status

- Completed: Added dev-only API endpoints (`/api/dev/notifications/verification` and `/invite`) and Next.js proxy routes, brought up Docker stack with Mailhog, verified `202 Accepted` and delivery to Mailhog UI. Updated README/RUNBOOK with Try it steps. API build PASS; web typecheck PASS.

## ~~Notif-12 — Hardened error handling and dedupe~~

Summary

- Add jittered backoff, dead-letter logging, and optional dedupe key on enqueue.

Acceptance Criteria

- Dispatcher uses jitter; final failure logs a dead-letter event with message key
- Optional dedupe mechanism prevents duplicate sends across restarts when key unchanged

Key Tasks

- Implement jitter, dead-letter log; design simple dedupe key pattern

Status

- Completed: Implemented jittered backoff (+/-20%), dead-letter logging with message key, and optional dedupe via `DedupeKey` backed by an in-memory TTL store. Updated `NotificationEnqueuer` to set keys for verification/invite. Refactored SMTP sender for testability and ensured template rendering handles nulls safely. Full test suite passing (72/72).

## Notif-13 — Notifications table (outbox) migration

Summary

- Add app.notifications table + enums/indexes for DB-backed outbox.

Dependencies

- Notif-01..06.

Acceptance Criteria

- EF migration creates table/constraints/indexes as per design.
- App builds and applies migration (Development) with no data loss.

Tasks

- EF model + configuration, enum mapping, partial unique index for dedupe_key (active subset).
- Migration + update DbContext registration.
- SnapshotArchitecture “Database” section update.

## Notif-14 — Enqueue writes to DB outbox

Summary

- Refactor enqueue to insert a row (Queued) and push id into Channel.

Dependencies

- Notif-13.

Acceptance Criteria

- New enqueues create DB rows with correct fields and optional dedupe_key.
- Duplicate active dedupe_key is rejected (friendly error).

Tasks

- Add repository/service method: CreateQueuedAsync(notification).
- Compute dedupeKey in NotificationEnqueuer (Verify/Invite).
- Unit tests for insert + duplicate handling.

## Notif-15 — Dispatcher reads/updates DB records

Summary

- Dispatcher transitions Queued→Sending→Sent/Failed/DeadLetter with retries and jitter.

Dependencies

- Notif-14.

Acceptance Criteria

- On success: subject/body snapshots persisted, status=Sent, sent_at set.
- On failure: attempt_count incremented, next_attempt_at set; after max attempts → DeadLetter.

Tasks

- DB fetch loop (by status/next_attempt_at), state transitions in a transaction.
- Jittered backoff logic; structured logging with notificationId.
- Unit tests: success, retry path, dead-letter.

## Notif-16 — Admin/dev endpoints: list + retry

Summary

- Minimal endpoints to list recent notifications and retry DeadLetter.

Dependencies

- Notif-15.

Acceptance Criteria

- GET /api/dev/notifications (filters: kind, status, tenant, take/skip)
- POST /api/dev/notifications/{id}/retry moves DeadLetter/Failed to Queued.

Tasks

- Endpoints + auth (dev headers).
- Tests using in-memory DB.

Completed

- Implemented dev-only endpoints in `DevNotificationsEndpoints.cs`:
  - `GET /api/dev/notifications` with filters (kind, status, tenantId) and paging (`take`/`skip`) + `X-Total-Count`.
  - `POST /api/dev/notifications/{id}/retry` transitions `Failed/DeadLetter → Queued` via `INotificationOutbox.TryRequeueAsync`.
- Gated to `Development` and requires dev header auth (tenant/user from headers).
- Added tests in `apps/api.tests` covering listing, filtering, paging, and retry state transition (fresh scope + `AsNoTracking`).
- Result: Full API test suite green (86/86). Avoided using a shared `InMemoryDatabaseRoot` to prevent cross-test interference.

## Notif-17 — Purge job (retention)

Summary

- Background job to purge old notifications by retention window.

Dependencies

- Notif-15.

Acceptance Criteria

- Configurable retention (default 60 days).
- Job deletes rows older than retention; logs counts.

Tasks

- HostedService + options, unit tests, RUNBOOK note.

## Notif-18 — Dedupe store and policy

Summary

- Optional dedupe policy backed by DB TTL instead of in-memory only.

Dependencies

- Notif-14/15.

Acceptance Criteria

- Enqueue rejects duplicate dedupe_key when active; TTL respected.

Tasks

- DB constraint + guard, test race conditions, documentation.

## ~~Notif-19 — Delivery status webhook (optional)~~

Summary

- Implemented SendGrid event webhook to record provider delivery updates on notifications.

Acceptance Criteria

- POST `/api/notifications/webhook/sendgrid` accepts event payloads and honors optional shared-secret token header.
- Updates the notification row with a normalized `provider_status` and event timestamp; idempotent on replays.

Files/Changes

- apps/api/App/Endpoints/NotificationsWebhookEndpoints.cs — new endpoint mapping (SendGrid).
- apps/api/App/Options/SendGridOptions.cs — added `WebhookToken` for simple shared-secret validation.
- apps/api/App/Notifications/INotificationOutbox.cs — added `UpdateProviderStatusAsync`.
- apps/api/App/Notifications/EfNotificationOutbox.cs — persists provider status under `DataJson.provider_status`.
- apps/api/Program.cs — maps webhook endpoint.
- apps/api.tests/Api/NotificationsWebhookTests.cs — tests for accepted event and token behavior.

Status

- Completed: Endpoint implemented and tested; docs updated. No schema migration required (stored under `data_json`).

## ~~Notif-20 — E2E verification (outbox path)~~

Summary

- End-to-end test in Development: enqueue → DB row → SMTP → Mailhog → status=Sent.

Dependencies

- Notif-15/17.

Acceptance Criteria

- Manual and/or automated E2E confirms DB row transitions to Sent and message present in Mailhog.

Status

- Completed: Added automated E2E test (`NotificationsE2E_Mailhog`) that enqueues via dev endpoint, dispatcher sends via SMTP to Mailhog, outbox transitions to Sent, and Mailhog API confirms message presence. Fixed EF InMemory transaction issue by gating transactions behind `Database.IsRelational()`. Test passed locally with Mailhog running.

Tasks

- Add dev script/docs; optional integration test harness with Testcontainers.

## ~~Notif-21 — PII minimization and token hashing~~

Summary

- Stop persisting raw tokens; hash tokens before storage. Minimize PII in outbox payloads and dedupe keys.

Dependencies

- Notif-13..15 (outbox path in place) or parallel design prep.

Acceptance Criteria

- Outbox model includes token_hash (no raw token stored).
- Dedupe keys use hashed email (normalized) and never raw tokens.
- data_json contains only minimal fields; no sensitive payloads.
- Logs redact emails to k\*\*\*@domain.com.

Status

- Completed: Added `token_hash` to outbox model and EF mapping/migration; `NotificationEnqueuer` normalizes email, computes SHA‑256 token hash, pre‑renders subject/html/text snapshots, and stores only the hash (no raw tokens) in the outbox. Dispatcher reuses snapshots when present. Logging redacts recipient emails across SMTP/SendGrid paths. Focused regression and full API test suites are green.

Tasks

- Update model/config: add token_hash (text), ensure raw tokens never persisted. ✅
- Update NotificationEnqueuer to compute token_hash and hashed-email dedupe keys. ✅
- Add redaction helper; use in dispatcher logs. ✅
- Unit tests for hashing and redaction. ✅

## Notif-22 — Field-level encryption for sensitive columns (optional)

Summary

- Encrypt sensitive outbox fields at rest (if stored): body_html, body_text, to_name (and optionally subject).

Dependencies

- Notif-13..15.

Acceptance Criteria

- AES-GCM envelope encryption with rotating key (via .NET DataProtection or KMS-backed key).
- Read/write paths covered by tests; migration adds encrypted columns or switches serialization.
- Configurable on/off; default off in Development.

Tasks

- Introduce IFieldCipher abstraction and implementation.
- Wrap persistence for targeted fields; migration if schema changes required.
- Tests for encrypt/decrypt and key-rotation compatibility.

## Notif-23 — Retention policy hardening (PII-aware)

Summary

- Extend purge job to enforce PII retention windows (60 days Sent/Failed, 90 days DeadLetter) and document schedule.

Dependencies

- Notif-17.

Acceptance Criteria

- Configurable retention per status; defaults documented.
- Purge run logs counts; verified by unit/integration tests.

Tasks

- Extend purge job options and filtering by status/age.
- Tests with seeded data across age buckets.
- RUNBOOK update for retention settings.

## Notif-24 — Access control for notification views (prod)

Summary

- Restrict list/retry endpoints and future admin UI: SuperAdmin cross-tenant; Tenant Admin scoped to their tenant.

Dependencies

- Notif-16 (list/retry endpoints).

Acceptance Criteria

- Authorization policy and handlers enforce role/tenant scope.
- Queries filter by tenant for Tenant Admin.
- Dev endpoints remain dev-only.

Tasks

- Add policies/attributes; enforce in endpoints.
- Add tests for authorization and tenant filtering.

## Notif-25 — Logging and telemetry privacy gates

Summary

- Enforce redaction and avoid PII in logs/metrics.

Dependencies

- Notif-09 (observability).

Acceptance Criteria

- Logger helpers redact emails across dispatcher/enqueuer.
- Metrics exclude PII (counts/tags only).
- Tests for redaction helper and logging usage.

Tasks

- Implement RedactEmail utility.
- Replace direct email logging with redacted versions.
- Add unit tests verifying redaction in scopes/messages.

## Notif-26 — Privacy Policy and vendor compliance docs

Summary

- Document PII handling, retention, subprocessors (SendGrid), and user rights; add DPA references.

Dependencies

- None (docs).

Acceptance Criteria

- Privacy Policy draft updated in repo.
- Subprocessor list includes SendGrid; DPA/Vendor section in RUNBOOK.
- Link from README.

Tasks

- Author/update Privacy Policy and subprocessor doc.
- RUNBOOK: data-subject request handling and incident response outline.
- README: link to policy and subprocessor list.

## Notif-27 — Outbox schema extension for Resend

Summary

- Extend the notifications table with resend fields and supporting indexes.

Dependencies

- Notif-13..15 (Outbox baseline).

Acceptance Criteria

- Columns added: resend_of_notification_id, resend_reason, resend_count (default 0), last_resend_at, throttle_until.
- Indexes created on (resend_of_notification_id) and (to_email, kind, created_at desc).
- Migrations apply cleanly in Development.

Tasks

- EF model + configuration; migration; update SnapshotArchitecture DB section.

## Notif-28 — Manual resend endpoint (single)

Summary

- Add POST /api/notifications/{id}/resend to create a new notification referencing the original.

Dependencies

- Notif-27.

Acceptance Criteria

- Creates a new outbox row with resend_of_notification_id set, derived dedupe_key that bypasses standard dedupe, and policy-enforced throttle.
- Returns 201 with new id; 429 if throttled; 404 if original not found; 403 if unauthorized.

Tasks

- Endpoint, policy service (throttle calc), unit/integration tests, structured logs with originalId.

## Notif-29 — Bulk resend endpoint

Summary

- Add POST /api/notifications/resend-bulk with filters to resend many safely.

Dependencies

- Notif-28.

Acceptance Criteria

- Accepts filters (kind, tenant, to_emails, from/to date, limit).
- Streams creation with batching/backpressure; returns a summary (created, skipped_throttled, errors).
- Enforces per-tenant caps and per-recipient throttle.

Tasks

- Endpoint + request validation, batch processing, caps, tests (happy/edge cases), RUNBOOK note.

## Notif-30 — Resend policy, throttling, and metrics

Summary

- Centralize resend policy and add metrics.

Dependencies

- Notif-28/29.

Acceptance Criteria

- Configurable defaults: min interval (e.g., 48h), per-tenant daily cap, per-run safety cap.
- Metrics: email.resend.total tagged by kind and reason.
- Unit tests for policy outcomes.

Tasks

- Policy service + options, metrics emission, docs for defaults and overrides.

## Notif-31 — Resend history and admin UI hooks (API)

Summary

- Provide history API to list resends for a notification and wire minimal admin hooks.

Dependencies

- Notif-28.

Acceptance Criteria

- GET /api/notifications/{id}/resends returns child notifications (paged).
- Web: placeholder admin integration point defined (API contract docs).

Tasks

- Endpoint + tests, API docs update, add to SnapshotArchitecture “Notifications” section.

## Notif-32 — Automated resend (no-action detector) [optional]

Summary

- A scheduled job initiates resends when an expected action did not occur.

Dependencies

- Notif-30.

Acceptance Criteria

- Job evaluates rules (e.g., “Invite not accepted after 7 days”), respects throttle/caps, and emits metrics/logs.
- Configurable enable/disable and evaluation window.

Tasks

- HostedService + rules interface, tests with seeded data, RUNBOOK “operations” note.

---

Suggested order: Notif-01 → Notif-02 → Notif-03 → Notif-04 → Notif-05 → Notif-06 → Notif-07 → Notif-08 → Notif-09 → Notif-10 → Notif-11 → Notif-12 → Notif-13 → Notif-14 → Notif-15 → Notif-16 → Notif-17 → Notif-18 → Notif-19 → Notif-20 → Notif-21 → Notif-22 → Notif-23 → Notif-24 → Notif-25 → Notif-26 → Notif-27 → Notif-28 → Notif-29 → Notif-30 → Notif-31 → Notif-32
