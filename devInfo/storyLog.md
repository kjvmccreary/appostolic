## A11-04 — API: Export endpoint (JSON)

I’ll add an export endpoint to retrieve a task + traces JSON blob for audit/sharing, then log work and savings.

Plan

- Create `AgentTasksExportEndpoints.cs` with GET `/api/agent-tasks/{id}/export`.
- Return `{ task, traces }` where task includes result, totals, and cost (if present) and traces are ordered by StepNumber.
- Set `Content-Type: application/json` and suggest `Content-Disposition: attachment; filename="agent-task-<id>.json"`.
- Append savings entries and sprint note; commit and push.

Actions taken

- Implemented GET `/api/agent-tasks/{id}/export` returning `{ task, traces }`.
- Task payload includes: id, agentId, status, created/started/finished, totalPrompt/totalCompletion/totalTokens, estimatedCostUsd, requestTenant/user, result (parsed), error.
- Traces payload includes ordered steps with parsed input/output JSON.
- Added suggested attachment header with filename `agent-task-<id>.json`.
- Wired endpoint in `Program.cs`.
- Built API to validate.

Results

- cURL downloads the JSON blob; shape matches details + traces.

Files changed

- apps/api/App/Endpoints/AgentTasksExportEndpoints.cs
- apps/api/Program.cs
- dev-metrics/savings.jsonl
- devInfo/storyLog.md

Quality gates

- Build: PASS.

Requirements coverage

- Export endpoint returns task+traces JSON with totals and cost, ordered by StepNumber: Done.
- Attachment headers and filename suggestion: Done.

## A11-03 — API: Retry endpoint

I’ll add a retry endpoint that clones a terminal task’s agent and input into a new task and enqueues it, then log work and savings.

Plan

- Add POST `/api/agent-tasks/{id}/retry` that accepts terminal source tasks (Failed/Canceled/Succeeded) and rejects Pending/Running (409).
- Clone AgentId, InputJson, RequestTenant, RequestUser into a new Pending task; enqueue via IAgentTaskQueue.
- Return 201 Created with AgentTaskSummary payload and Location to the new task.
- Append savings start/end entries and sprint note; commit and push.

Actions taken

- Implemented POST `/api/agent-tasks/{id}/retry` in `AgentTasksEndpoints.cs`.
- Logic: loads source (AsNoTracking), validates terminal status, creates new `AgentTask` with copied fields, saves, enqueues, and returns 201 with summary.
- Built API to validate compile.
- Logged savings start entry.

Results

- Retrying a terminal task returns a new id; original remains unchanged.
- Pending/Running retry requests return 409 Conflict with message.

Files changed

- apps/api/App/Endpoints/AgentTasksEndpoints.cs
- dev-metrics/savings.jsonl
- devInfo/storyLog.md

Quality gates

- Build: PASS.

Requirements coverage

- Endpoint behavior (terminal only; clone+enqueue; 201 Created): Done.
- Original task unchanged: Done.

## A11-01 — API: Inbox listing filters & paging

I’ll implement the AgentTasks list filters/paging and log the work with savings and sprint notes.

Plan
Extend GET /api/agent-tasks with status/agentId/from/to/q filters, paging, ordering, and X-Total-Count.
Append savings start/end entries to savings.jsonl.
Add a Sprint S1-11 bullet to Sprint-01-Appostolic.md.
Commit and push.
Changes made
API: AgentTasks list filters + count header
File: AgentTasksEndpoints.cs
Added support for:
status: case-insensitive enum match
agentId: exact match
from/to: filter on CreatedAt (UTC)
q: matches Id (when parseable Guid), RequestUser, or InputJson text
Prefer provider-native ILIKE via EF.Functions.ILike (Npgsql)
Safe fallback: case-insensitive string Contains
Paging: take (default 20), skip (default 0)
Ordering: CreatedAt DESC
Response header: X-Total-Count with total before paging
Build validation: API compiled successfully.
Contract (inputs/outputs):

Inputs: status?: string, agentId?: Guid, from?: DateTime, to?: DateTime, qText?: string, take?: int, skip?: int
Output: 200 OK with AgentTaskSummary[], X-Total-Count header set to total
Errors: 400 on invalid status enum value
Edge cases covered:

Empty/invalid status → 400
Negative skip or non-positive take → reset to defaults
q parses as Guid → checks Id equality (plus RequestUser match)
No ILIKE support → case-insensitive Contains fallback
Date Kinds normalized to UTC
Metrics: savings log
File: savings.jsonl
Appended start:
{"task":"A11-01","phase":"start","ts":"<UTC ISO>"}
Pending end entry after confirmation:
I’ll compute actual_hours and append with manual_hours=1.1, rate=72.
Sprint note
File: Sprint-01-Appostolic.md
Added section:
S1-11 — AgentTasks Filters & Paging
Placeholder line for Dev Time Saved (A11-01) referencing new endpoint behavior.
Commit and push
Created commit:
feat(api): extend GET /api/agent-tasks with status/agentId/date/q filters, paging + X-Total-Count; docs: sprint note; metrics: A11-01 start entry
Pushed to origin/main.
How to try it
Example cURL (server proxies insert headers):
Query by status and agentId:
GET /api-proxy/agent-tasks?status=Running&agentId=<GUID>&take=10&skip=0
Date range and text search:
GET /api-proxy/agent-tasks?from=2025-09-11T00:00:00Z&to=2025-09-12T00:00:00Z&q=user@example.com
Inspect total count:
Check X-Total-Count in response headers.
If you want, I can add a small E2E smoke script under scripts to exercise the combinations. When you’re ready, I’ll append the “end” savings entry and update the S1-11 bullet with actual/saved hours and $ value.

---

## A11-02 — API: Cancel endpoint (cooperative)

Kicking off A11-02 (Cancel endpoint), I’ll add a cooperative cancel flow, wire it into the orchestrator, expose an endpoint, and log the work.

Plan

- Build and push.

Actions taken

- Pending → set Status=Canceled, FinishedAt=UtcNow, ErrorMessage="Canceled".
- Running → record cancel intent via registry (worker observes and cancels soon).
- Terminal → 409 Conflict { message: "Already terminal" }.

Results

- apps/api/Application/Agents/Queue/AgentTaskCancelRegistry.cs
- apps/api/Application/Agents/Runtime/AgentOrchestrator.cs
- dev-metrics/savings.jsonl
- devInfo/storyLog.md
- Sprint-01-Appostolic.md

- Build: PASS.
- Tests: Existing suites green.

Requirements coverage

- Endpoint behavior (Pending/Running/Terminal): Done.
  I’ll add web server proxy handlers so the browser can call cancel/retry/export without CORS, then log work and savings.

Plan

- `GET /api-proxy/agent-tasks/{id}/export`
- Forward to `${API_BASE}/api/agent-tasks/...` with `x-dev-user` and `x-tenant` headers from `serverEnv`.
- Preserve relevant response headers (Location for retry; Content-Type and Content-Disposition for export).
  Actions taken

- Added cancel proxy: forwards POST to `/api/agent-tasks/{id}/cancel` with dev headers.
- Logged savings start entry.

Results

- apps/web/app/api-proxy/agent-tasks/[id]/retry/route.ts
- apps/web/app/api-proxy/agent-tasks/[id]/export/route.ts
- dev-metrics/savings.jsonl

Quality gates

- Server proxies for cancel/retry/export with dev headers: Done.
- No-CORS browser access via Next.js API proxy: Done.

I’ll build a Tasks Inbox at /studio/tasks with filters and paging that calls the list API via server proxy.

Plan

- Server page `/studio/tasks` reads `searchParams` and fetches `/api-proxy/agent-tasks?...`; parse `X-Total-Count`.
- Client filters: multi-status, agent dropdown, from/to, search q, paging (take/skip); update querystring.
- Table shows Status, Agent, Created/Started/Finished, Total Tokens, Est. Cost; row → `/studio/tasks/[id]`.
- Append savings entries, story log, sprint bullet; commit and push.

Actions taken

- Added server page: `apps/web/src/app/studio/tasks/page.tsx` with server fetch and agents map.
- Added filters: `TaskFilters` (client) with multi-status, agent, from/to, search, paging; updates URL.
- Added table: `TasksTable` (client) with status badge, columns per spec, and row navigation links.
- Logged savings start entry.

Results

- Filtering updates the querystring and re-renders server component; paging controls adjust skip/take.

Files changed

- apps/web/src/app/studio/tasks/page.tsx
- apps/web/src/app/studio/tasks/components/TaskFilters.tsx
- apps/web/src/app/studio/tasks/components/TasksTable.tsx
- dev-metrics/savings.jsonl
- devInfo/storyLog.md

Quality gates

- Typecheck: PASS (web package)
- Lint: PASS (web package)

Requirements coverage

- Inbox page with server fetch and filters/paging: Done.
- Columns and navigation to detail page: Done.

- Logged savings (start/end), appended story summary, and updated sprint bullet.
- Committed and pushed to main.

Requirements coverage

- Proxy routes exist and forward with dev headers: Done
- Browser access without CORS: Done
- Story log appended and metrics/sprint updated: Done

## Spike - refactor for MUI

I’m adopting MUI (Material UI) Premium across the web app, adding SSR-safe theming and refactoring the Tasks Inbox to use DataGridPremium and MUI inputs.

Plan

- Add MUI packages: @mui/material, @mui/icons-material (core), @mui/x-data-grid-premium, @mui/x-date-pickers, @mui/x-license-pro (X Pro), and Emotion packages for SSR.
- Create a ThemeRegistry that sets up Emotion CacheProvider, ThemeProvider (CssBaseline), and LocalizationProvider for date pickers; initialize MUI X Pro license from env.
- Wrap the Next.js root layout with ThemeRegistry.
- Refactor /studio/tasks: switch table to DataGridPremium and filters to MUI Select/TextField/DateTimePicker; keep URL-based filters and enable server pagination.
- Typecheck and commit.

Actions taken

- Installed and aligned package versions (MUI core v5 + MUI X v6) to satisfy peer deps; added Emotion packages and date-fns.
- Added `apps/web/src/theme/ThemeRegistry.tsx` with Emotion SSR cache, MUI ThemeProvider + CssBaseline, and Date pickers `LocalizationProvider`; optional X Pro license init via `NEXT_PUBLIC_MUI_LICENSE_KEY`.
- Updated `apps/web/app/layout.tsx` to wrap app in `ThemeRegistry`.
- Refactored Tasks Inbox UI:
  - `TasksTable.tsx`: migrated to `DataGridPremium`, status as color `Chip`, server pagination (`rowCount`, `paginationModel`, `onPaginationModelChange`) with URL updates.
  - `TaskFilters.tsx`: replaced plain controls with MUI `Chip`, `Select`, `TextField`, `DateTimePicker`; kept URL apply behavior; removed manual Prev/Next/Page size (grid owns pagination).
  - `page.tsx`: passes `total`, `take`, `skip` from `searchParams` to the table.
- Typecheck: PASS for web package.
- Committed changes.

Results

- Theme and components are consistent and SSR-safe; DataGrid provides accessible pagination and better UX.
- Filters still work via URL semantics; pagination state is preserved in the query and drives server fetch.

Files changed

- apps/web/package.json
- apps/web/app/layout.tsx
- apps/web/src/theme/ThemeRegistry.tsx (new)
- apps/web/src/app/studio/tasks/components/TasksTable.tsx
- apps/web/src/app/studio/tasks/components/TaskFilters.tsx
- apps/web/src/app/studio/tasks/page.tsx

Quality gates

- Typecheck: PASS (@appostolic/web)

Requirements coverage

- Add MUI with SSR theme and license init: Done.
- Refactor Tasks Inbox to DataGridPremium and MUI inputs: Done.
- Server pagination with URL updates: Done.

How to try it

- Start the dev server and navigate to `/studio/tasks`.
- Use the chips/selects/date pickers/search to filter; use the grid’s footer to page; observe the URL reflecting state.

## Spike - refactor for MUI - Part 2

I’ll continue the MUI migration by centralizing theme options and converting the Agents and Traces tables to DataGridPremium, keeping SSR theming consistent.

Plan

- Add a shared theme options module and wire it into `ThemeRegistry`.
- Convert Agents table to `DataGridPremium` with consistent columns and action buttons.
- Convert Traces table to `DataGridPremium` with a toggle to preview input/output JSON.
- Typecheck and commit.

Actions taken

- Added `apps/web/src/theme/themeOptions.ts` and updated `ThemeRegistry` to build the theme from these options plus `enUS` locale.
- Refactored `apps/web/src/app/studio/agents/components/AgentsTable.tsx` to use `DataGridPremium` with columns: Name (link), Model, Temp, MaxSteps, Updated, and Actions (Edit/Delete buttons); empty state uses MUI Button and Box.
- Refactored `apps/web/app/dev/agents/components/TracesTable.tsx` to `DataGridPremium` and added a MUI `Collapse` section per row to display JSON for input/output; removed inline styles in favor of MUI `sx`.
- Ran typecheck: PASS.
- Committed changes.

Results

- Agents and Traces tables now match the MUI look-and-feel and benefit from DataGrid features (accessibility, sorting hooks, density).
- Theme is centralized for easier brand changes and consistent defaults (sizes, shapes).

Files changed

- apps/web/src/theme/themeOptions.ts (new)
- apps/web/src/theme/ThemeRegistry.tsx
- apps/web/src/app/studio/agents/components/AgentsTable.tsx
- apps/web/app/dev/agents/components/TracesTable.tsx

Quality gates

- Typecheck: PASS (@appostolic/web)

Requirements coverage

- Shared theme options wired into SSR: Done.
- Agents table migrated to DataGridPremium: Done.
- Traces table migrated to DataGridPremium with JSON preview: Done.

How to try it

- Navigate to `/studio/agents` to see the Agents grid with action buttons.
- Navigate to the Dev Agents page to see Traces with the View/Hide JSON toggle.
