# Notifications Privacy Policy (Engineering Draft)

This engineering draft documents how the Notifications subsystem handles user data (particularly PII) across environments. It guides implementation and review; product/legal policy may be published separately.

## Scope

- Subsystem: Email Notifications (outbox, templates, providers)
- Data types: recipient email/name, template data_json, token parameters (verification/invite), message content (subject/html/text), provider delivery status

## Data handling principles

- Minimize PII: store only what’s necessary to fulfill delivery and troubleshooting.
- Defense-in-depth: hash secrets/tokens; encrypt sensitive fields at rest (optional); redact logs/telemetry.
- Tenancy: all data is tenant-scoped; RLS enforced in DB.
- Retention: time-bounded retention and scrub-then-delete sequencing.

## Storage

- Outbox table: `app.notifications`
  - Core fields: `id`, `tenant_id`, `kind`, `to_email (citext)`, `to_name`, `subject`, `body_html`, `body_text`, `data_json (jsonb)`, `status`, `attempt_count`, `next_attempt_at`, `last_error`, timestamps
  - Optional `token_hash (text)`: SHA-256 of verification/invite tokens; raw tokens never stored
  - Optional `dedupe_key (varchar)`: for repeat-suppression; TTL table governs duplicates

## Token handling (Notif-21)

- Tokens used in verification/invite flows are never stored in plaintext.
- We store `token_hash = SHA-256(token)` for idempotence/troubleshooting.
- Links in emails contain the token; server validates and consumes it.

## Encryption at rest (Notif-22)

- Optional AES-GCM encryption via `IFieldCipher` for sensitive fields:
  - `to_name`, `body_html`, `body_text`; optionally `subject`
- Format: `enc:v1:<base64url(nonce|ciphertext|tag)>`
- Decrypt only when leasing a row for send; keep ciphertext in DB otherwise.

## Retention & scrubbing (Notif-23)

- PII-aware scrub phase nulls sensitive fields after a shorter window; then standard deletion windows apply.
- Configurable via `Notifications` options; default scrub-then-delete windows documented in SnapshotArchitecture.

## Logging/Telemetry (Notif-25)

- Logs: recipient emails are redacted in all log statements and scopes (e.g., `u***@example.com`).
- Metrics: only non-PII tags (e.g., `kind`); counts only. No emails, tokens, names in labels.
- Providers and dispatcher enforce redaction; unit tests verify coverage.

## Providers

- Development: SMTP → Mailhog (no real delivery), safe defaults.
- Production: SendGrid (API key required in prod). Webhook events are normalized and stored under `data_json.provider_status`.

## Access control (Notif-24)

- Admin endpoints under `/api/notifications` are tenant-scoped by default.
- Superadmin capability allows cross-tenant reads and retry operations when `superadmin` claim is present (dev header or allowlist).

## Subprocessors

- Development: Mailhog (local only; no external processing).
- Production: SendGrid (email delivery). See vendor compliance for details.

## Incident response

- If PII exposure is suspected, rotate logs and audit recent changes. Unit tests cover redaction; re-run suite and review providers.
- Review configuration for encryption and retention windows; tighten as needed.

## References

- `SnapshotArchitecture.md` — Notifications sections (Outbox, Webhooks, Retention, Privacy gates)
- `apps/api/App/Notifications/*` — implementation
- `apps/api.tests/*` — dispatcher/provider and privacy tests
