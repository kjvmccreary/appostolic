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
