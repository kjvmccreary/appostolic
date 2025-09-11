# Appostolic API

## Getting Started / Running Locally

- Install .NET 8 SDK and PNPM
- From repo root, build API: `dotnet build apps/api/Appostolic.Api.csproj`
- Run API (watch): `make api` (http://localhost:5198)
- Swagger UI: http://localhost:5198/swagger/
- Health: `GET /health`

---

## Agent Runtime v1 — Data Model & Runtime

### Schemas & Defaults

- Default PostgreSQL schema: `app`
- EF migrations folder: `apps/api/Migrations/`
- Auto-migration on startup in Development/Test via `Database.Migrate()`

### Agent Runtime Domain

Mini reference table:

| Entity       | Key fields / notes                                                                                                                   |
| ------------ | ------------------------------------------------------------------------------------------------------------------------------------ |
| `Agent`      | Id, Name (<=120), Model (<=80), Temperature [0..2], MaxSteps [1..50], SystemPrompt (text), ToolAllowlist (jsonb)                     |
| `AgentTask`  | Id, AgentId, InputJson (text), Status (stored as string), ResultJson/ErrorMessage (text), Created/Started/Finished timestamps        |
| `AgentTrace` | Id, TaskId, StepNumber >=1, Kind (enum, stored as int), Name (<=120), InputJson/OutputJson (text), DurationMs >=0, tokens, CreatedAt |

Details:

- Entities: Agent, AgentTask, AgentTrace with key fields, constraints, and enum storage (AgentStatus as string; TraceKind as int)
- JSONB: `Agent.ToolAllowlist`, long text columns (`SystemPrompt`, `InputJson`, `OutputJson`, `ResultJson`, `ErrorMessage`)
- Check constraints: Temperature [0..2], MaxSteps [1..50], StepNumber >=1, DurationMs >=0
- Indexes: `Agent(Name unique)`, `AgentTask(AgentId, CreatedAt DESC)`, `AgentTask(Status, CreatedAt DESC)`, `AgentTrace(TaskId, StepNumber unique)`

Source references:

- Configurations: `apps/api/Infrastructure/Configurations/*.cs`
- Entities: `apps/api/Domain/Agents/*.cs`
- Registry (read-only, v1): `apps/api/Application/Agents/AgentRegistry.cs`

### Auth & Tenant Scoping (Dev)

- Send headers on `/api/*` requests:
  - `x-dev-user: dev@example.com`, `x-tenant: acme`
- Tenant scope: middleware starts a tx and sets `app.tenant_id` via `set_config(...)`; skips `/health*` and `/swagger*`.
  - Middleware: `apps/api/App/Infrastructure/MultiTenancy/TenantScopeMiddleware.cs`

### Endpoints & OpenAPI

- Swagger UI: http://localhost:5198/swagger/
- Swagger JSON: http://localhost:5198/swagger/v1/swagger.json
- Current dev endpoints under `/api` (see `apps/api/App/Endpoints/V1.cs`):
  - `GET /api/me`, `GET /api/tenants`, `GET/POST /api/lessons`
- Upcoming Agent Runtime endpoints will follow the same auth pattern (e.g., `/api/agent-tasks`, `/api/agent-tasks/{id}`)

### Local Run & Make targets

- Quick links: API base `http://localhost:5198`, Health `GET /health`
- Make targets: `make api`, `make migrate`, `make bootstrap`, `make sdk`, `make web`, `make mobile`

### cURL smoke test (dev headers)

```bash
curl -s http://localhost:5198/api/me \
  -H "x-dev-user: dev@example.com" -H "x-tenant: acme" | jq .
```

Swagger UI note: If you see JSON at `/swagger`, open `/swagger/` (trailing slash).

### Cross-links

- Architecture snapshot: `SnapshotArchitecture.md`
- DB init SQL: `infra/docker/initdb/init.sql`

---

## Dev Tool Invoker

Development-only helper to smoke-test tool calls via HTTP. Available only in Development environment and respects existing dev headers authentication and tenant scoping.

Example:

```bash
curl -s http://localhost:5198/api/dev/tool-call \
  -H "x-dev-user: dev@example.com" -H "x-tenant: acme" \
  -H "Content-Type: application/json" \
  -d '{ "name":"web.search", "input": { "q":"intro", "take": 3 } }' | jq .
```

The endpoint resolves the tool from the registry and executes it with a synthetic context, returning the ToolCallResult (success, output, error, durationMs).

### Dev Agents Demo (Development only)

Run the seeded ResearchAgent inline; returns the created task and its traces:

```bash
curl -s http://localhost:5198/api/dev/agents/demo \
  -H "x-dev-user: dev@example.com" -H "x-tenant: acme" \
  -H "Content-Type: application/json" \
  -d '{ "topic": "Intro to EF Core" }' | jq '.task.status, .traces | length'
```

## Agent Tasks API (S1-09)

Use these endpoints to create tasks for a deterministic agent and optionally fetch traces.

Important:

- Include dev headers on all `/api/*` calls: `x-dev-user` and `x-tenant`.
- Swagger UI at http://localhost:5198/swagger/ (note trailing slash).
- For now, tasks will be created with `Pending` status; A09-05 will connect a queue worker to execute tasks.

Concrete IDs:

- ResearchAgent (deterministic): `11111111-1111-1111-1111-111111111111`

Examples:

```bash
# Create
curl -s http://localhost:5198/api/agent-tasks \
  -H "x-dev-user: kevin@example.com" -H "x-tenant: acme" \
  -H "Content-Type: application/json" \
  -d '{ "agentId":"11111111-1111-1111-1111-111111111111", "input": { "topic": "Beatitudes" } }' | jq .

# Get w/ traces
curl -s "http://localhost:5198/api/agent-tasks/<TASK_ID>?includeTraces=true" \
  -H "x-dev-user: kevin@example.com" -H "x-tenant: acme" | jq .

# List (may be empty if none Running yet)
curl -s "http://localhost:5198/api/agent-tasks?status=Running&take=10&skip=0" \
  -H "x-dev-user: kevin@example.com" -H "x-tenant: acme" | jq .
```

Tip: In VS Code’s Run and Debug panel, use the task "Dev: web+api+mobile" to start the API (and web/mobile) with `pnpm dev`. Or use `make api` to run just the API at http://localhost:5198.

## Agent Orchestrator & Traces

High-level execution sequence (one iteration):

Model → Tool → FinalAnswer

- The model plans the next action (either UseTool or FinalAnswer).
- If it plans a tool, we record a Model trace at step N and a Tool trace at step N+1.
- When the model returns FinalAnswer, the task completes successfully.

### Example AgentTrace rows (redacted)

Model step (step_number: 1):

```json
{
  "kind": "Model",
  "name": "model",
  "stepNumber": 1,
  "inputJson": {
    "system": "...system prompt...",
    "context": {
      "input": { "q": "intro" },
      "scratchpad": {
        /* ... */
      }
      // lastTool omitted on first step
    }
  },
  "outputJson": {
    "action": "UseTool",
    "PromptTokens": 105,
    "CompletionTokens": 20,
    "Rationale": "planned tool use"
  },
  "durationMs": 3
}
```

Tool step (step_number: 2):

```json
{
  "kind": "Tool",
  "name": "web.search",
  "stepNumber": 2,
  "inputJson": { "q": "intro", "take": 1 },
  "outputJson": { "results": [{ "title": "Intro to EF Core", "url": "...", "snippet": "..." }] },
  "durationMs": 1,
  "PromptTokens": 105,
  "CompletionTokens": 20
}
```

Notes:

- Durations and token counts are clamped to non-negative values.
- A unique index on `(TaskId, StepNumber)` enforces ordering; on rare collision we retry once with `StepNumber++` and log a warning.
- Terminal statuses always set `FinishedAt`; we set `StartedAt` at the first step.

### Dev headers and trace context

- Dev-only authentication uses headers:
  - `x-dev-user: dev@example.com`
  - `x-tenant: acme`
- Tenant/user are carried in telemetry (Activity/log tags) during tool execution for observability. AgentTrace rows themselves only persist tool/model inputs/outputs and timing, not PII.

### See also

- Sprint notes for follow-ups and adjacent work: `Sprint-01-Appostolic.md` (items A09-04, A09-05)
