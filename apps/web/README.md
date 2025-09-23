# Web App — Run Agent Panel (S1-09) & Agent Studio (A10)

This Next.js app includes a developer “Run Agent” panel at `/dev/agents` that lets you:

- Select a seeded Agent (from the API’s AgentRegistry)
- Provide JSON input
- Create a task and watch live traces until it finishes

## How the web panel talks to the API

All calls are made server-side through App Router handlers under `app/api-proxy/*` (avoids CORS and keeps credentials server‑only). Development headers were removed; the proxy now forwards a real Bearer JWT derived from the authenticated session. During local development the login page obtains a neutral access + refresh cookie; tenant selection upgrades to a tenant-scoped access token which the proxy attaches as `Authorization: Bearer <token>`.

Routes:

- `GET /api-proxy/dev/agents` → lists seeded agents (Bearer auth)
- `POST /api-proxy/agent-tasks` → creates a new task (Bearer auth)
- `GET /api-proxy/agent-tasks/{id}?includeTraces=true` → fetches task details + traces (Bearer auth)

If a request is made without a valid access token the proxy returns 401 and the client triggers a silent refresh (if a valid refresh cookie exists) or redirects to login.

## Required environment variables

Create `apps/web/.env.local` with:

```dotenv
# Where the API is listening
NEXT_PUBLIC_API_BASE=http://localhost:5198

# (Optional) logging / feature flags can go here
```

Notes:

- After editing `.env.local`, restart the Next.js dev server.
- No user/tenant dev header env vars are required; authentication flows mirror production (login + refresh cookie + Bearer token).

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
- If task creation returns 401/403, ensure your session is valid (try logging out/in) and tenant selection completed.
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

## Agent Studio (A10)

Manage Agents from the UI under `/studio/agents`.

Pages

- `/studio/agents` — server-rendered list of agents using `GET /api-proxy/agents`
  - Disabled agents are grayed out; list shows only enabled by default
- `/studio/agents/new` — create an agent
- `/studio/agents/[id]` — edit an agent

Form features (create/edit)

- Inline validation: name, model, temperature [0..2], maxSteps [1..50]
- Tool selection: checkboxes sourced from `GET /api-proxy/agents/tools`
- System prompt text area with live token estimate (approx. 4 chars/token)
- Enabled toggle: set `isEnabled`; disabled agents remain in DB but are hidden by default from lists
- Save triggers POST or PUT via server proxy with dev headers; toasts on success and navigates back to the list

Run this agent

- Each row and editor header links to `/dev/agents?agentId=<id>` to try the agent in the Run panel.

Server proxy routes

- `GET /api-proxy/agents` — list
- `POST /api-proxy/agents` — create
- `GET /api-proxy/agents/[id]` — details
- `PUT /api-proxy/agents/[id]` — update
- `DELETE /api-proxy/agents/[id]` — delete
- `GET /api-proxy/agents/tools` — tool catalog for allowlist UI

Notes

- Dev headers are attached in all proxy routes. The API prefers DB-defined agents at runtime and falls back to the seeded registry if not found.

- The test is skipped in CI unless `E2E_WEB_ENABLE=1` is set. To run against already-running servers, set `PLAYWRIGHT_WEB_NO_SERVER=1` and provide `WEB_BASE` and `NEXT_PUBLIC_API_BASE` envs.
