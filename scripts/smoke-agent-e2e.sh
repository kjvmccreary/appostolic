#!/usr/bin/env bash

set -Eeuo pipefail

# Tiny E2E smoke: creates a task, polls until terminal, prints status, totals, and last 2 traces
# Env:
#   API_BASE   (default: http://localhost:5198)
#   DEV_USER   (default: kevin@example.com)
#   DEV_TENANT (default: kevin-personal)
#   AGENT_ID   (default: 11111111-1111-1111-1111-111111111111)

API_BASE=${API_BASE:-http://localhost:5198}
DEV_USER=${DEV_USER:-kevin@example.com}
DEV_TENANT=${DEV_TENANT:-kevin-personal}
AGENT_ID=${AGENT_ID:-11111111-1111-1111-1111-111111111111}

hdr=(
  -H "x-dev-user: ${DEV_USER}"
  -H "x-tenant: ${DEV_TENANT}"
  -H "Content-Type: application/json"
)

echo "[smoke] API_BASE=${API_BASE} AGENT_ID=${AGENT_ID}"

cleanup() {
  code=$?
  if [[ $code -ne 0 ]]; then
    echo "[smoke] failed (exit ${code})"
  fi
}
trap cleanup EXIT

create_payload() {
  cat <<JSON
{"agentId":"${AGENT_ID}","input":{"topic":"Intro to EF Core"}}
JSON
}

echo "[smoke] creating task…"
create_resp=$(curl -fsS -X POST "${API_BASE}/api/agent-tasks" "${hdr[@]}" --data @<(create_payload)) || {
  echo "[smoke] create failed" >&2
  exit 1
}

# Task ID might be at root (standard) or nested under .task (dev/demo endpoints)
task_id=$(jq -r '(.id // .task.id // empty)' <<<"${create_resp}")
if [[ -z "${task_id}" || "${task_id}" == "null" ]]; then
  echo "[smoke] could not parse task id" >&2
  echo "Response: ${create_resp}" >&2
  exit 1
fi
echo "[smoke] task id: ${task_id}"

echo "[smoke] polling for completion…"
status=""
details_json=""
for i in {1..120}; do # up to ~30s (sleep 0.25)
  details_json=$(curl -fsS "${API_BASE}/api/agent-tasks/${task_id}" "${hdr[@]}") || true
  status=$(jq -r '.status // empty' <<<"${details_json}")
  if [[ -z "${status}" ]]; then
    sleep 0.25
    continue
  fi
  if [[ "${status}" != "Pending" && "${status}" != "Running" ]]; then
    break
  fi
  sleep 0.25
done

if [[ -z "${status}" || "${status}" == "Pending" || "${status}" == "Running" ]]; then
  echo "[smoke] task did not reach a terminal state in time" >&2
  echo "Details: ${details_json}" | jq . >&2 || true
  exit 2
fi

total_tokens=$(jq -r '(.totalTokens // .total_tokens // 0)' <<<"${details_json}")
est_cost=$(jq -r '(.estimatedCostUsd // .estimated_cost_usd // empty)' <<<"${details_json}")

echo "[smoke] status: ${status}"
echo "[smoke] total tokens: ${total_tokens}"
if [[ -n "${est_cost}" && "${est_cost}" != "null" ]]; then
  echo "[smoke] est. cost: $${est_cost}"
else
  echo "[smoke] est. cost: n/a"
fi

echo "[smoke] last 2 traces:"
jq -r '
  (.traces // [])
  | sort_by(.stepNumber // 0)
  | (if length > 2 then .[-2:] else . end)
  | .[]
  | "- step \(.stepNumber // 0): \(.kind // "?") \(.name // "?") (\(.durationMs // 0) ms)"
' <<<"${details_json}" || echo "[smoke] (no traces available)"

echo "[smoke] done."
