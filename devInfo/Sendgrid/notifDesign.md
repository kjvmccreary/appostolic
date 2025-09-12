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

Status: Planned
Owner: Engineering
Last updated: 2025-09-12
