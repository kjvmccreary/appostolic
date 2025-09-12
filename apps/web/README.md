# Web App — Run Agent Panel (S1-09)

This Next.js app includes a developer “Run Agent” panel at `/dev/agents` that lets you:

- Select a seeded Agent (from the API’s AgentRegistry)
- Provide JSON input
- Create a task and watch live traces until it finishes

## How the web panel talks to the API

All calls are made server-side through App Router proxy routes under `app/api-proxy/*` to avoid CORS and to attach development headers automatically:

- `GET /api-proxy/dev/agents` → lists seeded agents
- `POST /api-proxy/agent-tasks` → creates a new task
- `GET /api-proxy/agent-tasks/{id}?includeTraces=true` → fetches task details + traces

These proxy routes forward requests to the API base URL and inject required dev headers so the API authenticates the request in Development:

- `x-dev-user: ${DEV_USER}`
- `x-tenant: ${DEV_TENANT}`

The values come from server-only env vars, validated at startup in `src/lib/serverEnv.ts`.

## Required environment variables

Create `apps/web/.env.local` with:

```dotenv
# Where the API is listening
NEXT_PUBLIC_API_BASE=http://localhost:5198

# Dev header auth (used by server proxy routes only)
DEV_USER=kevin@example.com
DEV_TENANT=kevin-personal
```

Notes:

- After editing `.env.local`, restart the Next.js dev server.
- These values are read by the server proxy route handlers and are not exposed to the browser.

## End-to-end flow

1. List agents (SSR)

- Page: `/dev/agents` does a server-side fetch of `/api-proxy/dev/agents` to populate the agent dropdown.

2. Create a task (client → server route → API)

- POST to `/api-proxy/agent-tasks` with body `{ agentId, input }`.
- The server route calls the API with the dev headers; API returns `201` and a task `id`.

3. Poll task + traces until terminal status

- A client hook polls `/api-proxy/agent-tasks/{id}?includeTraces=true` every 750ms.
- Status: `Pending → Running → Succeeded|Failed|Canceled`.
- Traces accumulate while running. On success, the result JSON is shown; on failure/cancel, the error message is displayed.

### Tokens and estimated cost in the UI

- Above the Traces table, the Run panel shows summary badges when available:
  - Total tokens: overall `prompt + completion` for the task
  - Prompt / Completion: per-type totals
  - Est. cost: shown only when the API includes a non-null `estimatedCostUsd` (i.e., backend pricing is enabled and a model price is configured)
- In the Traces table, Model rows display `promptTokens / completionTokens`. Tool rows leave the tokens column blank.

4. Recent runs section

- The page lists the most recent task IDs for quick reference.

## Screenshots

TODO: Add screenshots of:

- Agents dropdown and JSON input
- Live traces table
- Result panel and status badge

## Troubleshooting

- If `/dev/agents` fails to load agents, verify `.env.local` and that the API is running.
- If task creation returns 401/403, double-check `DEV_USER` and `DEV_TENANT` match seeded dev values on the API.
- If polling never reaches a terminal state, check the API Worker logs; the ResearchAgent may hit MaxSteps (this is acceptable in dev and still validates the pipeline).

## End-to-end (Playwright) smoke test

We ship a simple Playwright test that drives the Run Agent page and verifies token totals and optional estimated cost render after completion.

- Test file: `tests/e2e/run-agent.spec.ts`
- Config: `playwright.config.ts`

Run locally:

```bash
pnpm --filter @appostolic/web i
pnpm --filter @appostolic/web exec npx -y playwright install --with-deps

# Ensure API base and dev headers are set for server proxy routes
cp apps/web/.env.local.example apps/web/.env.local # then edit if needed

# One command will start API (5198) + Web (3000) and run tests
pnpm --filter @appostolic/web e2e
```

CI note:

- The test is skipped in CI unless `E2E_WEB_ENABLE=1` is set. To run against already-running servers, set `PLAYWRIGHT_WEB_NO_SERVER=1` and provide `WEB_BASE` and `NEXT_PUBLIC_API_BASE` envs.
