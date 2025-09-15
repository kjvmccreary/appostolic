# Vendor Compliance — Notifications Subsystem

This living document inventories subprocessors used by the Notifications subsystem and describes data flows, configuration, and compliance considerations.

## Subprocessors

- SendGrid (Production email delivery)
  - Data: recipient email/name, subject, html/text body, provider metadata
  - Storage: transient in provider; delivery status returned via webhook and stored under `notifications.data_json.provider_status`
  - Security: API key (env var `SendGrid__ApiKey`); never committed. TLS enforced. Optional webhook shared secret.
  - Compliance: SOC 2, ISO 27001 (refer to provider docs). DPA available.

- Mailhog (Development email capture)
  - Data: email content for local testing only
  - Storage: local Docker container; not exposed publicly. Resettable at any time.
  - Security: Used only in Development. No real user data should be used.

## Configuration & Secrets

- Provider selection:
  - Development: default `Email:Provider=smtp` (Mailhog)
  - Production: `Email:Provider=sendgrid` required, with non-empty `SendGrid__ApiKey`
- Compatibility shim: legacy `SENDGRID_API_KEY` is mapped to `SendGrid:ApiKey` at startup if present and unbound
- Webhook security: optional header token checked on `/api/notifications/webhook/sendgrid`

## Data Flow

1. Enqueue — API constructs `EmailMessage` (minimal PII), adds token hash when applicable
2. Outbox — `EfNotificationOutbox` writes `Queued` row; dedupe TTL claim if configured
3. Dispatcher — leases due row; decrypts if encrypted; renders or uses snapshots; sends via provider
4. Provider — attempts delivery; in production, SendGrid returns events back to `/api/notifications/webhook/sendgrid`
5. Retention — purge job scrubs sensitive fields and deletes older records per policy

## Privacy Controls

- Token hashing (Notif-21) — no raw tokens at rest
- Optional AES-GCM encryption (Notif-22) — sensitive fields encrypted at rest
- PII-aware scrubbing (Notif-23) — scrub-then-delete
- Redacted logs/metrics (Notif-25) — no raw emails or tokens in logs/labels

## Access Control

- Admin endpoints under `/api/notifications` are tenant-scoped with optional superadmin override (dev header or allowlisted emails)

## Audit & Testing

- Integration tests for admin endpoints (tenant/superadmin)
- Unit tests for dispatcher redaction and encryption round-trips
- Periodic manual verification against Mailhog in Development

## References

- Snapshot: `SnapshotArchitecture.md` — Notifications sections
- Options: `apps/api/App/Options/*`
- Implementation: `apps/api/App/Notifications/*`
- Tests: `apps/api.tests/*`
