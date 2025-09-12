## A11-02 — API: Cancel endpoint (cooperative)

- Added POST `/api/agent-tasks/{id}/cancel` endpoint.
- Behavior:
  - Pending → immediately sets `Status=Canceled`, `FinishedAt=UtcNow`, and `ErrorMessage="Canceled"`.
  - Running → records cancel intent via in-memory `AgentTaskCancelRegistry`; worker/orchestrator polls between steps and transitions to `Canceled` soon after.
  - Terminal (Succeeded/Failed/Canceled) → returns 409 Conflict `{ message: "Already terminal" }`.
- Infrastructure:
  - New `AgentTaskCancelRegistry` (singleton) with `RequestCancel`, `IsCancelRequested`, and `TryClear`.
  - Orchestrator observes registry and cancels cooperatively between steps.
  - Registered the registry in DI.

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
