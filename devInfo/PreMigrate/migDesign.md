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

---

# SprintPlan — Migration (Mig##)

Each story is scoped to be independently testable and reversible. Prefix: Mig##.

## Mig01 — Introduce transport abstraction

Summary

- Define `INotificationTransport` and provide `ChannelTransport` implementation. No behavior change.

Dependencies

- Existing notifications code.

Acceptance Criteria

- Enqueuer depends on `INotificationTransport`; ChannelTransport preserves current behavior.
- Tests updated to use a fake transport.

Tasks

- Interface + DI wiring; adapter for existing Channel.
- Unit tests; minimal refactor in enqueuer/dispatcher.

## Mig02 — DB outbox publisher integration

Summary

- After inserting to outbox, publish a "notification.queued" message containing the outbox id via the transport (still ChannelTransport by default).

Dependencies

- Notif-13..15 (outbox); Mig01.

Acceptance Criteria

- New outbox rows result in a transport publish; ChannelTransport wakes dispatcher.
- Backward compatible and idempotent.

Tasks

- Add publisher call in enqueue path; include minimal metadata.
- Tests for publish-on-insert behavior.

## Mig03 — External worker executable (same repo)

Summary

- Create a worker process (console host) that hosts the dispatcher and can run separately from the API.

Dependencies

- Mig01–02.

Acceptance Criteria

- Worker can run locally to consume from ChannelTransport; API still works if worker is down (Channel in API remains a fallback).

Tasks

- New project or entrypoint; shared DI composition; README/RUNBOOK notes.

## Mig04 — Broker adapter (RabbitMQ/SQS) behind flag

Summary

- Implement `BrokerTransport` adapter with configuration gates; keep default as ChannelTransport.

Dependencies

- Mig01–03.

Acceptance Criteria

- With feature flag enabled and broker configured, publisher emits messages to broker; consumer (worker) can consume them.
- Disable flag → revert to Channel.

Tasks

- Implement publish/subscribe; connection lifecycle; health checks.
- Minimal retries and backoff; unit/integration tests (local broker or testcontainers).

## Mig05 — DLQ and replay tooling

Summary

- Add dead-letter queue support and a simple replay tool/endpoint.

Dependencies

- Mig04.

Acceptance Criteria

- Messages that exceed retry policy route to DLQ; replay command can move them back to the main queue.

Tasks

- Broker config for DLQ; CLI or dev endpoint to replay; RUNBOOK instructions.

## Mig06 — Observability for broker path

Summary

- Extend metrics/logs/traces for broker latency, consumer lag, DLQ depth.

Dependencies

- Mig04–05.

Acceptance Criteria

- New metrics exposed; logs include transport type; traces correlate publish→consume→send.

Tasks

- Add meters/counters; structured logs; OTEL spans.

## Mig07 — PII hardening in transport path

Summary

- Ensure published messages carry only minimal metadata (outbox id, kind, tenantId optional) and no PII.

Dependencies

- Mig01–04; Notif-21 (PII minimization).

Acceptance Criteria

- No email addresses or tokens in broker payloads; policies/tests enforce this.

Tasks

- Schema review; payload guards; tests.

## Mig08 — Rollout plan and fallback

Summary

- Document and script safe rollout toggles and fallback to ChannelTransport.

Dependencies

- All prior Mig stories.

Acceptance Criteria

- RUNBOOK section with step-by-step rollout, verification checks, and rollback switch.

Tasks

- Write RUNBOOK playbook; add feature flag toggles; smoke tests.
