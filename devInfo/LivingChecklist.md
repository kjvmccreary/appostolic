# Appostolic — Living Checklist (v1.0 Go‑to‑Market)

Purpose

- A single, living, checkable list of what must be true for the 1.0 launch. Keep it up to date as features land. Update this file when closing a story and at sprint close.
- When you check off an item here, also update:
  - SnapshotArchitecture.md (architecture/source of truth)
  - devInfo/storyLog.md (append the story summary)

Legend

- [ ] = not done
- [x] = done
- ➕ = post‑1.0 candidate (tracked but not required for 1.0)

How to use

- Check items as they are completed. Add links or short notes after items when helpful (e.g., PR, commit hash).
- If scope changes, move an item to Post‑1.0 with a brief note and the sprint where it moved.

---

## Global quality gates (always on)

- [x] API build green; tests passing (unit/integration); EF migrations applied
- [x] Web typecheck/lint green; tests passing; coverage thresholds satisfied
- [x] Accessibility basics upheld (labels, focus, aria-current/expanded, color contrast)
- [x] Server‑first auth/role guards for any new routes (avoid client-only gating) — Tenant settings & logo endpoints added with TenantAdmin policy
- [x] Docs updated: SnapshotArchitecture.md, devInfo/storyLog.md, and this LivingChecklist

---

## Release 1.0 pillars and checkpoints

### IAM (Users, Tenants, Roles, Invites)

- [x] Username/password auth (argon2id) and session
- [x] Magic Link (request/consume) with hashed tokens and email
- [x] Invitations with Roles flags (TenantAdmin, Approver, Creator, Learner) and acceptance page
- [x] Role policies enforced; uniform 403 ProblemDetails; last‑admin invariant (409) with audit trail
- [ ] OAuth SSO (Google, Microsoft) — ➕ Post‑1.0
- [ ] Tenant settings page (basic org info, feature toggles) — In 1.0 scope

### Tenant & Org Management

- [x] Tenant creation and membership flows (personal + invited)
- [x] Role management UI (flags) with last‑admin guard surfaced
- [ ] Basic Tenant settings UI — In 1.0 scope

### Guardrails & Denomination Profiles

- [ ] Policy schema + RLS (system/tenant/denomination levels)
- [ ] Evaluator + preflight API (allow/deny with “why”)
- [ ] Admin UI to view/edit policies and apply presets
- [ ] Versioned policy snapshots (object storage)

### Lesson Generation Pipeline

- [ ] Job submission endpoint (topic, audience, duration, deliverables)
- [ ] Async worker with provider abstraction; iterative refinement loop
- [ ] Deliverables builders integrated with jobs lifecycle

### Deliverables (DocGen, Slides)

- [ ] Handouts (Markdown → PDF) with asset storage and signed URLs
- [ ] Slides (HTML/Reveal or PPTX) with asset storage and signed URLs

### Storage & Versioning

- [x] Object storage wiring (MinIO/S3) seam for avatars/logos (UPROF-09) — lesson artifacts will reuse seam
- [ ] Version history (per lesson, per deliverable)

### Notifications & Messaging (Email)

- [x] Outbox + dispatcher with retry/backoff; SMTP (dev) and SendGrid (prod)
- [x] Verification and Invite emails (HTML), privacy/PII guardrails
- [x] Resend (manual/bulk/auto) + DLQ list/replay; transport channel/redis + external worker

### Usage Metering, Plans & Entitlements

- [ ] Per‑job usage metering (token/cost capture)
- [ ] Entitlements middleware and `/api/entitlements`
- [ ] Plan tiers surfaced in UI (Free/Pro/Org) with usage/quotas
- [ ] Stripe integration + webhooks; Billing/Invoices UI

### Observability & Audit

- [x] OTel traces/metrics and structured logs with privacy gates
- [x] Tenant audit ledger (e.g., membership role changes) with admin UI
- [ ] Admin dashboards for ops — ➕ Post‑1.0

### Responsive Web UX (Teacher/Admin)

- [x] Theming (Light/Dark/AMOLED), sticky TopBar, mobile nav drawer, accessibility baselines
- [x] Admin: Members, Invites, Audits, Notifications (DLQ)
- [x] Studio: Agents (CRUD), Tasks inbox/detail (cancel/retry/export)
- [ ] Admin: Guardrails page (policy config)
- [ ] Admin: Billing/Usage pages

### Interactive Learning (Quizzes & Trivia)

- [ ] Basic quizzes/trivia generation and completion tracking (1.0)
- ➕ Gamification (badges, streaks, leaderboards) — Post‑1.0

### Platform Ops & Safety

- [x] Makefile bootstrap (up, migrate, seed); dev doctor scripts; health pages
- [ ] Feature flag admin (central toggles) — ➕ Post‑1.0

---

## Process & docs (keep in sync)

- [x] After each story: append `devInfo/storyLog.md` (same summary as PR/assistant), update `SnapshotArchitecture.md`, and tick items here
- [ ] At sprint close: review gaps, move deferrals to Post‑1.0, and ensure this checklist reflects reality

## Post‑1.0 candidates (parking lot)

- SSO providers (Google/Microsoft/Entra)
- Search & Retrieval across lesson/devotional content
- Media generation (images, audio, video)
- Commerce Admin (refunds, credits, dunning)
- Tenant analytics dashboards
- Marketplace for self‑published curricula
- UI Builder / Dynamic Forms
- Advanced compliance & policy (exports/erasure)

## Refactor Backlog (internal quality improvements)

These are non-functional refactors we intend to tackle when touching adjacent code or before 1.0 hardening. They are safe to defer short-term.

- [ ] Consolidate duplicated DeepMerge logic (UserProfileEndpoints & TenantSettingsEndpoints) into `Application/Json/JsonMerge.cs` + unit tests (null clearing, array replacement, nested creation). (Added 2025‑09‑16)
- [ ] Introduce depth/size guard in shared DeepMerge to mitigate pathological nested payloads (evaluate after consolidation).
- [ ] Replace best-effort swallow on object delete (logo/avatar) with structured logging + optional retry policy once background cleanup job exists.

- Last updated: 2025‑09‑16 (Added UPROF-05/06 profile & guardrails forms, UPROF-08 change password UI; prior: TEN-01/TEN-02, UPROF-07, UPROF-09, Node 20 testing)
