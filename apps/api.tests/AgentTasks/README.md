# AgentTasks Integration Tests

This folder contains deterministic integration tests for the AgentTasks API. Tests run the API in Development using `WebApplicationFactory<Program>` so background hosted services (e.g., `AgentTaskWorker`) are active.

## What these tests cover

- Cancel: Pending → 202 Accepted with terminal Canceled; terminal tasks → 409 Conflict
- Retry: Terminal tasks → 201 Created with a new task cloned and enqueued
- List: Filters `status`/`agentId`/`from`/`to`/`q`, `CreatedAt DESC` ordering, and `X-Total-Count` header

## Test environment

- Environment: Development (ensures background worker runs)
- Auth: Tests obtain JWT access tokens via helpers (`AuthTestClientFlow.LoginAndSelectTenantAsync`) which perform real login + tenant selection against in-memory seeded credentials. Legacy dev headers were removed; any attempt to send them results in 401 `{ code: "dev_headers_removed" }`.
- Data provider: EF Core InMemory (fast and process-local)

## Determinism helpers (Development-only)

To avoid races with the background worker, the create endpoint supports test-only headers (active in Development only):

- `x-test-enqueue-delay-ms: <int>`
  - Delays enqueue by N milliseconds after persisting the `Pending` task
- `x-test-suppress-enqueue: true`
  - Skips enqueue entirely; the task remains `Pending` until the test cancels or otherwise updates it

These headers are ignored outside Development.

## Test base helpers

Provided by `AgentTasksTestBase`:

- `CreateTaskAsync(Guid agentId, object input, int? enqueueDelayMs = null, bool suppressEnqueue = false)`
  - Creates a task via POST `/api/agent-tasks` with optional delay/suppression
- `GetTaskAsync(Guid id, bool includeTraces = false)`
  - Gets task details (optionally `{ task, traces }` when `includeTraces=true`)
- `WaitUntilAsync(Guid id, Func<string, bool> isTerminal, int timeoutMs = 7500, int pollMs = 250)`
  - Polls until the predicate returns true or timeout
- `ClearAllTasksAsync()`
  - Removes all `AgentTask` and `AgentTrace` rows from the InMemory DB to isolate tests

## Provider-agnostic free-text search

The list endpoint uses a provider-aware `q` filter:

- On Npgsql (PostgreSQL): `EF.Functions.ILike`
- On non-Npgsql providers (e.g., InMemory): case-insensitive `Contains`

Tests validate `q` matches input JSON and request user and that the `X-Total-Count` header is set.

## Run the tests

From the repo root:

```bash
# Run AgentTasks tests only
dotnet test apps/api.tests/Appostolic.Api.Tests.csproj -c Debug --filter FullyQualifiedName~Appostolic.Api.Tests.AgentTasks

# Run entire test project
dotnet test apps/api.tests/Appostolic.Api.Tests.csproj -c Debug
```

## Troubleshooting

- 401 Unauthorized: ensure the factory sets dev headers; tests should use the provided HttpClient
- Flaky cancel Pending: use `suppressEnqueue: true` in `CreateTaskAsync` to guarantee Pending state
- InMemory provider errors on `ILike`: use the provider-agnostic fallback; already implemented in the API
