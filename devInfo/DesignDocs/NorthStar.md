# Product Vision & Development Plan (Appostolic)

> **Purpose**  
> Give the team a single, durable source of truth for _what_ we’re building, _why_, and _how_ the current sprint plan maps to that vision. This complements `docs/DEV_ONBOARDING.md`, **Project Files — Structure & Context**, and **Project Knowledge — Sprints & Agent Prompts**.

---

## 1) Vision

**Appostolic** is a Jesus-forward lesson builder for Sunday School teachers, Bible study leaders, and preachers. It produces age-appropriate, time-bounded lesson plans (and supporting materials) that reflect the user’s Christian tradition/denomination while ensuring strong theological guardrails. It is _assistive_, not authoritative: teachers stay in control—editing, annotating, and choosing deliverables.

**North-star outcome**: a teacher can go from topic → classroom-ready lesson (outline, notes, optional handout/slides) in under **10 minutes**, with doctrinal alignment and safety by default.

**Key principles**

- **Jesus-forward**: Never promote conversion away from Christian belief. Descriptive comparisons of other religions are allowed; promotional content is not.
- **Denominational alignment**: Content should reflect the user’s selected tradition (e.g., Baptist, Methodist) and be configurable by tenant admins.
- **Teacher-first UX**: Clear controls for duration, age group, references, tone, and deliverables; a rich editor for last-mile tweaks (Syncfusion doc editor).
- **Safety & respect**: Respect privacy; avoid per-student sensitive profiling. Use aggregated class context only when necessary and with consent.
- **Observability & accountability**: Transparent guardrails, explainability on allow/deny, and versioned policy snapshots.

---

## 2) Target users & jobs to be done

- **Solo Teacher / Small Group Leader** (Free/Pro): “I need a faithful, 60-minute lesson plan for youth on Romans 8 by Sunday.”
- **Church Staff / Ministry Teams** (Org): “We need uniform guardrails across ministries, shared content, and seat management.”
- **Curriculum Director** (Org): “I must ensure doctrinal consistency, approve changes, and view usage/costs.”

---

## 3) Core product capabilities (MVP → v1)

1. **Lesson generation** (topic → outline, teacher notes; optional handout/slides).
2. **Guardrails** (system + tenant overrides + user-level prefs) and **denomination profiles** (doctrinal presets).
3. **Deliverables**: Bible references, printable handouts (PDF), slides (HTML/PPTX), optional media links. Devotionals may output journaling PDFs, guided prayer sheets, or audio devotionals.
4. **Storage & versioning**: artifacts in object storage; policy/config snapshots versioned.
5. **Multi-tenant & roles**: Owner/Admin/Creator/Approver/Learner (hardcoded roles, tenant-scoped); invites; org-grade controls.
6. **Usage metering & plans**: Free/Pro/Org with quotas and cost transparency.
7. **Interactive Learning (Quizzes & Trivia)**: AI-generated age-appropriate, doctrinally aligned quizzes/trivia as deliverables. Learner completion tracking + teacher recognition (gold stars).
8. **Observability & audit**: Structured JSON logs, OTel traces/metrics, tenant-scoped audit ledger.
9. **Platform Ops & SuperAdmin**: Cross-tenant monitoring, governance, configuration.
10. **Compliance & Policy**: Data export/erasure, retention, open records support.

> **Future-leaning**: Video companion generation (TTS + slides to video, or third-party service), Marketplace for self-published curricula, advanced gamification for learners, UI Builder/Dynamic Forms.

---

## 4) Safety & ethics

- **Prohibited**: Content that promotes or instructs conversion away from Christianity (e.g., to Mormonism, Jehovah’s Witnesses, Islam).
- **Allowed with context**: Descriptive/explanatory material about other religions for comparison or education.
- **Sensitive attributes**: Avoid storing or inferring protected characteristics at the individual level (gender, race, household). If class context is used, prefer optional, aggregated inputs with clear consent and purpose, and store minimally.
- **Explainability**: Guardrails engine returns _why_ a prompt is allowed/denied with matched rules.

---

## 5) Architecture snapshot (current)

_(Summarized from `SnapshotArchitecture.md` and current repo state.)_

- **Monorepo**: `apps/api` (ASP.NET Core + EF Core, Postgres/pgvector), `apps/web` (Next.js), `apps/mobile` (Expo/React Native), `packages/sdk` (TS client), `infra/docker` (Compose: Postgres, Redis, MinIO, Mailhog, Qdrant, pgAdmin).
- **Auth**: Username/password + Magic Link (MVP). OAuth (Google, Microsoft/Entra ID, Facebook) planned.
- **Tenant scoping**: Multi-tenant with per-user roles. Users can belong to multiple tenants with different roles.
- **Lesson Pipeline**: Job submission, guardrails, async worker, refinement loop, DocGen integration.
- **DocGen**: Syncfusion editor, Markdown→PDF, Reveal.js/PPTX slides; future media generation.
- **Notifications**: Email verification + invites via SMTP/SendGrid outbox.
- **Observability**: OTel traces/metrics, structured logging, tenant audit ledger.
- **Usage & Billing**: Usage metering, plan tiers, Stripe integration (post-MVP for billing UI).
- **SuperAdmin**: Cross-tenant monitoring, config, audit access.

---

## 6) Development plan ↔ vision mapping

This shows how sprints deliver the vision in safe increments.

### ✅ Completed (foundation & first surface)

- **S1-07** — Idempotent seed; EF unification; DB init; make targets.
- **S1-08** — Dev auth; per-request tenant scope; minimal endpoints; Swagger + TS SDK; web demo page.
- **Auth-01 → Auth-05** — Core user/tenant schema, signup, login (username/password + magic link), tenant switching, hardcoded roles (Owner/Admin/Creator/Approver/Learner).
- **Auth-06 → Auth-09** — Members list, invite API/email, invite acceptance, member role management.
- **Notif-07/08** — Verification and invite email enqueuer + templates.
- **Notif-13 → Notif-15, 17, 18** — Notifications outbox, dispatcher, retries, dedupe, retention.
- **Auth-10, Auth-11** — Proxy header mapping & role-based route protection.

### 🔜 Sprint 1.9 — Guardrails & Denomination Profiles (Safety core)

- **Schema & RLS** for system/tenant/denomination policies.
- **Evaluator service + Preflight API** (allow/deny with reasons).
- **Admin UI** for policy view/toggles; MinIO snapshots; tests & fixtures.

### 🔜 Sprint 1.10 — Generation Pipeline v1 (Value core)

- **Job API**: submit lesson job (duration, age group, deliverables).
- **Worker**: Redis queue, provider abstraction, writes outputs to MinIO, updates DB.
- **Deliverables builders**: Handout (Markdown→PDF), Slides (Reveal/HTML or PPTX).
- **Usage metering** and `/api/usage` (owners).

### 🔜 Sprint 1.11 — Mobile Readiness & SDK Everywhere

- **Upgrade Expo** to SDK 54.
- **Use `@appostolic/sdk`** in mobile for `/me`, `/tenants`, `/lessons`.
- **List/detail** screens for generated artifacts.

### 🔜 Sprint 1.12 — Plans, Billing, Seats (Go-to-market)

- **Stripe** integration & webhooks.
- **Entitlements & quotas** middleware + `/api/entitlements`.
- **Seat invites & roles**; align RLS and org UX.

### 📌 Backlog themes

- **Content Safety v2** (multi-pass moderation, red-team logs, appeals).
- **Video generator MVP** (TTS + slides to video or third-party).
- **Observability** (OTel, metrics, admin console).
- **Internationalization** (scripture translation variants, locales).
- **Personalized Devotionals** (daily reflections, journaling, guided prayers, family devotionals; Thematic Prayer included).
- **Self-Published Curricula Marketplace** (lessons, devotionals, curricula packs; monetization; tenant licensing).
- **Interactive Learning (Quizzes & Trivia)** (AI-generated quizzes/trivia; MVP = completion tracking + teacher recognition; Post-MVP = gamification, badges, leaderboards).

---

## 7) Pricing & usage model (directional)

- **Free**: limited jobs/month, handout only, small storage; community support.
- **Pro**: higher limits, slides, snapshots, priority queue; optional team of 2–3.
- **Org**: pooled seats, org guardrails, shared content library, audit logs, SSO (phase 2), priority generation.
- **Metering**: per-job/provider token counts and cost capture for transparency and forecasting.

---

## 8) Metrics & success criteria

- **Time-to-first-lesson** (TTFL) < 10 minutes (p95).
- **First job success rate** (> 90% no manual retries).
- **Guardrails precision/recall** on test prompts.
- **Edit distance**: % of lessons that need heavy edits (↓ over time).
- **Activation**: % of users who create ≥2 lessons in first week.
- **Cost/lesson** within plan margins.

---

## 9) Operational posture

- **One-command onboarding** (`make bootstrap`) and CI _dev-sanity_ to prevent drift.
- **Config as data**: versioned guardrails in MinIO; rollback support.
- **Incident-light dev**: local Compose + idempotent seed; simple diagnostics (`scripts/dev-doctor.sh`).
- **System logging & observability**: Structured JSON logs across all services (Serilog). OpenTelemetry traces + metrics. Tenant-scoped audit ledger for tenant admins; full system logs restricted to SuperAdmin. PII redaction by default. Retention policy (7d hot, 30d warm, 180d cold). Alerting & dashboards per pillar. Log access is itself audited.

---

## 10) What’s in scope vs out of scope (MVP)

**In**: Lesson generation, guardrails & denomination presets, handouts/slides, storage, multi-tenant basics, usage metering, basic plans, basic quizzes/trivia with completion tracking, responsive web app.  
**Out (for now)**: Fine-grained per-student personalization; public lesson marketplace; advanced analytics; SSO (post-MVP for Org); media generation; gamification; advanced integrations.

---

## 11) Cross-doc references

- `docs/DEV_ONBOARDING.md` — developer setup.
- **Project Files — Structure & Context** — folder layout & locations.
- **Project Knowledge — Sprints & Agent Prompts** — task-level execution details.
- `SnapshotArchitecture.md` — environment snapshot (kept up to date per sprint).

---

## 12) Decision log (abridged)

- **.NET 8** for API/worker (LTS stability now; revisit .NET 9 later).
- **pgvector image** with `initdb` (extensions + schema) and **GUC-based tenant scope**.
- **Header-auth in dev**; real auth to follow during Org tier.
- **SDK generation** to keep web/mobile in lockstep with the API.

---

## 13) Next concrete steps

1. **S1-09** guardrails schema + evaluator + admin UI.
2. **S1-10** job/worker + deliverables builders.
3. **S1-11** Expo 54 + mobile SDK adoption.
4. **S1-12** Stripe + entitlements + invites.

> When this plan shifts, update this document _and_ the Sprints doc so vision ↔ delivery stay aligned.
