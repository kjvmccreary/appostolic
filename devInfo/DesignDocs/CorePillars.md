# Appostolic — Core Pillars

> **Purpose**  
> This document defines the **long-term pillars** of the Appostolic platform — the durable, cross-pod capabilities that underpin all products in the suite.  
> It is deliberately _not_ an MVP cut; instead, it describes the eventual shape of the system so we can reason about scope, boundaries, and future roadmap.

---

## 1) Identity & Access Management (IAM)

- **Purpose**: Multi-tenant user authentication, authorization, and role management.
- **Capabilities**:
  - Authentication methods: Username/Password, Magic Link (email), OAuth providers (Google, Microsoft/Entra ID, Facebook).
  - Multi-tenant memberships (users can belong to multiple tenants).
  - Hardcoded roles for v1: **Owner**, **Admin**, **Creator**, **Approver**, **Learner** (roles are tenant-scoped).
  - Tenant switching (user selects active tenant).
  - Invite system for onboarding.
- **Integration**: Works with Guardrails (role-based enforcement), Tenant config, and Audit logs.

---

## 2) Content Generation & Workflow Pillars

### 2.1 Prompt & Guardrails

- **Purpose**: Ensure all AI-generated content is doctrinally aligned, age-appropriate, and safe.
- **Capabilities**:
  - System-level guardrails (hard rules).
  - Tenant-level overrides (configurable per org).
  - User-level guardrails/preferences.
  - Denomination profiles (presets).
  - Preflight evaluator returns allow/deny + reasons.
- **Integration**: Used by Lesson Pipeline and AI Services.
- **Boundaries**: Guardrails are not user-creatable (beyond selecting options).

### 2.2 Lesson Pipeline

- **Purpose**: Core engine for going from prompt → lesson/job outputs.
- **Capabilities**:
  - Job submission API (topic, duration, age group, deliverables).
  - Preflight guardrails check.
  - Async workflow execution (queue + worker).
  - Iterative refinement loops (user feedback → regenerate).
  - Supports multiple content modes: **Lesson Plans** (MVP), future **Devotionals**, **Sermons**, **Family Guides**.
  - Devotional mode supports output formats: Daily Reflections, Journaling Templates, Guided Prayers, Family Devotionals.
- **Integration**: Consumes Guardrails, DocGen, Usage Metering, Storage.
- **Boundaries**: Workflows are initially hardcoded (Creator→Approver→Publish). Full dynamic workflow builder is a future pillar.

### 2.3 DocGen, Deliverables & Media

- **Purpose**: Transform structured content into usable formats.
- **Capabilities**:
  - Handouts: Markdown → PDF (Syncfusion).
  - Slides: HTML/Reveal.js, optional PPTX.
  - Media generation (images, audio, video) as a parallel pipeline (post-MVP).
  - Devotional outputs: journaling PDFs, guided prayer sheets, audio devotionals.
- **Integration**: Plugs into Lesson Pipeline and Marketplace.
- **Boundaries**: Media generation initially via external providers (OpenAI, ElevenLabs).

### 2.4 Interactive Learning (Quizzes & Trivia)

- **Purpose**: Enrich learner engagement with AI-generated quizzes and trivia.
- **Capabilities**:
  - AI generates age-appropriate, doctrinally aligned trivia/quiz questions from lesson themes.
  - Creators can edit in Syncfusion editor.
  - Deliverables: editable docs, PDFs, interactive quiz components.
  - **Learner Role**: take quizzes (completion tracked, not just correctness). Teachers can award recognition (gold stars).
  - Completion data flows into Tenant Analytics.
- **Roadmap**:
  - **MVP**: Completion tracking + teacher recognition.
  - **Post-MVP**: Full “Interactive Learning & Assessment” pillar — scoring, badges, streaks, leaderboards, automated rewards.

---

## 3) Tenant & Org Management

- **Purpose**: Provide enterprise-grade multi-tenant controls.
- **Capabilities**:
  - Tenant creation and management.
  - Membership management (add/remove users, assign roles).
  - Tenant-level settings, defaults, branding.
- **Integration**: Works with IAM, Guardrails, Analytics, Billing.
- **Boundaries**: No custom role definition (roles are fixed).

---

## 4) Usage Metering & Plans

- **Purpose**: Track and enforce quotas for lesson generation, storage, and advanced features.
- **Capabilities**:
  - Metered job counts and token usage.
  - Plan tiers (Free/Pro/Org).
  - Seat management for Org.
- **Integration**: Works with Billing, Tenant Mgmt, Analytics.

---

## 5) Billing & Commerce Admin

- **Purpose**: Subscription and payment management.
- **Capabilities**:
  - Stripe integration for plans, billing cycles, payments.
  - Seat management, subscription changes, upgrades/downgrades.
  - Dunning, refunds, credits, invoicing (post-MVP).
- **Integration**: Tight coupling with Usage Metering & Tenant Mgmt.
- **Boundaries**: Only Owners/Admins access billing UI.

---

## 6) Lesson Sharing & Marketplace

- **Purpose**: Enable distribution, discovery, and optional monetization of created content.
- **Capabilities**:
  - Publish lesson plans, devotionals, or full curricula.
  - Free or paid (monetization).
  - Browsing, search, tagging.
- **Integration**: Uses Storage, DocGen, Billing, Guardrails.
- **Roadmap**: Marketplace is a **future differentiator**. In early releases, focus is internal sharing; external marketplace comes later.

---

## 7) Learner Experience

- **Purpose**: Provide a portal for the **Learner** role.
- **Capabilities**:
  - Access pre-lesson activities.
  - View lesson handouts/slides.
  - Take quizzes, see trivia, receive recognition.
  - Eventually: track progress, badges, streaks, personalized devotionals.
- **Integration**: Relies on IAM (role=Learner), Lesson Pipeline (interactive deliverables), Analytics (engagement tracking).
- **Boundaries**: Basic access + completion tracking at v1; full gamification later.

---

## 8) Integration & API Management

- **Purpose**: Expose and consume external systems.
- **Capabilities**:
  - Webhooks for lesson/job events.
  - Outbound connectors (Teams, Slack, email, etc.).
  - API keys + scopes.
- **Roadmap**: Post-MVP.

---

## 9) Observability & Audit

- **Purpose**: Ensure system transparency, reliability, and compliance.
- **Capabilities**:
  - Structured JSON logs (Serilog).
  - OpenTelemetry traces + metrics.
  - Tenant-scoped audit ledger (immutable).
  - Redaction of PII in logs.
  - Retention policy: 7d hot, 30d warm, 180d cold.
  - Dashboards & alerts by pillar (Auth, Guardrails, Jobs, Outbox, Billing).
- **Access control**:
  - Tenant Admins see only their tenant’s audit events.
  - SuperAdmin has full log access (logged in audit trail).

---

## 10) Platform Ops & SuperAdmin

- **Purpose**: Cross-tenant monitoring, governance, and configuration.
- **Capabilities**:
  - SuperAdmin dashboard: usage across tenants, billing health, error rates.
  - Global config/flags.
  - Cross-tenant observability.

---

## 11) Compliance & Policy

- **Purpose**: Support data privacy, open records, and export/erasure requests.
- **Capabilities**:
  - GDPR/CCPA data subject export & delete.
  - Tenant-scoped data retention policies.
  - Public/open records compliance for government use cases.

---

## 12) Future Differentiators

- **Dynamic Workflow Builder (UI-driven)**: Tenant admins define custom flows, node types (human task, AI task, approvals, timers).
- **UI Builder / Dynamic Forms**: Allow tenants to define forms + data collection workflows.
- **Video Generator**: TTS + slides to video; optional third-party integration.
- **Advanced Analytics**: Cross-tenant adoption insights, financial usage dashboards.

---

## Note — System Logging & Observability

- **Structured JSON logs** across all services (e.g., Serilog in ASP.NET Core).
- **OpenTelemetry** traces and metrics with trace IDs included in every log line.
- **Tenant-scoped audit ledger** available to tenant admins; full system logs restricted to SuperAdmin.
- **Sensitive data redaction** applied (emails masked, no raw secrets/tokens).
- **Retention policy**: e.g., 7d hot (searchable), 30d warm (compressed), 180d cold (archived).
- **Alerting & dashboards** per pillar (Auth, Guardrails, Lesson Pipeline, Outbox, Billing).
- **Access control**: all log access is itself audited.

---"} ```
