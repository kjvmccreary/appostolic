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

## Notif-09 — Observability (metrics + logs)

Summary

- Add OTEL counters/gauges and structured logging enrichment.

Acceptance Criteria

- Metrics: emails_sent_total, emails_failed_total
- Logs include correlation fields (userId, tenantId, inviteId)

Key Tasks

- Add metric instruments and log scopes

## Notif-10 — Docs and environment wiring

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

## Notif-11 — E2E dev verification (Mailhog)

Summary

- Verify an end-to-end flow delivers an email to Mailhog.

Acceptance Criteria

- Local run with provider=smtp sends email captured by Mailhog with expected subject/link

Key Tasks

- Add a tiny dev endpoint or test hook to enqueue a sample message and manually verify

## Notif-12 — Hardened error handling and dedupe

Summary

- Add jittered backoff, dead-letter logging, and optional dedupe key on enqueue.

Acceptance Criteria

- Dispatcher uses jitter; final failure logs a dead-letter event with message key
- Optional dedupe mechanism prevents duplicate sends across restarts when key unchanged

Key Tasks

- Implement jitter, dead-letter log; design simple dedupe key pattern

---

Suggested order: Notif-01 → Notif-02 → Notif-03 → Notif-04 → Notif-05 → Notif-06 → Notif-07 → Notif-08 → Notif-09 → Notif-10 → Notif-11 → Notif-12
