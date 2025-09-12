# Notifications Transport Migration Design (Monolith → Broker → Service)

This document outlines a pragmatic, low-risk path to evolve the current notifications subsystem from a modular monolith (Channel + DB outbox) to an optional broker-backed transport and, if/when needed, a separate NotificationsService. The plan preserves the API enqueue contract and emphasizes idempotency, PII safety, observability, and rollbackability at every stage.

## Objectives

- Keep MVP simple: remain a modular monolith while adding a transactional outbox and idempotent dispatcher.
- Prepare clean seams so that swapping the in-memory Channel for a message broker (RabbitMQ/SQS/etc.) is configuration-only for callers.
- Support DLQ, retry/backoff with jitter, dedupe, and privacy-by-design throughout the lifecycle.
- Enable future extraction of the dispatcher into a separate deployable without changing API contracts.

## Current State (Stage 0)

- Enqueue → in-memory Channel (EmailQueue).
- Dispatcher → background hosted service in the API process.
- Providers → SendGrid/SMTP, with jittered retries, dedupe (in-memory), and dead-letter logging.
- No DB persistence for notifications yet (planned in Notif-13..15).

## Target Intermediate State (Stage 1: DB Outbox Monolith)

- Introduce `app.notifications` outbox table (Queued/Sending/Sent/Failed/DeadLetter), attempt counts, next_attempt_at, dedupe_key, token hashing, minimal PII, optional encrypted fields.
- Enqueue writes a row (Queued) and signals a wake-up via Channel (fast path). The outbox is the source of truth.
- Dispatcher reads eligible rows, transitions states with jittered retries, and persists subject/body snapshots (optional) and sent_at.
- Admin/dev endpoints for listing and retrying; purge job for retention.

## Future-Ready State (Stage 2: Broker-Ready Monolith)

- Introduce a transport seam: `INotificationTransport` with `ChannelTransport` (current) and `BrokerTransport` (adapter for RabbitMQ/SQS/etc.).
- Publisher emits a message referencing the outbox id (and minimal metadata) after DB insert; Channel still used in development or as a fallback.
- Dispatcher can run in-process or as a separate worker executable in the same repo, consuming from the selected transport.

## Optional Extraction (Stage 3: Dedicated NotificationsService)

- Run the dispatcher as a separate service/deployment consuming from the broker. Keep API enqueue unchanged (still writes outbox and publishes event).
- Add a DLQ and replay tooling; add dashboards/alerts.
- Optional SendGrid webhook processor to update provider delivery status fields on outbox rows.

## Contracts and Data Shapes

- Message contract (publish):
  - type: string (e.g., "notification.queued")
  - id: uuid (notification outbox id)
  - occurredAt: ISO timestamp
  - tenantId?: uuid
  - kind: string (Verification | Invite | …)
  - dedupeKey?: string (hashed)

- Outbox row (source of truth): see Notif-13 design — includes PII-minimized fields, token_hash, status/attempts, and timestamps.

## Idempotency & Dedupe

- Enqueue-side: partial unique constraint on dedupe_key for active window (Queued/Sending/Sent within TTL) with friendly error; dedupe keys use hashed email + pepper; tokens stored as hashes.
- Consumer-side: processing by outbox id within a transaction; status checks prevent double-send; resend flows use derived dedupe keys to intentionally bypass dedupe while enforcing throttles.

## Retry, Backoff, and DLQ

- Retry schedule with +/-20% jitter (e.g., 0.5s, 2s, 8s, then DeadLetter).
- On final failure: mark DeadLetter and persist last_error; emit metric and structured log with correlation ids.
- With broker: configure per-queue dead-letter exchange or use per-message TTL + DLQ routing keys.

## PII and Security

- Minimize stored fields; avoid persisting full HTML bodies unless needed. If stored, encrypt at rest.
- Hash tokens and use hashed emails for dedupe keys; redact emails in logs (k\*\*\*@domain.com).
- Access control for admin endpoints; tenant scoping; retention and purge job.
- Secrets management for broker credentials and SendGrid API key; TLS for all transports.

## Observability

- Metrics: email.queued.total, email.sent.total, email.failed.total, email.deadletter.total, email.resend.total (by kind/reason).
- Traces around enqueue→dispatch→send with correlation ids (notificationId, originalId for resends, tenantId, userId).
- Logs redact PII and include status transitions.

## Rollout & Rollback

- Feature flags for transport selection (channel vs broker) and for field-level encryption.
- Safe migration path: deploy outbox first; once stable, introduce transport seam; only then adopt a broker (no contract changes).
- Rollback: switch transport back to ChannelTransport; dispatcher in-process continues to work; outbox remains authoritative.
  \n---\n\nSee the migration sprint plan at `devInfo/PreMigrate/migSprintPlan.md`.
