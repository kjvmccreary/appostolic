# Notifications Design — SendGrid Integration

This document captures the plan to integrate SendGrid for transactional email in Appostolic. It focuses on two initial events aligned with the Auth sprint: account verification and tenant invite. The implementation keeps a provider-agnostic abstraction so callers don’t care whether emails deliver via SendGrid or SMTP (Mailhog) in development.

## Goals

- Deliver transactional emails reliably using SendGrid
- Keep a clean, testable abstraction (queue + sender + templates)
- Support local dev delivery via SMTP/Mailhog without changing calling code
- Provide clear hook points for upcoming Auth stories: verification and invite
- Include basic observability, retry, and safety controls

## Scope (MVP)

- API-side email pipeline (queue + dispatcher hosted service)
- Provider implementations: SendGrid (primary), SMTP dev fallback
- File-based templating for Invite and Verification
- No marketing/bulk campaigns

## Architecture

- Abstractions
  - IEmailQueue (enqueue-only + internal Channel reader)
  - EmailDispatcherHostedService (background worker)
  - IEmailSender (provider interface)
  - ITemplateRenderer (Scriban-based)
- Providers
  - SendGridEmailSender (HTTP API)
  - SmtpEmailSender (dev fallback → Mailhog)
- Templates
  - Invite: includes inviter, tenant, role, accept link
  - Verify: includes verify link
- Links
  - Built using `EmailOptions.WebBaseUrl`: absolute links for web flows
  - Examples:
    - Verify: {WebBaseUrl}/verify?token=...
    - Invite: {WebBaseUrl}/invite/accept?token=...

## Environment and secrets

API

- SendGrid API Key — keep in environment only (SendGrid\_\_ApiKey preferred; optional shim from SENDGRID_API_KEY). Do not commit secrets.
- Email:FromAddress (e.g., no-reply@appostolic.io)
- Email:FromName (e.g., Appostolic)
- Email:Provider = sendgrid | smtp
- Email:WebBaseUrl (http://localhost:3000 in dev)
- SMTP:Host (127.0.0.1), SMTP:Port (1025), SMTP:User?, SMTP:Pass?

Web

- NEXT_PUBLIC_WEB_BASE (for link consistency if web ever builds links)

Dev/ops

- Keep provider configurable via settings (Development=SMTP, Production=SendGrid)
- Add SENDGRID_API_KEY in CI/CD secrets; do not commit
- Domain authentication and link branding on SendGrid for prod

## Contracts and options

Types

- EmailKind: Verification | Invite
- EmailMessage: Kind, ToEmail, ToName?, Data (dictionary)

Options

- SendGridOptions: ApiKey, FromEmail, FromName
- EmailOptions: WebBaseUrl, Provider
- SmtpOptions: Host, Port, User?, Pass?

## Templating

- Use Scriban to render subject/text/html
- Templates live under `App/Notifications/Templates` (or embedded for simplicity)
- Sample subjects
  - Verification: "Verify your email"
  - Invite: "You're invited to join {{ tenant }}"
- Template model merges `EmailMessage.Data` with `webBaseUrl`

## Queue + hosted service

- EmailQueue: `Channel<EmailMessage>`; writer exposed via IEmailQueue; reader internal
- EmailDispatcherHostedService:
  - Read → render template → send via IEmailSender
  - Retries with exponential backoff (e.g., 3 attempts, 0.5s, 2s, 8s)
  - Logs with correlation metadata (userId, tenantId, inviteId when present)
  - OTEL metrics: emails_sent_total, emails_failed_total; span per send

## Providers

SendGridEmailSender

- Build `MailHelper.CreateSingleEmail` with text+html
- Fail on 4xx/5xx responses; include response body excerpt in logs
- Optional sandbox toggle for non-prod

SmtpEmailSender (dev)

- Use `System.Net.Mail` or MailKit
- Point to Mailhog (127.0.0.1:1025) in dev

## DI wiring (Program.cs)

- Add options binding from config
- Register:
  - `AddSingleton<ITemplateRenderer, ScribanTemplateRenderer>()`
  - `AddSingleton<IEmailQueue, EmailQueue>()`
  - `AddHostedService<EmailDispatcherHostedService>()`
  - `AddSingleton<IEmailSender, SendGridEmailSender>()` when provider=sendgrid
  - `AddSingleton<IEmailSender, SmtpEmailSender>()` when provider=smtp

## Hook points for Auth sprint

- Signup (Auth-02)
  - After creating user + verification token, enqueue Verification email with link
- Invite create (Auth-07)
  - After creating invite row + token, enqueue Invite email with link
- Invite accept (Auth-08)
  - Optional: notify inviter (future)

Example enqueue (pseudo C#)

- Verification
  - Kind: Verification
  - To: user.Email
  - Data: { link: `${WebBaseUrl}/verify?token=${token}`, name: user.Name ?? user.Email }
- Invite
  - Kind: Invite
  - To: invite.Email
  - Data: { link: `${WebBaseUrl}/invite/accept?token=${token}`, tenant: tenant.Name, role: invite.Role, inviter: currentUser.Name }

## Observability and safety

- Log structured fields: kind, to, tenantId, userId, inviteId
- Retry backoff with jitter; dead-letter log on final failure
- Rate limit signup/invite endpoints to mitigate abuse
- Optional dedupe key on enqueue in case of retries across process restarts

## Testing strategy

Unit

- Template rendering fills expected subject and link
- SendGrid sender formats properties and handles non-2xx responses

Integration (API)

- Swap IEmailSender with a test double; assert enqueue on signup and invite endpoints
- Dispatcher happy-path and retry behavior with injected transient failures

E2E (dev)

- With SMTP provider, verify Mailhog receives email and content contains correct link

## Tasks breakdown (engineering)

1. Scaffolding

- Add `App/Notifications` folder and core interfaces/types
- Add options classes and config binding

2. Templates

- Add `Templates/Invite.sbn` and `Templates/Verify.sbn` (or embed string constants for MVP)

3. Providers

- Implement SendGridEmailSender; minimal SMTP fallback for dev

4. Dispatcher

- Implement hosted service with backoff and metrics

5. DI wiring

- Register options, services, and hosted service in `Program.cs`

6. Hooks

- Add enqueue calls in signup and invite endpoints when Auth stories are implemented

7. Docs & env

- Update README/RUNBOOK/SnapshotArchitecture with Email section and env vars
- Add non-secret defaults to `infra/docker/.env` (no API key)

## Risks and mitigations

- Misconfigured domains → use sandbox in staging and double-check sender identity
- Missing API key in prod → startup guard that fails when provider=sendgrid and key empty
- Email deliverability → add List-Unsubscribe header for future; ensure SPF/DKIM configured

---

## Feature add — Notification Outbox Persistence (DB-backed)

Summary

- Persist outgoing notifications in a DB “outbox” for auditability, idempotency (dedupe), retries, and reporting. The dispatcher reads from the outbox and updates status transitions.

Why

- Audit/troubleshoot (who/what/when, last error), idempotency via dedupeKey, retry scheduling (nextAttemptAt), dead-letter tracking, reporting by kind/tenant.

Minimal schema

- Table: app.notifications
  - id (uuid, pk)
  - kind (text/enum: Verification, Invite, …)
  - to_email (citext), to_name (text, null)
  - subject (text, null) — snapshot at send-time
  - body_html (text, null), body_text (text, null) — snapshot at send-time
  - data_json (jsonb) — original template data (link, tenant, role, etc.)
  - tenant_id (uuid, null) — for reporting/guardrails
  - dedupe_key (text, null) — unique within active/retention window
  - status (text/enum: Queued, Sending, Sent, Failed, DeadLetter)
  - attempt_count (int2, default 0)
  - next_attempt_at (timestamptz, null)
  - last_error (text, null)
  - created_at (timestamptz, default now())
  - sent_at (timestamptz, null)
  - updated_at (timestamptz, default now())
- Indexes:
  - (status, next_attempt_at) for dispatcher queries
  - (tenant_id, created_at DESC) for admin views
  - (created_at DESC)
  - Unique partial on (dedupe_key) where status in (‘Queued’, ‘Sending’, ‘Sent’) within TTL window

Processing model

- Enqueue:
  - Insert row (Queued) with data_json + optional dedupe_key; reject insert if active duplicate exists.
  - Push the new id onto the in-memory channel (fast-path wakeup).
- Dispatch:
  - Select next eligible by (status=Queued and next_attempt_at is null or due).
  - Mark Sending, render templates, attempt send.
  - On success: update subject/body snapshots, set Sent + sent_at; on failure: increment attempt_count, set last_error; schedule next_attempt_at with jitter; after max attempts → DeadLetter.
- Dedupe:
  - Compute dedupe keys per flow (e.g., Invite:{tenant}:{email}:{token}, Verify:{email}:{token}). TTL: 24h (configurable).
- Retention:
  - Purge after 30–90 days (configurable); keep DeadLetter longer if needed.

Observability

- Counters for queued/sent/failed/dead-letter by kind.
- Duration histograms (enqueue→send).
- Correlation fields on logs: userId, tenantId, inviteId, notificationId.

Ops and safety

- PII minimization: store only required fields. Consider encrypting body fields if mandated.
- SendGrid webhook (future): enrich delivery status (delivered/bounced/blocked) by notificationId or dedupe_key correlation.

Rollout plan

- Migration adds table and enum.
- Refactor enqueuer to insert into outbox and push id to channel.
- Dispatcher updated for DB transitions; keep Channel as wake-up signal (not as primary queue).
- Add admin/dev endpoints to list/retry dead-letter.
- Add purge job (background scheduled).
- Backward compatible: no change to callers (same enqueue API).

---

## Feature add — PII & Privacy-by-Design for Notifications

Goals

- Minimize, protect, and govern personal data in notifications (emails and outbox persistence).
- Support audits and retries without retaining unnecessary PII.

Principles

- Data minimization: store only what’s necessary to deliver and audit.
- Token safety: never persist raw verification/invite tokens; store salted/peppered hashes.
- Least privilege and access control: admin-only views; filter by tenant where applicable.
- Retention and deletion: purge on a defined schedule; keep dead-letter longer if required.
- Secure by default: TLS in transit; encryption at rest for sensitive fields if stored.

What to persist (outbox)

- Required:
  - kind (Verification, Invite, …)
  - to_email (citext) — consider hashing for dedupe/logging; keep clear-text for delivery/audit
  - to_name (nullable)
  - data_json (jsonb) — minimal dynamic fields only (e.g., tenant slug, role). Avoid sensitive payloads.
  - dedupe_key (text, nullable) — computed from non-PII or hashed PII (see below)
  - status, attempt_count, next_attempt_at, last_error, created_at, sent_at
- Optional snapshots (prefer not storing, or store briefly):
  - subject, body_html, body_text
  - Recommendation: store subject; omit HTML/text unless needed for audit. If stored, encrypt at rest.

Tokens and links

- Do not store raw tokens. Store a hashed token:
  - Hash: HMAC-SHA256(token, server-side pepper) or Argon2id with pepper.
  - On accept/verify, hash incoming token and compare.
- Links in emails should embed opaque tokens only; never include PII in query strings.

Dedupe keys

- Never use raw PII. Use a stable hash:
  - Example: Invite:{tenantSlug}:{sha256(normalizedEmail + ":" + tokenPepper)}
  - Example: Verify:{sha256(normalizedEmail + ":" + tokenPepper)}
- TTL for dedupe windows remains configurable (e.g., 24h).

Logging and telemetry

- Don’t log full emails or tokens. Redact to k\*\*\*@domain.com for logs.
- Include correlation IDs (notificationId, userId, tenantId, inviteId) instead of PII.
- Metrics contain counts tagged by kind/status only (no PII).

Access control

- Dev endpoints remain dev-only (header auth).
- Future admin UI/API:
  - Platform SuperAdmin can query across tenants.
  - Tenant Admin can only view notifications tied to their tenant.
  - Enforce via API authorization and DB filtering.

Retention and purge

- Default retention: 60 days for Sent/Failed; 90 days for DeadLetter (configurable).
- Purge tasks run on a schedule; keep metadata (counts) in metrics, not PII.

Encryption at rest (optional but recommended if storing bodies)

- Field-level encryption (AES-GCM envelope; key from KMS/KeyVault/DPAPI/DataProtection).
- Candidate fields: to_name, subject (optional), body_html, body_text, data_json subfields if sensitive.
- Key rotation strategy documented; non-breaking decrypt path required.

Vendor/data transfer (SendGrid)

- Configure SPF/DKIM/DMARC for custom domain.
- Disable click/open tracking unless consented; disclose in Privacy Policy if enabled.
- Webhook (future): accept events with signature verification; do not echo back PII.

Documentation and policy

- Update Privacy Policy: what data, why, retention, subprocessors (SendGrid), user rights.
- Maintain DPA and subprocessor list; include SendGrid.
- Add internal runbook for data subject requests and incident response.

Impact to existing design

- Outbox schema gains hashes (token_hash), optional encrypted columns, and stricter logging/redaction rules.
- Enqueue path computes dedupe keys using hashed email; tokens are hashed before persistence.
- Purge job expands to PII-aware retention windows.

---

## Feature add — Resend prior messages (manual, bulk, automated)

Goals

- Provide a safe, auditable way to resend previously sent notifications:
  - Manual: a user/admin explicitly resends a prior message.
  - Bulk: select multiple prior messages to resend.
  - Automated: future rules/processes trigger resends when an expected action didn’t occur.
- Respect privacy, rate limits, and dedupe logic while producing a new notification record.

Use cases

- Manual follow-up: Tenant Admin resends an Invite that wasn’t acted on.
- Bulk follow-up: Select multiple un-responded Verifications and resend.
- Automated follow-up: A scheduled job identifies “no action taken” after N days and resends.

Data model (extends Outbox)

- Columns (nullable unless noted):
  - resend_of_notification_id (uuid): references the original notification being resent.
  - resend_reason (text/enum): e.g., “Manual”, “Bulk”, “AutomatedNoAction”.
  - resend_count (int, default 0): number of times this logical message was resent (aggregated on original).
  - last_resend_at (timestamptz): timestamp of the most recent resend event related to the original notification.
  - throttle_until (timestamptz): next time a resend is allowed for this recipient/context (per policy).
- Indexes:
  - (resend_of_notification_id)
  - (to_email, kind, created_at desc)
- Dedupe considerations:
  - Explicit resends must bypass standard dedupe. Use a derived dedupe_key: {original.dedupe_key}:resend:{resendOrdinal}.
  - Enforce rate limits via throttle_until to avoid spam.

Resend flows

- Manual (single):
  - Input: original notification id; optional override fields (e.g., subject suffix).
  - Action: create a new outbox row with resend_of_notification_id=original.id, compute new dedupe_key, increment original.resend_count and last_resend_at, honor throttle policy, enqueue.
- Bulk:
  - Input: filter (kind, tenant, to_email list, date range, status/action flags).
  - Action: iterate and apply the Manual flow with batching + backpressure; summarize results.
- Automated:
  - A scheduled job queries domain signals (e.g., “Invite not accepted after 7 days”) and issues resends using the same mechanism, tagging resend_reason=AutomatedNoAction.

Policy & safeguards

- Throttling:
  - Default min interval between resends for the same (to_email, kind, tenant): e.g., 48h (configurable).
  - Respect per-tenant daily caps for automated resends.
- Audit:
  - Persist resend_reason, link to original (resend_of_notification_id), and maintain counters.
- PII:
  - No additional PII beyond what Outbox already stores (minimized, token hashing).
  - Logs redact emails; metrics exclude PII.
- Observability:
  - New counter: email.resend.total (tagged by kind and reason).
  - Correlate notificationId and originalId in logs/spans.

API endpoints (admin/dev; prod-guarded)

- POST /api/notifications/{id}/resend — manual single resend
- POST /api/notifications/resend-bulk — bulk resend by filter
- GET /api/notifications/{id}/resends — list resends for an original
- Dev-only variants remain under /api/dev/\* for testing

UI (future)

- Admin grid: add “Resend” per row; bulk select + “Resend selected”.
- Confirmation modal displays throttle/cap policy and estimated total.
- Show resend history on a notification detail panel.

Testing

- Unit: policy calc (throttle), dedupe_key derivation, counters, audit fields.
- Integration: endpoints create new rows with correct links to originals; throttle enforced.
- E2E (dev): bulk resend to Mailhog with caps enforced and results summarized.

Security & compliance

- Respect opt-out where applicable (not in MVP if strictly transactional).
- Document resend behavior in Privacy Policy.
- Ensure rate-limits and throttles to prevent abuse.
