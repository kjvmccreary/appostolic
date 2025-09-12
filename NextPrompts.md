A09-08.5 — API DTOs: surface totals & cost
Paste to Copilot:

Include token totals and estimated cost in API responses.

⏱ Start Timer
{"task":"A09-08.5","phase":"start","ts":"<UTC>"}

Work
Application/Agents/Api/AgentTaskDtos.cs:

Add to AgentTaskSummary: int totalTokens

Add to AgentTaskDetails: int totalPromptTokens, int totalCompletionTokens, int totalTokens, decimal? estimatedCostUsd

Map these in your endpoint handlers (GET list, GET {id}).

Swagger: the new fields should appear automatically.

✅ Acceptance
GET /api/agent-tasks/{id} returns token totals (+ cost when enabled).

💾 Manual Effort Baseline
ManualHours = 0.8

🧮 Stop/Compute
Append end; compute and log savings JSON + Sprint bullet.

A09-08.6 — Web panel: token/cost badges & per-step tokens
Paste to Copilot:

Enhance the Run Agent page to display token summaries and per-step tokens.

⏱ Start Timer
{"task":"A09-08.6","phase":"start","ts":"<UTC>"}

Files
apps/web/src/app/dev/agents/components/AgentRunForm.tsx (summary badges)

apps/web/src/app/dev/agents/components/TracesTable.tsx (columns already present; ensure tokens show)

UI
Above the traces, show badges:

Total tokens (from task)

Prompt/Completion breakdown

Est. cost (only when API returns non-null)

In the table, ensure PromptTokens and CompletionTokens render for model steps; tools can display zero/blank.

✅ Acceptance
After a run, badges show non-zero totals; table shows per-step tokens.

💾 Manual Effort Baseline
ManualHours = 1.2

🧮 Stop/Compute
Append end; compute and log savings JSON + Sprint bullet.

A09-08.7 — Tests: estimator & aggregation
Paste to Copilot:

Add tests for token estimation and aggregation into AgentTask.

⏱ Start Timer
{"task":"A09-08.7","phase":"start","ts":"<UTC>"}

Backend tests
Model/TokenEstimatorTests.cs: strings of known lengths → expected token counts; empty string → 0.

Orchestrator/TokenAggregationTests.cs: run a flow with 2 model steps; assert:

Sum of step tokens equals AgentTask.Total\*

EstimatedCostUsd increases when pricing enabled

(Optional) toggle ModelPricing.Enabled=false → cost is null.

Frontend tests
Extend useTaskPolling or AgentRunForm tests to assert the badges reflect totals from API.

✅ Acceptance
Tests pass deterministically.

💾 Manual Effort Baseline
ManualHours = 1.2

🧮 Stop/Compute
Append end; compute and log savings JSON + Sprint bullet.

A09-08.8 — README updates: tokens & cost
Paste to Copilot:

Update docs to explain token accounting & optional cost.

⏱ Start Timer
{"task":"A09-08.8","phase":"start","ts":"<UTC>"}

Files
apps/api/README.md and apps/web/README.md

Content
API: how tokens are counted per model step; roll-up on AgentTask; optional pricing via ModelPricing options; the heuristic nature of estimates.

Web: where totals/cost appear in the Run panel; a note that cost shows only when backend has pricing enabled.

✅ Acceptance
Docs render; examples match current behavior.

💾 Manual Effort Baseline
ManualHours = 0.6

🧮 Stop/Compute
Append end; compute and log savings JSON + Sprint bullet.

Acceptance for A09-08
Token estimator & pricing options in place (no external libs).

Orchestrator updates aggregate totals and optional estimated cost.

Metrics attribute tokens by agent/model.

API exposes totals (+ cost when configured).

Web shows totals & per-step tokens.

Tests cover estimator & aggregation.

Docs updated.

Actual-time Dev Time Saved entries recorded for A09-08.1 … A09-08.8.

When you’re finished, say “A09-08 done” and I’ll queue up S1-10: Agent Studio UI or the next S1-09 item if you’d like more runtime polish.
