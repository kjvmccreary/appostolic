# Appostolic — Architecture Snapshot (2025-09-13)

This document describes the structure, runtime, and conventions of the Appostolic monorepo. It’s organized to group related topics together for easier navigation and future updates.

## What’s new

- Notif‑29: Bulk resend endpoint `/api/notifications/resend-bulk` with per-request and per-tenant daily caps, tenant scoping, and per-recipient throttling. Also enabled JSON string↔︎enum serialization globally so request bodies may use enum names.

## Monorepo overview

- Package manager: PNPM workspaces + Turborepo
- Languages: TypeScript/Node (web/mobile/packages), C# .NET 8 (API)
- Root files: `appostolic.sln`, `package.json`, `pnpm-workspace.yaml`, `turbo.json`, `tsconfig.base.json`, `Makefile`
- Top-level folders:
  - `apps/`
    - `api/` — .NET 8 Minimal API + EF Core 8; dev header auth; agent runtime; worker/queue
    - `web/` — Next.js 14 (App Router) with server-side API proxy routes
    - `mobile/` — React Native/Expo
    - `render-worker/` — Node worker (TS)
  - `packages/` — shared packages: `sdk/`, `models/`, `ui/`, `docgen/`, `prompts/`, `video-scenes/`
  - `infra/` — Docker Compose stack, devcontainer, init SQL
  - `scripts/` — helper scripts (e.g., dev doctor)

### Full folder tree (abridged but comprehensive)

```
appostolic/
├─ appostolic.sln
├─ package.json
├─ pnpm-workspace.yaml
├─ turbo.json
├─ tsconfig.base.json
├─ Makefile
├─ README.md
├─ RUNBOOK.md
├─ SnapshotArchitecture.md
├─ apps/
│  ├─ api/
│  │  ├─ Appostolic.Api.csproj
│  │  ├─ Program.cs
│  │  ├─ App/
│  │  │  ├─ Endpoints/
│  │  │  │  ├─ V1.cs
│  │  │  │  ├─ DevToolsEndpoints.cs       # POST /api/dev/tool-call (Development only)
│  │  │  │  ├─ DevAgentsDemo.cs          # POST /api/dev/agents/demo (Development only)
│  │  │  │  ├─ DevAgentsEndpoints.cs     # GET /api/dev/agents (Development only)
  │  │  │  │  ├─ DevNotificationsEndpoints.cs # GET/POST /api/dev/notifications (Development only)
  │  │  │  │  ├─ NotificationsAdminEndpoints.cs # /api/notifications (prod: list/details/retry; tenant-scoped + superadmin)
│  │  │  │  ├─ AgentsEndpoints.cs        # /api/agents CRUD + /api/agents/tools
  │  │  │  │  └─ AgentTasksEndpoints.cs    # /api/agent-tasks (create/get/list; X-Total-Count; filters: status/agentId/from/to/q)
│  │  │  └─ Infrastructure/
│  │  │     ├─ Auth/
│  │  │     │  └─ DevHeaderAuthHandler.cs
│  │  │     └─ MultiTenancy/
│  │  │        └─ TenantScopeMiddleware.cs
│  │  ├─ Application/
│  │  │  ├─ Agents/
│  │  │  │  ├─ AgentRegistry.cs
│  │  │  │  ├─ Runtime/                   # Orchestrator + TraceWriter
│  │  │  │  │  ├─ AgentOrchestrator.cs
│  │  │  │  │  └─ TraceWriter.cs
│  │  │  │  ├─ Queue/                     # In-memory queue + worker
│  │  │  │  │  ├─ IAgentTaskQueue.cs
│  │  │  │  │  ├─ InMemoryAgentTaskQueue.cs
│  │  │  │  │  └─ AgentTaskWorker.cs
│  │  │  │  ├─ Tools/                     # Deterministic dev tools
│  │  │  │  │  ├─ WebSearchTool.cs
│  │  │  │  │  ├─ DbQueryTool.cs
│  │  │  │  │  └─ FsWriteTool.cs
│  │  │  │  └─ Model/                     # Mock model adapter
│  │  │  │     └─ MockModelAdapter.cs
│  │  │  └─ Validation/
│  │  │     └─ Guard.cs
│  │  ├─ Domain/
│  │  │  └─ Agents/
│  │  │     ├─ Agent.cs
│  │  │     ├─ AgentTask.cs
│  │  │     ├─ AgentTrace.cs
│  │  │     ├─ AgentStatus.cs
│  │  │     └─ TraceKind.cs
│  │  ├─ Infrastructure/
│  │  │  ├─ AppDbContext.cs
│  │  │  └─ Configurations/
│  │  │     └─ *.cs
│  │  ├─ Migrations/
│  │  │  └─ *.cs
│  │  ├─ tools/
│  │  │  └─ seed/                        # Idempotent seed for dev user/tenants
│  │  └─ Properties/launchSettings.json
│  ├─ web/
│  │  ├─ next.config.mjs
│  │  ├─ package.json
│  │  ├─ src/
│  │  │  └─ lib/serverEnv.ts              # API_BASE/DEV_USER/DEV_TENANT validation
│  │  └─ app/
│  │     ├─ layout.tsx
│  │     ├─ page.tsx
│  │     ├─ dev/page.tsx
│  │     ├─ dev/agents/page.tsx
│  │     ├─ dev/agents/components/AgentRunForm.tsx
│  │     ├─ dev/agents/components/TracesTable.tsx
│  │     ├─ studio/agents/                # Agent Studio (CRUD UI)
│  │     │  ├─ page.tsx                   # List (defaults to enabled-only)
│  │     │  ├─ new/page.tsx               # Create
│  │     │  ├─ [id]/page.tsx              # Edit
│  │     │  └─ components/
│  │     │     ├─ AgentForm.tsx           # Create/Edit form (includes Enabled toggle)
│  │     │     └─ AgentsTable.tsx         # List table
│  │     └─ api-proxy/
│  │        ├─ dev/agents/route.ts        # GET /api-proxy/dev/agents → API /api/dev/agents
│  │        ├─ agents/route.ts            # GET/POST /api-proxy/agents → API /api/agents
│  │        ├─ agents/[id]/route.ts       # GET/PUT/DELETE /api-proxy/agents/{id}
│  │        └─ agents/tools/route.ts      # GET /api-proxy/agents/tools → API /api/agents/tools
│  │        └─ agent-tasks/
│  │           ├─ route.ts                 # GET/POST /api-proxy/agent-tasks → API
│  │           └─ [id]/route.ts            # GET /api-proxy/agent-tasks/{id}
│  ├─ mobile/
│  │  ├─ app.json
│  │  ├─ package.json
│  │  └─ src/App.tsx
│  └─ render-worker/
│     └─ src/index.ts
├─ packages/
│  ├─ sdk/
│  │  ├─ package.json
│  │  ├─ tsconfig.json
│  │  └─ src/index.ts
│  ├─ models/
│  │  └─ src/*.ts
│  ├─ prompts/
│  ├─ ui/
│  └─ video-scenes/
├─ infra/
│  ├─ devcontainer/
│  │  └─ devcontainer.json
│  └─ docker/
│     ├─ compose.yml
│     ├─ docker-compose.yml
│     ├─ Dockerfile.api
│     ├─ Dockerfile.web
│     ├─ .env
│     ├─ initdb/
│     │  └─ init.sql
│     └─ data/
│        ├─ postgres/
│        ├─ minio/
│        └─ qdrant/
└─ scripts/
  └─ dev-doctor.sh
```

## Tech stack (high level)

- Backend: .NET 8, Minimal API, EF Core 8 (Npgsql provider, PostgreSQL)
- Frontend: Next.js 14 (app router), TypeScript, React 18, MUI Premium
- Mobile: Expo/React Native (TypeScript)
- Infra: Docker Compose for Postgres, Redis, MinIO, Qdrant, pgAdmin, Mailhog (dev)
- Docs/SDK: Swashbuckle for OpenAPI; custom TypeScript SDK package

---

## Runtime architecture

### API service (`apps/api`)

- Entrypoint: `apps/api/Program.cs`
- Middleware: Swagger, Authentication/Authorization, TenantScopeMiddleware, legacy sample for `X-Tenant-Id`
- Auto-migration (Dev/Test): `Database.Migrate()` at startup for relational providers
- OpenTelemetry: traces, metrics, logs with optional OTLP exporter; console exporters in Development

### Web app (`apps/web`)

- Next.js 14 (App Router); server-only proxy routes under `app/api-proxy/*` inject dev headers and avoid CORS
- Env validation: `src/lib/serverEnv.ts` ensures `NEXT_PUBLIC_API_BASE`, `DEV_USER`, `DEV_TENANT`
- Dev pages: `/dev/agents`, Agent Studio under `/studio/agents`

### Mobile (`apps/mobile`) and Render Worker (`apps/render-worker`)

- Mobile: Expo/React Native (TypeScript), minimal scaffold
- Render Worker: Node/TypeScript worker for rendering tasks (placeholder)

---

## Cross-cutting concerns

### Authentication & Authorization

- Dev headers (API): `x-dev-user` and `x-tenant`; emits claims `sub`, `email`, `tenant_id`, `tenant_slug`. All `/api/*` require authorization (dev headers expected). Swagger remains public.
  - Superadmin (dev/test friendly): `DevHeaderAuthHandler` can emit a `superadmin` claim when header `x-superadmin: true` is present or the user's email is included in config allowlist `Auth:SuperAdminEmails`. Used to enable cross-tenant notification views in admin endpoints.
- Web tenant selection/switcher:
  - Two‑stage login with `/select-tenant`; auto‑select when single membership.
  - Header `TenantSwitcher` updates session via `session.update({ tenant })` and sets `selected_tenant` cookie via `/api/tenant/select`.
  - Server proxies forward `x-tenant` based on session or cookie; when web auth is enabled, protected routes require a selected tenant (401), except invite acceptance route.
- Role-based guards (Auth‑11): server-only helpers (`roleGuard.ts`) enforce Owner/Admin on sensitive proxy routes for defense-in-depth.
- Security contract: a dev-mode integration test verifies unauthenticated `/api/*` calls return 401/403; the same requests succeed with dev headers.

### Multi-tenancy & RLS

- `TenantScopeMiddleware` skips `/health*` and `/swagger*`; when authenticated and `tenant_id` exists, begins a DB transaction and sets `app.tenant_id` GUC for RLS.
- Legacy demo header `X-Tenant-Id` in `Program.cs` for sample `/lessons` endpoints.

---

## Domain capabilities

### Agents (runtime v1)

- Domain types: Agent, AgentTask, AgentTrace; enums AgentStatus, TraceKind
- Validation: `Guard.cs` enforces invariants (NotNull, MaxLength, InRange)
- Orchestration: `AgentOrchestrator` with deterministic `MockModelAdapter` in dev; allowlist enforcement; trace step numbering strategy
- TraceWriter: persists traces; clamps non‑negatives; retries once on unique conflicts
- Tools: `web.search`, `db.query`, `fs.write` via `ToolRegistry`
- Queue/Worker: `InMemoryAgentTaskQueue` (Channel<Guid>, SingleReader=true); `AgentTaskWorker` consumes and processes with idempotence and graceful shutdown semantics
- Agent store resolution is DB‑first with fallback to static `AgentRegistry`

Endpoints:

- `GET /api/agents` (+ paging, includeDisabled)
- `GET /api/agents/{id}` | `POST /api/agents` | `PUT /api/agents/{id}` | `DELETE /api/agents/{id}`
- `GET /api/agents/tools`
- Agent tasks: `POST /api/agent-tasks`, `GET /api/agent-tasks/{id}?includeTraces=true`, `GET /api/agent-tasks` (filters, paging; `X-Total-Count`)

### Notifications (email)

Components:

- Queue: `IEmailQueue` + `EmailQueue` (in‑memory channel used by background dispatcher)
- Dispatcher (v1): `EmailDispatcherHostedService` renders and sends with retry/backoff; metrics/logging via OTEL
- Template renderer: `ScribanTemplateRenderer`
- Providers: `SmtpEmailSender` (dev), `SendGridEmailSender` (prod/real), `NoopEmailSender` fallback
- Enqueuer: `NotificationEnqueuer` helpers for verification and invite emails
- Resend (manual/bulk):
  - Manual: `POST /api/notifications/{id}/resend` (also dev variant) clones and enqueues a linked child, enforcing throttle via `(to_email, kind)` within `ResendThrottleWindow`.
  - Bulk: `POST /api/notifications/resend-bulk` filters by kind/date/recipients and enforces:
    - Per-request cap `Notifications:BulkResendMaxPerRequest` (default 100)
    - Per-tenant daily cap `Notifications:BulkResendPerTenantDailyCap` (default 500, rolling 24h)
    - Tenant scoping: non‑superadmin limited to current tenant; superadmin may filter by `tenantId`.
    - Throttle: per‑recipient pre‑check and outbox enforcement to avoid violating `ResendThrottleWindow`.
  - JSON: API accepts enum names in request bodies (global `JsonStringEnumConverter`), e.g., `{ "kind": "Verification" }`.
- PII hardening (Notif‑21):
  - Token hashing: verification/invite tokens are hashed (SHA‑256) and only the hash is stored on the outbox row (`TokenHash`); raw tokens are never persisted in Notification fields or `data_json`.
  - Pre‑rendered snapshots: subject/html/text may be pre‑rendered at enqueue time and stored; dispatcher reuses snapshots when present to avoid re‑render divergence.
  - Redacted logging: emails in logs are redacted (e.g., k\*\*\*@example.com) across SMTP/SendGrid providers and dispatcher paths.

PII scrubbing (Notif‑23):

- Early scrub of sensitive fields prior to deletion to minimize PII exposure time. A dedicated scrub pass nulls selected columns for notifications older than a scrub window but newer than the delete retention cutoff.
- Configuration (NotificationOptions):
  - Master switch `PiiScrubEnabled` (default true).
  - Scrub windows: `ScrubSentAfter`, `ScrubFailedAfter`, `ScrubDeadLetterAfter`.
  - Per‑field toggles: `ScrubToName`, `ScrubSubject`, `ScrubBodyHtml`, `ScrubBodyText`, and `ScrubToEmail` (email off by default).
- Observability: `NotificationsPurgeHostedService` logs `scrubbed` counts alongside purged counts each run.

Further reading:

- Privacy policy (engineering draft): devInfo/Sendgrid/privacyPolicy.md
- Vendor compliance and subprocessors: devInfo/Sendgrid/vendorCompliance.md

Outbox & Dispatcher (Notif‑13/14/15):

- Table `app.notifications` stores durable outbox entries (kind, to_email, data_json, dedupe_key, status, attempts, errors, timestamps; snapshots subject/html/text)
- Dispatcher `NotificationDispatcherHostedService` leases (`Queued`→`Sending`), renders, sends, and updates status with jittered backoff (0.5s/2s/8s +/-20%) and terminal `DeadLetter` on exhaustion; event‑driven via ID queue with polling fallback
- Testing note: EF InMemory provider does not support transactions; leasing logic gates transactional semantics behind `Database.IsRelational()` to keep tests stable while retaining transactions for relational providers.

Dedupe & Retention (Notif‑17/18):

- TTL dedupe table `app.notification_dedupes` (PK: dedupe_key, expires_at) is claimed before outbox insert; duplicate claims within TTL throw `DuplicateNotificationException`
- Partial unique index `ux_notifications_dedupe_key_active` applies only to in‑flight statuses (`Queued`,`Sending`); `Sent` dedupe is governed by the TTL table
- Hourly purge job removes expired dedupe claims and old notifications; retention windows configurable via `Notifications` options (e.g., Sent: 60d; Failed/Dead: 90d)
- Scrub‑then‑delete ordering (Notif‑23): For items within the scrub window but not yet at the deletion cutoff, the job nulls configured fields first; items past the deletion cutoff are removed entirely.

Dev endpoints:

- `POST /api/dev/notifications/verification` and `/invite` enqueue test emails; requires dev headers

Prod admin endpoints (Notif‑24):

- `GET /api/notifications` — list notifications
  - Non‑superadmin: tenant‑scoped using `tenant_id` claim; supports filters `status`, `kind`; paging via `take`/`skip`; `X-Total-Count` header.
  - Superadmin: cross‑tenant view allowed and may optionally filter by `tenantId`.
- `GET /api/notifications/{id}` — details
  - Non‑superadmin: 403 if the notification’s `tenant_id` doesn’t match current tenant.
  - Superadmin: allowed.
- `POST /api/notifications/{id}/retry` — retry Failed/DeadLetter
  - Transitions to `Queued` and nudges dispatcher; enforces same tenant/superadmin gating.

Access control:

- Non‑superadmin requests are auto‑scoped by current tenant (from `tenant_id` claim); cross‑tenant access is denied.
- Superadmin requests (claim `superadmin=true`) may access across tenants and use `tenantId` filter on list.

Provider webhooks:

- `POST /api/notifications/webhook/sendgrid` — receives SendGrid event webhooks; optional shared-secret via header. Normalizes and stores provider delivery status under `notifications.data_json.provider_status` along with event timestamp; designed to be idempotent for replayed events.

#### Field encryption (Notif-22)

- Optional at-rest encryption for selected outbox fields: `to_name`, `subject` (optional), `body_html`, `body_text`.
- Format: `enc:v1:` prefix followed by Base64URL payload containing AES-GCM ciphertext + nonce + tag.
- Configuration (NotificationOptions):
  - `EncryptFields` (bool) — master switch; default false
  - `EncryptionKeyBase64` (string) — 256-bit key as base64; required when enabled
  - Per-field toggles: `EncryptToName`, `EncryptSubject`, `EncryptBodyHtml`, `EncryptBodyText`
- Runtime behavior:
  - Encrypt on write (`CreateQueuedAsync` and `MarkSentAsync`), decrypt on lease (`LeaseNextDueAsync`).
  - DI selects `AesGcmFieldCipher` when enabled+key is valid; otherwise `NullFieldCipher` (no-op) for backward compatibility.
- No schema migration required; ciphertext is stored in existing text columns. Downstream services receive plaintext via the lease path.

---

## API surface

### Grouped v1 endpoints (`apps/api/App/Endpoints/V1.cs`)

- `GET /api/me` — user/tenant claims
- `GET /api/tenants` — current tenant summary
- `GET /api/lessons?take=&skip=` — paginated list; `POST /api/lessons` — create (uses current tenant)

### Agents endpoints

- `GET /api/agents?take=&skip=&includeDisabled=` — list (enabled‑only by default)
- `GET /api/agents/{id}` | `POST /api/agents` | `PUT /api/agents/{id}` | `DELETE /api/agents/{id}`
- `GET /api/agents/tools` — tool catalog for allowlists

### Agent tasks endpoints

- `POST /api/agent-tasks` — create, enqueue, returns `201 Created` with summary
- `GET /api/agent-tasks/{id}` — details; `?includeTraces=true` includes traces
- `GET /api/agent-tasks` — list with filters (status/agentId/from/to/q) and paging; sets `X-Total-Count`

### Dev-only endpoints

- `POST /api/dev/tool-call` — deterministic tool tests
- `GET /api/dev/agents` — seeded agents for UI dropdowns
- `POST /api/dev/agents/demo` — inline agent run with traces
- `POST /api/dev/notifications/verification` and `/invite` — enqueue test emails

### Swagger/OpenAPI

- Swagger JSON: `GET /swagger/v1/swagger.json` | UI: `GET /swagger/`
- Security scheme: API key (DevHeaders) for dev headers; UI remains public

---

## Data & persistence (EF Core)

- Provider: Npgsql (PostgreSQL 16 dev); default schema `app`
- Migrations under `apps/api/Migrations/`; auto‑migrate enabled in Dev/Test
- RLS strategy: set `app.tenant_id` via middleware; DB‑side policies/init in `infra/initdb` and EF migrations

Key domain tables and constraints:

- Agent runtime: `agents`, `agent_tasks`, `agent_traces` with unique index `(task_id, step_number)` and checks on step/duration
- Notifications: `notifications` (outbox) and `notification_dedupes` (TTL claims); dispatcher indexes on `(status, next_attempt_at)` and `(tenant_id, created_at DESC)`; `citext` for emails, `jsonb` for data. Resend (Notif‑27) adds metadata columns and indexes for safe resend policies: `resend_of_notification_id` (FK self‑reference, NO ACTION), `resend_reason`, `resend_count` (default 0), `last_resend_at`, `throttle_until`; indexes on `(resend_of_notification_id)` and `(to_email, kind, created_at DESC)`.
- Invitations: `invitations` with FKs to `tenants` and `users`; functional unique `(tenant_id, lower(email))` and index on `(tenant_id, expires_at)`
- Auth/tenant integrity: unique slug `tenants(name)`; FKs cascade/set‑null as appropriate

---

## Observability

- OpenTelemetry resource: `Appostolic.Api`
- Traces: ASP.NET Core, HTTP client, custom sources (`Appostolic.AgentRuntime`, `Appostolic.Tools`)
- Metrics: ASP.NET Core, HTTP client, runtime, `Appostolic.Metrics` (e.g., email sent/failed)
- Logs: structured logs; console exporters in Development; optional OTLP endpoint via `OTEL_EXPORTER_OTLP_ENDPOINT`

- Privacy (Notif‑25): Recipient emails are redacted across dispatcher/providers and log scopes; metrics include only non‑PII tags (e.g., kind). See devInfo/Sendgrid/privacyPolicy.md for practices and devInfo/Sendgrid/vendorCompliance.md for provider details.

---

## Infra & local development

- Docker Compose: Postgres, Redis, MinIO, Qdrant, Mailhog, pgAdmin
- Init SQL: `infra/initdb/init.sql` sets extensions, schemas, GUC helpers
- Devcontainer configuration available

Makefile highlights:

- `make migrate` — apply EF migrations
- `make sdk` — regenerate OpenAPI/SDK
- `make down` — stop local Docker stack
- `make up` — start infra Docker stack
- `make bootstrap` — nuke volumes, bring up infra, wait for Postgres, migrate, and seed
- `make api` — run API with dotnet watch (http://localhost:5198)
- `make web` — run Next.js dev server
- `make mobile` — run Expo dev server (port 8082)
- `make doctor` — run dev-doctor script

Important URLs:

- API base: http://localhost:5198
- Swagger UI: http://localhost:5198/swagger/
- Swagger JSON: http://localhost:5198/swagger/v1/swagger.json
- Postgres: localhost:55432 (container `postgres`)
- Redis: localhost:6380
- MinIO: http://localhost:9002 (API) / http://localhost:9003 (Console)
- Mailhog UI: http://localhost:8025
- Qdrant UI/API: http://localhost:6334
- pgAdmin: http://localhost:8081

Dev credentials (local only): see `infra/docker/.env` for Postgres/Redis/MinIO/Mailhog/Qdrant/pgAdmin

Dev auth for API testing: send headers `x-dev-user` and `x-tenant` (default seed: `kevin@example.com` / `kevin-personal`)

Troubleshooting:

- Swagger shows JSON: use `/swagger/` (trailing slash) or rely on `/swagger` redirect
- 401 on `/api/*`: send dev headers; run `make seed` (or `make bootstrap`) if you haven’t seeded
- Tenant scoping: verify `tenant_id` claim exists or use legacy `X-Tenant-Id` for sample `/lessons`
- Postgres connection errors: ensure Docker is up and port `55432` matches env
- Node engines: repo requires `node >=20 <21`; using Node 19 may error (URL.canParse). Use Node 20.x.

---

## SDK & OpenAPI (`packages/sdk`)

- OpenAPI generated via Swashbuckle CLI (output at `packages/sdk/openapi.json`)
- SDK currently provides minimal fetch helpers/types; can be expanded via codegen

---

## What’s new since last snapshot

- Agents CRUD: endpoints added, tool catalog surfaced; Agent enable/disable via `IsEnabled` with default true and includeDisabled query
- Agent Studio (web): list/create/edit pages; server proxies for Agents and Tools
- Agent resolution: `AgentStore` for DB‑first resolution with static fallback
- Database: migration for `is_enabled` on `app.agents`
- Notifications v1: in‑process queue + dispatcher with retry/backoff and OTEL metrics; SMTP/SendGrid/Noop providers and production guard; enqueue helpers for verification/invite with absolute links
- Outbox (Notif‑13): durable table `app.notifications` with indexes and dedupe
- Dedupe/Retention (Notif‑17/18): TTL dedupe table `app.notification_dedupes`, narrowed partial unique (Queued,Sending), and hourly purge job
- Notifications (Notif‑24): production admin endpoints under `/api/notifications` with tenant‑scoped access for Owner/Admin and optional cross‑tenant superadmin view; superadmin claim available via dev header or allowlist for testing and operations.
- Notifications (Notif‑27): outbox resend metadata added — self‑FK `resend_of_notification_id` (NO ACTION), `resend_reason`, `resend_count` (default 0), `last_resend_at`, `throttle_until`; supporting indexes on `(resend_of_notification_id)` and `(to_email, kind, created_at DESC)`.
- Notifications (Notif‑28): manual resend endpoints — `POST /api/notifications/{id}/resend` (prod admin, tenant‑scoped with superadmin override) and `POST /api/dev/notifications/{id}/resend` (dev). Returns `201 Created` with `Location` on success; enforces resend throttling with `429 Too Many Requests` and `Retry‑After` seconds header. Throttle window configurable via `NotificationOptions.ResendThrottleWindow` (default 5m).
- Migrations hygiene: proper EF migrations were scaffolded (Designer files + updated ModelSnapshot). An obsolete `token_aggregates` migration was converted to a no‑op to avoid conflicts.

---

## Prompt starters (copy into ChatGPT)

- “Expose read-only endpoints for dev agents (already available at `/api/dev/agents`) and consider promoting to `/api/agents` for non-dev contexts. Update Swagger and web to consume the stable path.”
- “Extend EF configurations for Agent runtime with additional indexes and constraints [describe]. Create and apply a migration and confirm defaults in PostgreSQL.”
- “Regenerate OpenAPI and update the TypeScript SDK to include new endpoints (AgentTasks, DevAgents). Update `apps/web` to call through the SDK or keep using server proxies.”
- “Implement AgentTask flows (done: create, get by id with traces, list with filters). Add delete/cancel if needed and write integration tests.”
- “Harden `TenantScopeMiddleware`: ensure GUC set/reset semantics, add diagnostics, and handle exceptions explicitly.”
- “Add integration tests for `/api/lessons` using dev auth and a test Postgres; include test harness and migrations in CI.”

### Database — Notifications Outbox (Notif-13)

- Table: `app.notifications`
  - Columns: id (uuid, pk), kind (text enum), to_email (citext), to_name (text), subject/body_html/body_text (text snapshots), data_json (jsonb), tenant_id (uuid, null), dedupe_key (varchar(200), null), status (text enum), attempt_count (int2), next_attempt_at (timestamptz, null), last_error (text), created_at/updated_at (timestamptz, defaults UTC now), sent_at (timestamptz, null)
  - Indexes:
    - (status, next_attempt_at) for dispatcher eligibility
    - (tenant_id, created_at DESC) for admin/dev listings
    - (created_at DESC)
    - Partial unique on (dedupe_key) where status IN ('Queued','Sending','Sent')
  - Extensions: `citext` enabled for case-insensitive email storage

### Notifications — Dispatcher (Notif-15)

- A background service `NotificationDispatcherHostedService` consumes IDs from an in-process `INotificationIdQueue` and also polls the DB periodically.
- It leases the next due outbox row by transitioning `Queued` → `Sending`, renders the templated content, sends via the configured provider (SMTP or SendGrid), and updates status:
  - On success: `Sent` with snapshots (`subject`, `body_html`, `body_text`) and `sent_at`.
  - On transient failure: increments `attempt_count`, records `last_error`, sets `next_attempt_at` using jittered backoff (0.5s, 2s, 8s +/-20%).
  - After max attempts: marks `DeadLetter`.
- Enqueue path (Notif-14) writes `Queued` rows and pushes the new `id` to the ID queue to wake the dispatcher quickly; polling is a safety net.

### Notifications — Dedupe & Retention (Notif-17/18)

- Dedupe TTL table: `app.notification_dedupes` with primary key `dedupe_key` and `expires_at`; used to claim dedupe keys before inserting outbox rows. Duplicate claims within TTL raise a friendly `DuplicateNotificationException`.
- Partial unique index change: `ux_notifications_dedupe_key_active` now applies to in-flight statuses only (`Queued`,`Sending`), while Sent dedupe is governed by the TTL table.
- Purge job: a hosted service runs hourly and removes expired dedupe claims and notifications older than retention windows (Sent: 60d; Failed/DeadLetter: 90d; configurable via `Notifications` options).

## Running locally (dev)

- Monorepo dev: `pnpm dev` (spawns web/api/mobile if configured)
- API only: `dotnet run --project apps/api/Appostolic.Api.csproj`
- Swagger UI: http://localhost:5198/swagger/ (trailing slash)
- Health: `GET /health`, `GET /healthz`

Seeding dev data:

- Use the Makefile to migrate and seed a known user/tenant pair for dev headers:
  - `make bootstrap` (nukes local volumes, starts infra, migrates, seeds)
  - Or run `make migrate` then `make seed`

Important URLs:

- API base: http://localhost:5198
- Swagger UI: http://localhost:5198/swagger/
- Swagger JSON: http://localhost:5198/swagger/v1/swagger.json
- Postgres: localhost:55432 (container `postgres`)
- Redis: localhost:6380
- MinIO API/Console: http://localhost:9002 / http://localhost:9003
- Mailhog UI: http://localhost:8025
- Qdrant UI/API: http://localhost:6334
- pgAdmin: http://localhost:8081

Makefile highlights:

- `make migrate` — apply EF migrations
- `make sdk` — regenerate OpenAPI/SDK
- `make down` — stop local Docker stack
- `make up` — start infra Docker stack
- `make bootstrap` — nuke volumes, bring up infra, wait for Postgres, migrate, and seed
- `make api` — run API with dotnet watch at http://localhost:5198
- `make web` — run Next.js dev server
- `make mobile` — run Expo dev server (port 8082)
- `make sdk` — build API and generate OpenAPI/SDK
- `make doctor` — run dev-doctor script

## API service (`apps/api`)

### Composition

- Entrypoint: `apps/api/Program.cs`
- EF Core DbContext: partial `AppDbContext` in `Program.cs` + additional partials under `Infrastructure/`
- Swagger registration:
  - Services: `AddEndpointsApiExplorer()`, `AddSwaggerGen(...)`
  - Middleware: `UseSwagger()`, `UseSwaggerUI(...)` with `RoutePrefix = "swagger"` and `SwaggerEndpoint("/swagger/v1/swagger.json", ...)`
  - Redirect: `/swagger` → `/swagger/index.html`

### Auth (dev)

- `DevHeaderAuthHandler` reads headers:
  - `x-dev-user` — user email
  - `x-tenant` — tenant slug
- Emits claims: `sub`, `email`, `tenant_id`, `tenant_slug`
- All `/api/*` endpoints require authorization (dev headers expected)

Security contract (A11-11):

- A development-mode integration test verifies that unauthenticated requests to `/api/*` (e.g., `GET /api/agent-tasks`) return 401 (or 403), while the same requests succeed with the dev headers (200 OK). Swagger endpoints remain public and accessible without authentication.

Seeded defaults (via `apps/api/tools/seed`):

- `x-dev-user: kevin@example.com`
- `x-tenant: kevin-personal`

### Tenant scoping

- `TenantScopeMiddleware` (conventional middleware):
  - Skips `/health*` and `/swagger*`
  - If authenticated and `tenant_id` exists, begins a DB transaction and sets PostgreSQL GUC `app.tenant_id` via `set_config(...)` for tenant RLS
- Additional header-based sample (`X-Tenant-Id`) in `Program.cs` for legacy demo endpoints (`/lessons`)

### Web selector/switcher (Auth‑04/05)

- Two-stage login includes `/select-tenant` page. When only one membership exists, it auto-selects and sets a `selected_tenant` cookie.
- Header `TenantSwitcher` component (rendered in `apps/web/app/layout.tsx`) lets the user change tenants at any time:
  - Calls `session.update({ tenant })` so NextAuth JWT/session reflect the choice.
  - POSTs to `/api/tenant/select` to set `selected_tenant` cookie with `{ httpOnly: true, sameSite: 'lax', secure: NODE_ENV==='production' }`.
  - Server proxy (`buildProxyHeaders`) reads session.tenant or the cookie to forward `x-tenant` to the API in dev.
  - Auth‑10 hardening: when web auth is enabled, proxy headers now require a selected tenant for protected routes (401 if missing). A single exception exists for invite acceptance (`POST /api-proxy/invites/accept`) which allows user‑only auth and omits `x-tenant` during acceptance flow, matching the API guard.

### Auth‑11 — Route protection (role-based)

- Added `apps/web/src/lib/roleGuard.ts` with server-only helpers:
  - `guardProxyRole({ tenantId, anyOf })` for API proxy routes → returns `Response(401|403)` or `null`.
  - `pickMembership(session, { tenantId|tenantSlug })` utility for SSR contexts.
- Enforced Owner/Admin on tenant-sensitive proxy routes:
  - Members: list (GET), update (PUT), remove (DELETE)
  - Invites: list/create (GET/POST), resend (POST), revoke (DELETE)
- SSR page `/studio/admin/members` already required Owner/Admin; proxies now mirror this at the server boundary for defense-in-depth.
- Tests under `apps/web/test/api-proxy/*guard.test.ts` verify 403 when insufficient and successful proxy when authorized.

### Endpoints

Agents endpoints (in `AgentsEndpoints.cs`):

- `GET /api/agents?take=&skip=&includeDisabled=`
  - Returns paged `AgentListItem[]` ordered by `CreatedAt DESC`.
  - Default filters to enabled agents only; pass `includeDisabled=true` to include all.
- `GET /api/agents/{id}` → `AgentDetails`.
- `POST /api/agents` → create agent; enforces unique name; defaults `isEnabled=true`.
- `PUT /api/agents/{id}` → update existing agent; preserves `isEnabled` unless provided.
- `DELETE /api/agents/{id}` → delete agent.
- `GET /api/agents/tools` → list tool catalog for building allowlists in UI.

- Grouped in `apps/api/App/Endpoints/V1.cs` under `/api`:
  - `GET /api/me` — returns user/tenant claims
  - `GET /api/tenants` — current tenant summary
  - `GET /api/lessons?take=&skip=` — paginated list
  - `POST /api/lessons` — create lesson (uses current tenant)
- Non-grouped sample endpoints (dev/testing) in `Program.cs`:
  - `/` — service info
  - `/health`, `/healthz`
  - `/lessons` (GET/POST) — uses `X-Tenant-Id` GUID header

Development-only endpoints and test hooks (mapped only when `ASPNETCORE_ENVIRONMENT=Development`):

- `POST /api/dev/tool-call` (in `DevToolsEndpoints.cs`)
  - Contract: `{ name: string, input: object }`
  - Tools available: `web.search`, `db.query`, `fs.write`
  - Useful for DB inspection and deterministic tool tests without direct SQL access
- `GET /api/dev/agents` (in `DevAgentsEndpoints.cs`)
  - Lists seeded agents from `AgentRegistry` for UI dropdowns
  - Requires dev headers
- `POST /api/dev/agents/demo` (in `DevAgentsDemo.cs`)
  - Runs a `ResearchAgent` inline via the orchestrator and returns `{ task, traces }`
  - Requires dev headers; writes `AgentTask`/`AgentTrace` rows under current tenant
- `POST /api/dev/notifications/verification` and `/invite` (in `DevNotificationsEndpoints.cs`)
  - Enqueues a verification or invite email via the notifications queue for E2E testing against Mailhog
  - Requires dev headers
  - E2E (Notif‑20): Verified full outbox path in Development — enqueue → DB row → SMTP (Mailhog) → `Sent`. Tests gate EF transactions behind `Database.IsRelational()` to support InMemory provider.

Agent task endpoints (in `AgentTasksEndpoints.cs`):

- `POST /api/agent-tasks`
  - Validates `agentId` and non-empty `input` JSON
  - Captures dev headers into `RequestTenant`/`RequestUser`
  - Persists `AgentTask` with `Pending` status, enqueues its id
  - Development-only test hooks: optional `x-test-enqueue-delay-ms`, `x-test-suppress-enqueue`
  - Returns `201 Created` with `AgentTaskSummary`
- `GET /api/agent-tasks/{id}`
  - Returns `AgentTaskDetails` (status, timestamps, optional result/error)
  - With `?includeTraces=true`, returns `{ task, traces }` where traces are ordered `AgentTraceDto[]`
- `GET /api/agent-tasks`
  - Lists `AgentTaskSummary[]` ordered by `CreatedAt DESC`
  - Optional filters: `status` (case-insensitive), `agentId`, `from`, `to`, `q` (free-text)
  - Free-text search is provider-aware: Npgsql uses `EF.Functions.ILike`, others fall back to case-insensitive `Contains`
  - Paging via `take`/`skip`; sets `X-Total-Count` header with total before paging

### Swagger/OpenAPI

- Swagger JSON: `GET /swagger/v1/swagger.json`
- Swagger UI: `GET /swagger/`
- Security scheme: API key ("DevHeaders") — uses `x-dev-user` (with `x-tenant` also expected by auth handler)

## Database and EF Core

- Provider: Npgsql (PostgreSQL 16 in dev)
- Default schema: `app`
- Migrations: `apps/api/Migrations/` (includes `20250911124311_s1_09_agent_runtime` and others)
- Auto-migration in Development/Test: `Database.Migrate()` at startup
- RLS strategy: set `app.tenant_id` via middleware; DB-side policies/init in `infra/initdb` and EF migrations

Agent runtime tables (key): `agents`, `agent_tasks`, `agent_traces` with:

- Unique index on `agent_traces(task_id, step_number)`
- Check constraints: `step_number >= 1`, `duration_ms >= 0`

Agent fields (highlights):

- `IsEnabled boolean default true` — used to soft-disable agents. API list endpoint hides disabled by default unless `includeDisabled=true`.

Auth/Multi-tenant schema updates (MVP prep):

- Tenants use `name` as a slug with a unique index (`app.tenants(name)`), aligning with existing seed and dev headers.
- Foreign keys enforce integrity:
  - `app.memberships(user_id) → app.users(id)` (cascade delete)
  - `app.memberships(tenant_id) → app.tenants(id)` (cascade delete)
  - `app.lessons(tenant_id) → app.tenants(id)` (cascade delete)

Invitations (Auth‑01):

- Table: `app.invitations`
  - Columns: `id uuid PK`, `tenant_id uuid NOT NULL`, `email text NOT NULL`, `role int NOT NULL`, `token text UNIQUE NOT NULL`, `expires_at timestamptz NOT NULL`, `invited_by_user_id uuid NULL`, `accepted_at timestamptz NULL`, `created_at timestamptz DEFAULT now()`
  - FKs: `tenant_id → app.tenants(id)` (CASCADE), `invited_by_user_id → app.users(id)` (SET NULL)
  - Indexes: unique on `token`; functional unique index `UX_invitations_tenant_email_ci` on `(tenant_id, lower(email))` for case-insensitive per‑tenant de‑dup; supporting index `(tenant_id, expires_at)`

Invite acceptance (Auth‑08):

- Endpoint: `POST /api/invites/accept { token }` (authorized)
  - Validates token and expiry; email must match the signed-in user.
  - Creates a `memberships` row for the invite’s tenant with the invite’s role (under RLS via tenant context), idempotent if membership already exists.
  - Marks the invitation `accepted_at`.
- Web: `/invite/accept` SSR route handles signed-in vs. redirect-to-login and calls the API; invite emails link to this route.

Auth‑02 — Passwords & Signup

- Password hashing: Argon2id with per‑user random salt and a configurable pepper (`Auth:PasswordPepper`). Stored fields on `users`: `password_hash text`, `password_salt bytea`, `password_updated_at timestamptz`.
- Anonymous signup endpoint: `POST /api/auth/signup`
  - Input: `{ email, password, inviteToken? }`
  - Behavior: creates `users` row with hashed password; if `inviteToken` is present and valid, creates a `memberships` row for the invite’s tenant with the invite’s role. Otherwise, ensures a personal tenant slug `{localpart}-personal` exists and creates an Owner membership there. Membership insertion is executed under tenant RLS by setting `app.tenant_id` within a transaction.
  - Output: `201 Created` with `{ id, email, tenant: { id, name } }` where tenant reflects either the invite’s tenant or the created/ensured personal tenant.

## Agent Runtime (v1)

Domain types (`apps/api/Domain/Agents/`):

- `Agent` — fields: Id, Name (<=120), SystemPrompt (text), ToolAllowlist (string[]/jsonb), Model (<=80), Temperature [0..2], MaxSteps [1..50], CreatedAt/UpdatedAt
- `AgentTask` — Id, AgentId (required), InputJson (required), Status, timestamps, optional ResultJson/ErrorMessage
- `AgentTrace` — Id, TaskId (required), StepNumber >= 1, Kind, Name (<=120), InputJson/OutputJson (required), DurationMs/PromptTokens/CompletionTokens (>= 0), CreatedAt
- Enums: `AgentStatus`, `TraceKind`

Validation utilities (`apps/api/Application/Validation/Guard.cs`):

- `NotNull`, `NotNullOrWhiteSpace`, `InRange`, `MaxLength` — used in constructors to enforce invariants with clear param names

EF Core configuration (`apps/api/Infrastructure/Configurations/*`):

- Tables under schema `app`, PostgreSQL-specific mappings (jsonb, text), check constraints, default timestamps, and indexes
- `AppDbContext` applies them via `ApplyConfigurationsFromAssembly(...)`

Registry (`apps/api/Application/Agents/AgentRegistry.cs`):

- Read-only in-memory list of two deterministic agents (v1), exposed for quick list/lookup

Runtime orchestration (`Application/Agents/Runtime`):

- `AgentOrchestrator` drives a loop of model decisions and optional tool calls.
  - Uses `IModelAdapter` (`MockModelAdapter`) for deterministic decisions in dev.
  - Enforces allowlist on tools and guardrails (empty tool name, max steps).
  - Step numbering: reserves two step numbers per iteration (model at N, tool at N+1), then advances to N+2 to satisfy the unique index on traces.
- `TraceWriter` persists traces with clamped non-negative durations/token counts and one-time retry on unique key conflicts.
- `ToolRegistry` wires deterministic tools: `web.search`, `db.query`, `fs.write`.

Queue and worker (`Application/Agents/Queue`):

- `InMemoryAgentTaskQueue` exposes a process-local `Channel<Guid>` with `SingleReader=true` and backpressure.
- `AgentTaskWorker` is a hosted service consuming the channel’s `Reader` so endpoints and worker share the same instance (DI singleton mapping for concrete + interface).
- Processing semantics:
  - Idempotent: only `Pending` tasks are transitioned to `Running`.
  - Graceful cancellation: on shutdown, any in-flight `Running` task is marked `Canceled`.
  - Transient resilience: retry orchestrator once on timeouts/deadlocks before failing.
  - Dequeue/load race mitigation: brief retry loop to read the task after enqueue (handles read-before-commit).
  - Logs dequeued count and last in-flight task id on stop.

Agent resolution (`Application/Agents/AgentStore.cs`):

- Registered as scoped service; used by `AgentTaskWorker` to resolve agents.
- Lookup strategy is DB-first by Id, then falls back to static `AgentRegistry.FindById(id)`.

## Notifications (Email)

Components (`apps/api/App/Notifications/*`):

- IEmailQueue + EmailQueue — in-memory channel used by the background dispatcher.
- EmailDispatcherHostedService — consumes queue, renders templates, sends emails with retry/backoff.
- ITemplateRenderer — `ScribanTemplateRenderer` composes subject/text/html bodies.
- IEmailSender — providers: `SmtpEmailSender` (dev), `SendGridEmailSender` (prod/real), and `NoopEmailSender` fallback.
- NotificationEnqueuer — helpers to enqueue verification and invite emails with correct absolute links.

Configuration keys (Options bound via .NET configuration):

- Email:Provider — "smtp" (default in Development) or "sendgrid" (default in non-Dev if unset)
- Email:WebBaseUrl — base URL for absolute links (default "http://localhost:3000")
- Email:FromAddress — default sender address (default "no-reply@appostolic.local")
- Email:FromName — default sender name (default "Appostolic")
- Smtp:Host — SMTP host (default 127.0.0.1 in Development)
- Smtp:Port — SMTP port (default 1025 in Development; Mailhog)
- Smtp:User / Smtp:Pass — optional credentials
- SendGrid:ApiKey — required when provider=sendgrid in Production

Environment variables (double-underscore syntax maps to nested keys):

- Email**Provider, Email**WebBaseUrl, Email**FromAddress, Email**FromName
- Smtp**Host, Smtp**Port, Smtp**User, Smtp**Pass
- SendGrid\_\_ApiKey (preferred)
- Compatibility shim: if legacy SENDGRID_API_KEY is set and SendGrid:ApiKey is empty, it is used automatically at startup.

Safety/guards:

- In Production, Email:Provider=sendgrid requires a non-empty SendGrid:ApiKey or the app fails fast at startup with a clear message.
- Development defaults to SMTP/Mailhog (127.0.0.1:1025) so no secrets are required for local testing.

Observability:

- Metrics: `email.sent.total` and `email.failed.total` counters (tagged by email kind) exposed via OTEL Meter "Appostolic.Metrics".
- Logs: Dispatcher adds correlation fields to scopes when present on the message data: `email.userId`, `email.tenantId`, `email.inviteId` (plus `email.tenant`, `email.inviter` fallbacks). In Development, logs/metrics are also exported to console.
- Privacy (Notif-25): Recipient emails are redacted in all logs/scopes/providers (e.g., `u***@example.com`). Metrics include only non-PII tags (kind), with no raw emails or tokens.

Local dev (Mailhog):

- With the default SMTP provider, emails appear in Mailhog at http://localhost:8025; no extra setup required.
- To test SendGrid locally, export `SendGrid__ApiKey` in your shell and set `Email__Provider=sendgrid` (or leave provider unset in non-Dev). Never commit real API keys.

## Web app (`apps/web`)

- Next.js 14 (App Router); Node runtime for server routes.
- Server-only API proxy routes under `app/api-proxy/*` inject dev headers and avoid CORS:
  - Agents CRUD:
    - `GET/POST /api-proxy/agents` ↔ API `/api/agents`
    - `GET/PUT/DELETE /api-proxy/agents/{id}` ↔ API `/api/agents/{id}`
    - `GET /api-proxy/agents/tools` ↔ API `/api/agents/tools`
  - `GET /api-proxy/dev/agents` → API `/api/dev/agents`
  - `POST /api-proxy/dev/notifications/verification` → API `/api/dev/notifications/verification`
  - `POST /api-proxy/dev/notifications/invite` → API `/api/dev/notifications/invite`
  - `POST /api-proxy/agent-tasks` → API `/api/agent-tasks`
  - `GET /api-proxy/agent-tasks/{id}?includeTraces=true` → API `/api/agent-tasks/{id}`
  - Tenants/Members (auth):
    - `GET /api-proxy/tenants/{tenantId}/members` ↔ API `/api/tenants/{tenantId}/members`
    - `PUT /api-proxy/tenants/{tenantId}/members/{userId}` ↔ API `/api/tenants/{tenantId}/members/{userId}`
    - `DELETE /api-proxy/tenants/{tenantId}/members/{userId}` ↔ API `/api/tenants/{tenantId}/members/{userId}`
- Env validation in `src/lib/serverEnv.ts` for `NEXT_PUBLIC_API_BASE`, `DEV_USER`, `DEV_TENANT`.
- Dev UI: `/dev/agents` (SSR) lists agents and renders a client `AgentRunForm` to create a task and live-poll details+traces via `useTaskPolling` (750ms until terminal).
- Agent Studio: `/studio/agents` (list), `/studio/agents/new` (create), `/studio/agents/[id]` (edit)
  - Uses `AgentForm` (create/edit) and `AgentsTable` (list)
  - Create/Update payload includes `isEnabled` and `toolAllowlist` selections
  - List defaults to enabled-only agents; UI can be extended to show disabled
- Result panel shows parsed JSON on success; error message shown on failure/cancel; recent run ids tracked client-side.
- Depends on `@appostolic/sdk` for general types/helpers; current panel uses direct fetch via server proxies.

### Test coverage (Phase 0 additions)

- Middleware gating tests ensure unauthenticated users are redirected to `/login` and authenticated users are kept away from `/login`.
- Logout page smoke test verifies `signOut({ redirect: false })` is called and the client navigates to `/login`.
- API proxy smokes for AgentTasks:
  - `GET /api-proxy/agent-tasks` returns 401 when unauthenticated and 200 when headers are present (mocked).
  - `POST /api-proxy/agent-tasks` returns 201 and forwards the `Location` header from the API.

## SDK and OpenAPI (`packages/sdk`)

- TS SDK scaffolding under `packages/sdk`
- OpenAPI generated from compiled API via Swashbuckle CLI (output at `packages/sdk/openapi.json`)
- SDK currently provides minimal fetch helpers/types; can be expanded via codegen

## Infra (`infra/`)

- Docker Compose services: Postgres, Redis, MinIO, Qdrant, Mailhog, pgAdmin
- Init SQL: `infra/initdb/init.sql` sets extensions, schemas, GUC helpers
- Devcontainer configuration available

### Dev credentials (local only)

From `infra/docker/.env` (for local development):

- Postgres:
  - Host: `localhost`
  - Port: `55432`
  - Database: `appdb`
  - User: `appuser`
  - Password: `apppassword`
- Redis:
  - Host: `localhost`
  - Port: `6380`
- MinIO:
  - Root User: `minio`
  - Root Password: `minio123`
  - API: `http://localhost:9002`
  - Console: `http://localhost:9003`
- Mailhog:
  - UI: `http://localhost:8025`
  - SMTP: `1025`
- Qdrant:
  - Host: `localhost`
  - Port: `6334`
- pgAdmin:
  - UI: `http://localhost:8081`
  - Email: `admin@example.com`
  - Password: `admin123`

Dev auth for API testing:

- Send headers on `/api/*` requests:
  - Default seed: `x-dev-user: kevin@example.com`, `x-tenant: kevin-personal`
  - If you use different seed data, adjust these accordingly. Some older docs may still reference `dev@example.com` / `acme`.

## Troubleshooting

- Swagger shows JSON instead of UI: use `/swagger/` or rely on `/swagger` redirect; ensure `UseSwaggerUI` configured and only registered once
- 401 on `/api/*`: send `x-dev-user` and `x-tenant` headers; if you haven’t seeded, run `make seed` (or `make bootstrap`)
- Tenant scoping not applied: verify `tenant_id` claim exists (set by dev auth) or use `X-Tenant-Id` for legacy `/lessons`
- Postgres connection errors: ensure Docker is up and port `55432` matches env

Node/pnpm engines:

- The repo requires `node >=20 <21` (see root `package.json`). Using Node 19 may error with `TypeError: URL.canParse is not a function` via Corepack.
- Fix locally by switching to Node 20.x (e.g., `nvm use 20`) and ensuring Corepack/pnpm use that runtime.

Development-only tools:

- Use `POST /api/dev/tool-call` with `db.query` to inspect tables under schema `app` and to count `agent_tasks`/`agent_traces` without psql.

## Prompt starters (copy into ChatGPT)

- “Expose read-only endpoints for dev agents (already available at `/api/dev/agents`) and consider promoting to `/api/agents` for non-dev contexts. Update Swagger and web to consume the stable path.”
- “Extend EF configurations for Agent runtime with additional indexes and constraints [describe]. Create and apply a migration and confirm defaults in PostgreSQL.”
- “Regenerate OpenAPI and update the TypeScript SDK to include new endpoints (AgentTasks, DevAgents). Update `apps/web` to call through the SDK or keep using server proxies.”
- “Implement AgentTask flows (done: create, get by id with traces, list with filters). Add delete/cancel if needed and write integration tests.”
- “Harden `TenantScopeMiddleware`: ensure GUC set/reset semantics, add diagnostics, and handle exceptions explicitly.”
- “Add integration tests for `/api/lessons` using dev auth and a test Postgres; include test harness and migrations in CI.”

---

Paste the above into ChatGPT when asking for changes so it can reference exact file paths, endpoints, and conventions used here.
