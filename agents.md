# Appostolic — Architecture Snapshot (2025-09-11)

Product Vision Plan is at Apostolic Product Vision Plan.md

This document captures the current structure, tech stack, and runtime expectations of the Appostolic monorepo so you can paste it into ChatGPT and get precise guidance and prompts for ongoing work.

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

## What's new since last snapshot

- Agents CRUD API (stable path):
  - Added `AgentsEndpoints.cs` with `GET/POST /api/agents`, `GET/PUT/DELETE /api/agents/{id}`.
  - Added `GET /api/agents/tools` to surface a tool catalog for building allowlists in the UI.
- Agent enable/disable:
  - New `Agent.IsEnabled boolean default true` persisted via EF; list endpoint hides disabled agents by default.
  - Pass `includeDisabled=true` on `GET /api/agents` to include disabled records; `POST/PUT` accept `isEnabled`.
- Agent Studio (web):
  - New pages under `/studio/agents`: list, create, edit with `AgentForm` and `AgentsTable`.
  - API proxy routes added for Agents and Tool catalog under `app/api-proxy/agents/*`.
- Agent resolution:
  - Introduced `AgentStore` used by the worker to resolve agents (DB-first, fallback to static registry).
- Database:
  - EF migrations updated to add the `is_enabled` column (default true) on `app.agents`.

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

## Web app (`apps/web`)

- Next.js 14 (App Router); Node runtime for server routes.
- Server-only API proxy routes under `app/api-proxy/*` inject dev headers and avoid CORS:
  - Agents CRUD:
    - `GET/POST /api-proxy/agents` ↔ API `/api/agents`
    - `GET/PUT/DELETE /api-proxy/agents/{id}` ↔ API `/api/agents/{id}`
    - `GET /api-proxy/agents/tools` ↔ API `/api/agents/tools`
  - `GET /api-proxy/dev/agents` → API `/api/dev/agents`
  - `POST /api-proxy/agent-tasks` → API `/api/agent-tasks`
  - `GET /api-proxy/agent-tasks/{id}?includeTraces=true` → API `/api/agent-tasks/{id}`
- Env validation in `src/lib/serverEnv.ts` for `NEXT_PUBLIC_API_BASE`, `DEV_USER`, `DEV_TENANT`.
- Dev UI: `/dev/agents` (SSR) lists agents and renders a client `AgentRunForm` to create a task and live-poll details+traces via `useTaskPolling` (750ms until terminal).
- Agent Studio: `/studio/agents` (list), `/studio/agents/new` (create), `/studio/agents/[id]` (edit)
  - Uses `AgentForm` (create/edit) and `AgentsTable` (list)
  - Create/Update payload includes `isEnabled` and `toolAllowlist` selections
  - List defaults to enabled-only agents; UI can be extended to show disabled
- Result panel shows parsed JSON on success; error message shown on failure/cancel; recent run ids tracked client-side.
- Depends on `@appostolic/sdk` for general types/helpers; current panel uses direct fetch via server proxies.

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
