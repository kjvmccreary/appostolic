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

### Auth & Tenant Scoping

Authentication now uses JWT Bearer tokens only (dev headers removed). Obtain a neutral token by logging in, then (optionally) select a tenant to receive a tenant-scoped access token.

Quick flow (example user already seeded):

```bash
# Login (email/password) → returns neutral access token & refresh cookie
curl -s -X POST http://localhost:5198/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"kevin@example.com","password":"Password123!"}' | jq '{ access, memberships }'

# Select tenant (replace TENANT_ID from memberships array) → returns tenant-scoped access
curl -s -X POST http://localhost:5198/api/auth/select-tenant \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer <NEUTRAL_ACCESS_TOKEN>" \
  -d '{"tenantId":"<TENANT_ID>"}' | jq '{ access }'

# Authenticated request
curl -s http://localhost:5198/api/me \
  -H "Authorization: Bearer <TENANT_ACCESS_TOKEN>" | jq .
```

Tenant scoping is applied by `TenantScopeMiddleware` using claims from the access token; dev headers are rejected with `401 {"code":"dev_headers_removed"}` if still present.

### Endpoints & OpenAPI

- Swagger UI: http://localhost:5198/swagger/
- Swagger JSON: http://localhost:5198/swagger/v1/swagger.json
- Current dev endpoints under `/api` (see `apps/api/App/Endpoints/V1.cs`):
  - `GET /api/me`, `GET /api/tenants`, `GET/POST /api/lessons`
- Upcoming Agent Runtime endpoints will follow the same auth pattern (e.g., `/api/agent-tasks`, `/api/agent-tasks/{id}`)

### Local Run & Make targets

- Quick links: API base `http://localhost:5198`, Health `GET /health`
- Make targets: `make api`, `make migrate`, `make bootstrap`, `make sdk`, `make web`, `make mobile`

### cURL smoke test

```bash
# Assuming you have exported TENANT_ACCESS from the select-tenant step:
curl -s http://localhost:5198/api/me -H "Authorization: Bearer $TENANT_ACCESS" | jq .
```

Swagger UI note: If you see JSON at `/swagger`, open `/swagger/` (trailing slash).

### Cross-links

- Architecture snapshot: `SnapshotArchitecture.md`
- DB init SQL: `infra/docker/initdb/init.sql`

---

## Dev Tool Invoker (Legacy)

The previous dev-header based tool invoker endpoints are deprecated. Future local testing should occur via authenticated agent/task flows or dedicated CLI utilities. Any request attempting to use legacy headers will receive `401 dev_headers_removed`.

### Dev Agents Demo (Legacy)

Inline demo endpoint retained temporarily for Development but no longer accepts dev headers; authenticate with a tenant-scoped Bearer token.

## Agents API (A10)

Manage Agents stored in the database. The runtime resolves agents via AgentStore and prefers DB-defined agents; if an agent ID is not found in the DB, it falls back to the read-only AgentRegistry (seeded agents).

Swagger tag: Agents

Endpoints

- GET /api/agents — list agents
- - By default returns only enabled agents. Add `includeDisabled=true` to include disabled.
- GET /api/agents/{id} — get one
- POST /api/agents — create
- PUT /api/agents/{id} — update
- DELETE /api/agents/{id} — delete
- GET /api/agents/tools — read-only tool catalog for allowlisting in UI

cURL examples (Bearer auth)

```bash
# List agents (enabled only)
curl -s "http://localhost:5198/api/agents?take=50" \
  -H "Authorization: Bearer $TENANT_ACCESS" | jq 'map({ id, name, model, temperature, maxSteps })'

# Include disabled too
curl -s "http://localhost:5198/api/agents?includeDisabled=true&take=50" \
  -H "Authorization: Bearer $TENANT_ACCESS" | jq 'map({ id, name, isEnabled })'

# Create an agent (explicitly disabled example)
curl -s http://localhost:5198/api/agents \
  -H "Authorization: Bearer $TENANT_ACCESS" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Researcher",
    "model": "gpt-4o-mini",
    "temperature": 0.2,
    "maxSteps": 8,
    "systemPrompt": "You are a helpful research assistant.",
  "toolAllowlist": ["web.search", "db.query"],
  "isEnabled": false
  }' | jq '{ id, name }'

# Get details
curl -s http://localhost:5198/api/agents/<AGENT_ID> \
  -H "Authorization: Bearer $TENANT_ACCESS" | jq '{ id, name, toolAllowlist }'

# Update (enable)
curl -s -X PUT http://localhost:5198/api/agents/<AGENT_ID> \
  -H "Authorization: Bearer $TENANT_ACCESS" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Researcher",
    "model": "gpt-4o-mini",
    "temperature": 0.1,
    "maxSteps": 10,
    "systemPrompt": "Focus on credible sources.",
  "toolAllowlist": ["web.search"],
  "isEnabled": true
  }' | jq '{ id, name, temperature, maxSteps }'

# Delete
curl -s -X DELETE http://localhost:5198/api/agents/<AGENT_ID> \
  -H "Authorization: Bearer $TENANT_ACCESS" -i | head -n1

# Tool catalog (read-only)
curl -s http://localhost:5198/api/agents/tools \
  -H "Authorization: Bearer $TENANT_ACCESS" | jq 'map({ name, category, description })'
```

## Agent Tasks API (S1-09)

Use these endpoints to create tasks for a deterministic agent and optionally fetch traces.

Important:

- All `/api/*` calls require a valid Bearer token (JWT). Obtain via login + tenant selection (see Auth section above).
- Swagger UI at http://localhost:5198/swagger/ (note trailing slash).
- Tasks are created `Pending`; background worker processes asynchronously.

Concrete IDs:

- ResearchAgent (deterministic): `11111111-1111-1111-1111-111111111111`

Examples:

```bash
# Create
curl -s http://localhost:5198/api/agent-tasks \
  -H "Authorization: Bearer $TENANT_ACCESS" \
  -H "Content-Type: application/json" \
  -d '{ "agentId":"11111111-1111-1111-1111-111111111111", "input": { "topic": "Beatitudes" } }' | jq .

# Get w/ traces
curl -s "http://localhost:5198/api/agent-tasks/<TASK_ID>?includeTraces=true" \
  -H "Authorization: Bearer $TENANT_ACCESS" | jq .

# List (may be empty if none Running yet)
curl -s "http://localhost:5198/api/agent-tasks?status=Running&take=10&skip=0" \
  -H "Authorization: Bearer $TENANT_ACCESS" | jq .
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

### Token accounting and optional estimated cost

How tokens are counted

- For each Model step, the orchestrator records `PromptTokens` and `CompletionTokens` on the trace. If a model adapter does not supply explicit counts, a deterministic heuristic is used via `TokenEstimator` (roughly 4 ASCII-ish characters per token; ceil(length/4)).
- Tool steps do not contribute tokens.

Roll-up on AgentTask

- `AgentTask.TotalPromptTokens` and `AgentTask.TotalCompletionTokens` are incremented after each Model decision.
- `AgentTask.TotalTokens` is computed as `TotalPromptTokens + TotalCompletionTokens`.

Optional estimated cost

- If pricing is enabled and a model’s price is configured, the orchestrator accumulates an `EstimatedCostUsd` on the task:
  - Input cost = (promptTokens / 1000) × `ModelPricing.Models[model].InputPer1K`
  - Output cost = (completionTokens / 1000) × `ModelPricing.Models[model].OutputPer1K`
- Configure in `appsettings.Development.json` under `ModelPricing`:

```json
"ModelPricing": {
  "Models": {
    "gpt-4o-mini": { "inputPer1K": 0.15, "outputPer1K": 0.6 },
    "gpt-4.1-mini": { "inputPer1K": 0.25, "outputPer1K": 1.0 }
  },
  "enabled": true
}
```

Heuristic nature of estimates

- When the model adapter does not provide exact token counts, estimates come from `TokenEstimator` and may differ from vendor-specific tokenization. Costs are best-effort approximations for development.

API responses

- `GET /api/agent-tasks/{id}` returns totals and `estimatedCostUsd` (when pricing is enabled). The list endpoint includes `totalTokens` on each summary row.

### Dev headers and trace context

- Dev-only authentication uses headers:
  - `x-dev-user: dev@example.com`
  - `x-tenant: acme`
- Tenant/user are carried in telemetry (Activity/log tags) during tool execution for observability. AgentTrace rows themselves only persist tool/model inputs/outputs and timing, not PII.

### See also

- Sprint notes for follow-ups and adjacent work: `Sprint-01-Appostolic.md` (items A09-04, A09-05)

---

## Telemetry (Dev)

What’s enabled in Development

- OpenTelemetry traces and metrics with the Console exporter.

How to run

- Start the API: `make api` (http://localhost:5198)
- Trigger an agent run from the Web Run panel or via cURL (see Agent Tasks examples below).
- Watch the API console for emitted spans and periodic metric summaries.

Enable OTLP export (optional)

- Set an endpoint to send telemetry to an OTLP collector, for example:
  - `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317`

Key spans

- `agent.run`
- `agent.model`
- `tool.*`

Key metrics

- `TasksCreated`
- `TasksCompleted{status}`
- `TaskDurationMs`
- `ModelTokens`
- `ToolDurationMs{tool}`
- `ToolErrors{tool}`

Correlation notes

- Logs include the `traceId` for request/run correlation.
- Spans include tenant and user attributes (from dev headers) for multi-tenant observability.

## Worker Execution (S1-09)

In Development, task processing uses an in-memory queue and a background worker:

- Queue: `InMemoryAgentTaskQueue` (Channel<Guid>)
- Worker: `AgentTaskWorker` (BackgroundService)

Flow: the create endpoint enqueues the task ID → the worker dequeues and runs the orchestrator → the task transitions:

Pending → Running → Succeeded/Failed

Try it:

```bash
# Create a task
TASK=$(curl -s http://localhost:5198/api/agent-tasks \
  -H "x-dev-user: dev@example.com" -H "x-tenant: acme" \
  -H "Content-Type: application/json" \
  -d '{ "agentId":"11111111-1111-1111-1111-111111111111", "input": { "topic": "Beatitudes" } }' | jq -r .id)

# Poll for completion
curl -s "http://localhost:5198/api/agent-tasks/$TASK?includeTraces=true" \
  -H "x-dev-user: dev@example.com" -H "x-tenant: acme" | jq .
```

Note: For production you can later swap `IAgentTaskQueue` for an external broker like RabbitMQ or Azure Storage Queues.
