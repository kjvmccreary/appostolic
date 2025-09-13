# Appostolic — Core Pillars (High‑Level Architecture)

> Purpose: Define the durable domains (“pillars”) of Appostolic as independent, composable capabilities that will eventually map to discrete microservices. **This document intentionally avoids MVP scope and delivery sequencing**. It focuses on boundaries, responsibilities, ownership, and interactions across the web/API stack you’re building.

---

## 0) Scope & Non‑Goals

- **Scope**: Web + API architecture; internal runtime and data boundaries; cross‑pillar contracts. Mobile is out‑of‑scope here.
- **Non‑Goals**: Sprint planning, MVP cut lines, cost estimates, vendor selection minutiae.

---

## 1) Architecture Principles (applies to all pillars)

- **Tenant‑first**: Every read/write flows through tenant context (RLS in Postgres + middleware). No cross‑tenant joins.
- **Config‑as‑data**: Policies, templates, and forms are data, versioned, and auditable.
- **Event‑friendly**: Pillars publish domain events; consumers react asynchronously where appropriate.
- **Fail‑soft**: Non‑critical pillars (e.g., Notifications) degrade gracefully without blocking core flows.
- **Clear ownership**: Each pillar owns its authoritative data and APIs; other pillars integrate via contracts, not DB reach‑ins.
- **Observability**: Traces, metrics, and logs are first‑class.

---

## 2) Pillars (Bounded Contexts)

### 2.1 Identity & Access (IAM)

**Purpose**: Authenticate users and authorize actions within a tenant context.

**Owns**

- Users, Credentials (Argon2id), Sessions, Password reset tokens
- Tenants, Memberships, Roles/Permissions
- Invites (issue/accept), Email verification

**Provides**

- Auth endpoints (signup, login, logout, invite accept)
- Tenant selection/switching semantics & claims
- Role checks (Owner/Admin/Member) and policy hooks

**Consumes**

- Notifications (for verification/invites)
- Audit/Observability (login events, policy decisions)

**Key events**

- `UserRegistered`, `InviteSent`, `InviteAccepted`, `PasswordChanged`, `TenantSelected`

**Boundaries**

- No storage of PII beyond necessity; email as citext; token hashing; rotation windows.

#### 2.1.1 Authentication Providers

**Supported Methods**

- **Email/Password** — traditional local accounts (hashed via Argon2id).
- **Magic Link** — passwordless login via one‑time email link (delivered through Notifications outbox).
- **Federated Login (OIDC/SAML)**
  - **Google** — personal + Workspace accounts.
  - **Microsoft / Entra ID** — critical for large orgs; supports per‑tenant configuration and Just‑In‑Time provisioning.
  - **Apple** — for iOS users / App Store compliance.
  - **Facebook** — optional, for broader consumer reach.

**Provider model**

- All providers resolve to a single `users` row; credentials/federated identities linked via `external_identities` table.
- Memberships and hardcoded roles (Owner, Admin, Creator, Approver, Learner) enforced through IAM/Tenant Mgmt, independent of login method.
- Future: SCIM + advanced SSO features (group → role mapping).

---

### 2.2 Tenant & Org Management

**Purpose**: Organizational container and controls for settings that span a tenant.

**Owns**

- Tenants (profile, denomination selection), Org settings
- Seat counts, roles model (coordination with IAM)
- Feature flags & entitlements (read model fed by Billing)

**Provides**

- Admin surfaces for members, roles, seat allocation
- Effective settings resolution (org → ministry → user overrides)

**Consumes**

- Billing/Entitlements (plan → feature gates)
- Guardrails/Doctrine Profiles (selected profile + overrides)

**Key events**

- `TenantCreated`, `SeatAllocated`, `SeatRevoked`, `SettingsChanged`

---

### 2.3 Guardrails & Doctrine Profiles

**Purpose**: Policy engine that ensures generated content adheres to denominational and safety constraints.

**Owns**

- System policies, Denomination profiles, Tenant overrides, **User-level preferences/guardrails**
- Versioned policy snapshots & explainability artifacts

**Provides**

- Preflight evaluation (`allow`/`deny` + reasons)
- Effective policy view (system ⊕ profile ⊕ tenant overrides ⊕ user preferences)

**Consumes**

- Tenant selection (profile binding)
- Observability (policy hit stats)

**Key events**

- `PolicySnapshotCreated`, `PolicyEvaluated`

**Boundaries**

- Deterministic evaluation with JSON‑schema validation of policies; no hidden rules.

---

### 2.4 Lesson Intent & Generation Pipeline

**Purpose**: Turn a user’s intent (topic, age group, timebox, deliverables) into structured lesson artifacts.

**Owns**

- Job submissions, Provider abstraction, Prompt templates
- Generation traces/metrics (non‑PII), Error taxonomy
- **Support for iterative refinement loops** (additional prompts to refine existing lesson rather than create new)

**Provides**

- `POST /jobs/lessons` (idempotent), job status/results
- Hooks for Guardrails preflight, retries, and provider selection
- API to continue/refine an existing job (prompt chaining)

**Consumes**

- Guardrails preflight (hard gate)
- Storage & Versioning (artifacts)
- DocGen/Slides/Media (rendering)
- Observability (latency, success rate)

**Key events**

- `LessonJobSubmitted`, `LessonGenerated`, `LessonFailed`, `LessonRefined`

**Boundaries**

- Provider‑agnostic; cost/tokens recorded but detached from Billing logic.

---

### 2.5 DocGen, Deliverables & Media (Handouts/Slides/Media)

**Purpose**: Transform structured lesson content into final deliverables (Markdown→PDF, Slides→HTML/PPTX) and complementary media (images, audio, video).

**Owns**

- Render pipelines, Templates/themes, Export settings
- Media generation adapters (e.g., text-to-image, text-to-audio, TTS+slides to video)
- Optional redlining/track‑changes integration points

**Provides**

- Rendering APIs (synchronous for small, async for large)
- Theme pack catalog & preview endpoints
- Media generation APIs

**Consumes**

- Storage & Versioning (read/write artifacts)
- Lesson Pipeline (source content)

**Key events**

- `HandoutRendered`, `SlidesRendered`, `MediaGenerated`

**Boundaries**

- Pure rendering/generation; no business decisions (guardrails live elsewhere).

---

**Purpose**: Transform structured lesson content into final deliverables (Markdown→PDF, Slides→HTML/PPTX).

**Owns**

- Render pipelines, Templates/themes, Export settings
- Optional redlining/track‑changes integration points

**Provides**

- Rendering APIs (synchronous for small, async for large)
- Theme pack catalog & preview endpoints

**Consumes**

- Storage & Versioning (read/write artifacts)
- Lesson Pipeline (source content)

**Key events**

- `HandoutRendered`, `SlidesRendered`

**Boundaries**

- Pure rendering; no business decisions (guardrails live elsewhere).

---

### 2.6 Storage & Versioning

**Purpose**: Durable object storage for artifacts and versioned configuration snapshots.

**Owns**

- Objects (handouts, slides, policy snapshots), Metadata
- Retention policies & lifecycle management

**Provides**

- Signed URL issuance (read/write), Version listings, Integrity checks

**Consumes**

- DocGen/Deliverables (writes), Guardrails (snapshots), Lesson Pipeline (reads)

**Key events**

- `ObjectStored`, `VersionCreated`, `ObjectPurged`

**Boundaries**

- Immutable by default; redactions handled via new versions + pointers.

---

### 2.7 Search & Retrieval

**Purpose**: Index and retrieve prior lessons, artifacts, and references for reuse and cross‑lesson context.

**Owns**

- Vector indexes (titles, outlines), Metadata catalogs

**Provides**

- Search APIs (keyword + semantic), `related-to:<topic>` lookups

**Consumes**

- Storage (artifact bytes/links), Lesson Pipeline (summaries)

**Key events**

- `ArtifactIndexed`, `IndexRebuilt`

**Boundaries**

- No cross‑tenant leakage; per‑tenant indexes only.

---

### 2.8 Notifications & Messaging

**Purpose**: Outbound communications (email now; in‑app/messaging later) with durability and privacy.

**Owns**

- Email outbox (DB), Templates, Delivery attempts/metrics

**Provides**

- Enqueue APIs (`verification`, `invite`, system alerts), Admin listing & retry

**Consumes**

- IAM (verification/invites), Lesson Pipeline (completion notices)

**Key events**

- `EmailQueued`, `EmailSent`, `EmailDeadLettered`

**Boundaries**

- PII minimization; subject/body snapshots retained with retention windows.

---

### 2.9 Usage Metering, Plans & Entitlements

**Purpose**: Track usage, map plans to entitlements, and enforce quotas.

**Owns**

- Usage records (per job/provider), Plan catalogs, Entitlement tables

**Provides**

- Read model for feature gates (`canUseSlides`, `jobQuotaRemaining`)
- Admin usage endpoints & exports

**Consumes**

- Billing (plan changes), Lesson Pipeline (usage emits)

**Key events**

- `UsageRecorded`, `PlanChanged`, `QuotaBreached`

**Boundaries**

- Enforcement via middleware and read‑model checks; no direct Stripe coupling here.

---

### 2.10 Billing & Payments (External-integrated)

**Purpose**: Monetize Appostolic with subscriptions and seat management; integrate with Stripe.

**Owns**

- Local mirror of Stripe objects necessary for runtime (customer, subscription status, seats)

**Provides**

- Webhook handlers, Admin subscription views, Seat provisioning hooks

**Consumes**

- Entitlements (to update gates), IAM (seat enforcement)

**Key events**

- `SubscriptionActivated`, `SubscriptionLapsed`, `SeatCountUpdated`

**Boundaries**

- Least-data mirroring; sensitive data stays with Stripe.

---

### 2.11 Commerce Admin (SaaS Operations)

**Purpose**: Provide internal/admin tools to manage the commercial side of Appostolic as a SaaS platform.

**Owns**

- Admin console for subscriptions, invoices, payments, and account lifecycle
- Adjustments (comp/free plans, credits, refunds)
- Customer support actions (manual seat changes, account freezes/cancellations)
- Reporting datasets (MRR, churn, cohort analysis)

**Provides**

- Secure back-office UI for staff
- APIs for admin‑initiated plan/seat overrides
- Exports for finance/accounting

**Consumes**

- Billing & Payments (subscription/invoice data)
- Entitlements (to apply overrides)
- Tenant Mgmt (org profile, seats)

**Key events**

- `AdminPlanOverride`, `RefundIssued`, `InvoiceAdjusted`

**Boundaries**

- Restricted to SuperAdmin/staff roles; strong audit trails required.

---

### 2.11 Observability & Audit

**Purpose**: Make the system transparent and diagnosable for engineers and admins.

**Owns**

- Trace spans, Metrics, Structured logs, User‑visible audit ledger

**Provides**

- Admin console (health, queues, errors), Export endpoints for audits

**Consumes**

- All pillars emit traces/metrics; IAM feeds user/audit context

**Key events**

- `AuditEntryRecorded`, `AnomalyDetected`

**Boundaries**

- Redaction rules for PII; tenant‑scoped queries by default.

---

### 2.12 Admin & Configuration

**Purpose**: Centralize feature flags, environment configuration, and platform‑wide toggles.

**Owns**

- Feature flags, Runtime configuration, Maintenance windows

**Provides**

- Safe‑by‑default toggles, Readonly config surfaces for support

**Consumes**

- All pillars for flag checks and config retrieval

**Key events**

- `FlagChanged`, `MaintenanceScheduled`

---

### 2.13 Agents Runtime (Research & Automation)

**Purpose**: Define and orchestrate agents with tool allowlists for research/automation tasks.

**Owns**

- Agent definitions, Tasks, Traces, Tool registry

**Provides**

- Task orchestration, Trace export, Deterministic dev tools

**Consumes**

- Search (knowledge), Storage (artifacts), Observability

**Key events**

- `AgentTaskCreated`, `AgentTaskCompleted`

**Boundaries**

- Tool allowlist enforced per agent; tenant isolation for data access.

---

### 2.14 Roles & Collaboration (Hardcoded Workflow)

**Purpose**: Support large organizations with simple, predefined role-based workflows for creation, review, and approval of lesson plans.

**Owns**

- Hardcoded roles: Creator, Approver, Admin, Learner (students)
- Role semantics and enforcement logic

**Provides**

- Role checks integrated into IAM and Tenant Management
- Collaboration primitives: assign Creator, require Approver sign-off, Learner access to pre-lesson activities

**Consumes**

- IAM (memberships and claims)
- Notifications (review/approval requests)

**Key events**

- `LessonSubmittedForReview`, `LessonApproved`, `LessonRejected`

**Boundaries**

- Not a full workflow engine; roles are hardcoded and non-configurable.

---

### 2.15 Lesson Sharing & Marketplace

**Purpose**: Enable users to share lesson plans within the community, either free or paid.

**Owns**

- Marketplace listings, pricing, licensing metadata
- Transaction records (when monetization is enabled)

**Provides**

- Public/tenant-scoped catalogs of lessons
- Listing creation/review APIs

**Consumes**

- Storage (artifacts), Billing (optional payment integration)

**Key events**

- `LessonPublished`, `LessonPurchased`

**Boundaries**

- Tenant isolation rules enforced; PII stripped from shared content.

---

### 2.14 Platform Ops & Cross‑Tenant Analytics (SuperAdmin)

**Purpose**: Give the business a secure, aggregate view across **all tenants** for platform health, adoption, and financials—without violating tenant isolation.

**Owns**

- SuperAdmin role & access policies
- Aggregated usage warehouse (append‑only facts sourced from Usage/Observability/Billing) with strong PII minimization
- Operational dashboards (activation, TTFL, success rate, cost/lesson, churn indicators)

**Provides**

- Read‑only analytics UI and export endpoints
- Alerting on anomalies (e.g., error spikes, runaway costs)

**Consumes**

- Usage Metering (facts), Billing (subscription states), Observability (metrics), Auth (tenant/user counts)

**Key events**

- `AnalyticsSnapshotPublished`, `AnomalyAlertRaised`

**Boundaries**

- No raw tenant data exposure; analytics derived from denormalized facts with tenant‑level aggregation. Access restricted to SuperAdmin only.

---

### 2.15 Mobile Experience & Sync (Client Pillar)

**Purpose**: Define platform guarantees that enable high‑quality mobile experiences, even though mobile itself isn’t a microservice.

**Owns**

- Client contracts for auth/tenant switching, entitlement checks, and artifact access (signed URLs)
- Offline‑friendly patterns (caching envelopes, background refresh hooks)
- Push notification bridge (via Notifications) and deep‑link conventions

**Provides**

- Stable API shapes & pagination suitable for constrained networks
- Guidance for offline caches and conflict policies (read‑mostly artifacts)

**Consumes**

- IAM (tokens/tenants), Entitlements, Storage (artifact reads), Notifications (push)

**Key events**

- `ClientSyncHintIssued`, `PushSent`

**Boundaries**

- No write‑heavy offline editing in MVP guidance; mobile follows platform security/PII policies.

---

### 2.16 Web Responsiveness (Cross‑device UI)

**Purpose**: Ensure the primary web app (`apps/web`) delivers a responsive, mobile‑friendly experience so that teachers and leaders can use the platform on phones or tablets without a native app.

**Owns**

- Responsive design system (MUI + Tailwind)
- Breakpoint definitions and reusable UI components
- Mobile‑first navigation patterns (drawer menus, collapsible panels)

**Provides**

- Seamless access to core lesson generation, editing, and sharing flows from a mobile browser
- Ensures parity of capabilities between desktop and mobile web during MVP

**Consumes**

- All platform APIs via `@appostolic/sdk`
- Notifications (via web push in browser)

**Boundaries**

- This is not a separate microservice; it is a design/development pillar guiding web client implementation.

---

### 2.17 Commerce Admin (Subscription Ops)

**Purpose**: Operate the business side of SaaS: subscription lifecycle, plan changes, refunds/credits, seat adjustments, and dunning—separate from tenant-facing admin.

**Owns**

- Admin UI for customer accounts (search, view, adjust)
- Operational policies (proration, grace periods, dunning cadence)
- Audit trail of commercial actions (who/when/why)

**Provides**

- Flows for upgrade/downgrade/cancel/reactivate
- Seat grants/revokes, comp codes, credits
- Reports (MRR/ARR deltas, cohort retention, expansion/contraction)

**Consumes**

- **Billing & Payments** (Stripe objects, invoices, refunds)
- **Entitlements** (to apply resulting gates)
- **Tenant Mgmt** (to reflect seat changes)
- **Notifications** (receipts, dunning, renewal reminders)
- **Platform Ops & Analytics** (exports/metrics)

**Key events**

- `SubscriptionUpgraded`, `SubscriptionDowngraded`, `RefundIssued`, `SeatsAdjusted`, `DunningTriggered`

**Boundaries**

- Commerce decisions flow through clear policies; sensitive payment data remains in Stripe; only mirrored IDs/states locally.

---

### 2.18 Integration & API Management

**Purpose**: Manage connections to external systems (church mgmt, LMS, calendars, comms apps) and provide safe external APIs.

**Owns**

- API keys, OAuth client credentials, rate limiting policies
- Webhook subscriptions and delivery logs
- Integration connectors (e.g., Planning Center, Rock RMS, Canvas, Teams/Slack)

**Provides**

- Outbound webhooks (lesson completed, invite accepted)
- REST/GraphQL APIs for tenant data access
- Integration health checks and retry policies

**Consumes**

- IAM (authN/authZ for API consumers)
- Notifications (webhook retries)

**Key events**

- `WebhookDelivered`, `APIKeyRotated`

**Boundaries**

- Strict tenant isolation; no cross-tenant leakage.

---

### 2.19 Content Library & Taxonomy

**Purpose**: Curated, reusable lesson components, scripture indexes, and denominational resources.

**Owns**

- Canonical scripture references and index
- Tagged reusable lesson outlines, examples, and denominational resource packs

**Provides**

- Catalog APIs (browse, search)
- Tenant-scoped libraries (private + shared)

**Consumes**

- Storage (artifact persistence)
- Search (indexing)

**Key events**

- `ContentPackPublished`, `ContentPackUpdated`

**Boundaries**

- Versioned, immutable content; updates create new versions.

---

### 2.20 Localization & Internationalization

**Purpose**: Enable multi-language support for both UI and generated content.

**Owns**

- Locale catalogs (UI strings)
- Scripture translation variants and mapping

**Provides**

- Language toggle, locale negotiation
- API for scripture text retrieval in selected translation

**Consumes**

- Lesson Pipeline (for prompt localization)
- DocGen/Media (for deliverables in locale)

**Key events**

- `LocaleAdded`, `TranslationPackUpdated`

**Boundaries**

- Scripture licensing rules must be respected.

---

### 2.21 Compliance & Policy

**Purpose**: Ensure platform-wide compliance with data protection and regulatory obligations.

**Owns**

- Data export/erasure requests (GDPR/CCPA)
- Consent records and policy acceptance logs
- Open Records compliance tooling (for public orgs)

**Provides**

- APIs and admin UI for subject access requests
- Export packages with redaction rules

**Consumes**

- IAM (user linkage)
- Storage (artifact references)

**Key events**

- `DataExportRequested`, `DataDeleted`

**Boundaries**

- Clear retention windows; policy versioning.

---

### 2.22 Tenant Analytics

**Purpose**: Give tenant admins insights into usage, adoption, and lesson trends within their org.

**Owns**

- Usage fact tables per tenant
- Adoption metrics (active users, lessons per week)

**Provides**

- Admin dashboards (tenant scope)
- Export endpoints (CSV/Excel)

**Consumes**

- Usage Metering (raw facts)
- Observability (metrics)

**Key events**

- `TenantAnalyticsSnapshotPublished`

**Boundaries**

- Tenant scope enforced; no leakage.

---

### 2.23 UI Builder / Dynamic Forms

**Purpose**: Enable tenant-level customization of forms, prompts, and lesson attributes.

**Owns**

- JSON form definitions and schemas
- Field catalogs and validation rules

**Provides**

- APIs to fetch/render forms dynamically
- Admin UI for tenant overrides (drag/drop builder)

**Consumes**

- Tenant Mgmt (settings)
- IAM (permissions)

**Key events**

- `FormDefinitionCreated`, `FormDefinitionUpdated`

**Boundaries**

- Schema validation enforced; defaults provided for safety.

---

### 2.24 Security Services

**Purpose**: Enterprise-grade security controls beyond IAM basics.

**Owns**

- MFA enforcement policies (per tenant/user)
- API key management and rotation
- Secret storage and rotation policies

**Provides**

- MFA enrollment flows (TOTP, WebAuthn/Passkeys)
- Admin APIs to manage keys/secrets

**Consumes**

- IAM (user accounts)
- Integration/API Mgmt (external calls)

**Key events**

- `MFAEnabled`, `SecretRotated`

**Boundaries**

- Sensitive data encrypted at rest; audit trails for all changes.

---

## 3) Interactions Map (at a glance)

- **IAM → everyone**: issues identity/tenant/roles; every pillar trusts IAM for authZ context.
- **Tenant Mgmt ↔ Entitlements/Billing**: plan/seat state becomes feature gates used by web/mobile and APIs.
- **Guardrails → Lesson Pipeline**: preflight allow/deny (system ⊕ denomination ⊕ tenant ⊕ user-level prefs) before generation starts.
- **Lesson Pipeline → DocGen & Media → Storage**: jobs produce content; renderers/media generators write artifacts to Storage (versioned).
- **Storage → Search**: artifacts & summaries get indexed; search feeds reuse/related items.
- **Notifications** hangs off IAM (invites/verification) and Lesson events (ready, review requested, approved), and **Commerce Admin** (receipts/dunning/renewals).
- **Roles & Collaboration** overlays Lesson Pipeline & DocGen: hardcoded roles (Creator/Approver/Admin/Learner) gate create→review→approve; Learner gets read-only/pre-lesson surfaces.
- **Marketplace** reads from Storage (published versions) and optionally **Billing** for paid listings; enforces tenant isolation & licenses.
- **Commerce Admin** orchestrates subscription lifecycle (upgrades/downgrades/cancel/reactivate), refunds/credits, seat adjustments; works with **Billing**, **Entitlements**, **Tenant Mgmt**, and **Notifications**.
- **Platform Ops & Cross-Tenant Analytics** consumes Usage/Observability/Billing for SuperAdmin dashboards—no raw tenant data exposed.
- **Web Responsiveness & Mobile Experience** consume SDK/APIs, signed URLs, and push; they don’t own business logic.
- **Observability** wraps everything (traces/metrics/logs) and emits audit entries.

---
