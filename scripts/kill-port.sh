#!/usr/bin/env bash
set -euo pipefail
PORT="${1:-}"
if [[ -z "$PORT" ]]; then
  echo "Usage: $0 <port>" >&2
  exit 1
fi
# macOS: use lsof to find PIDs listening on the port
PIDS=$(lsof -nP -i tcp:"$PORT" -sTCP:LISTEN -t 2>/dev/null || true)
if [[ -z "$PIDS" ]]; then
  echo "No process listening on port $PORT"
  exit 0
fi
for PID in $PIDS; do
  if ps -p "$PID" > /dev/null 2>&1; then
    echo "Killing PID $PID (port $PORT)"
    kill "$PID" || true
    # give it a moment, then force if still alive
    sleep 0.5
    if ps -p "$PID" > /dev/null 2>&1; then
      echo "Force killing PID $PID"
      kill -9 "$PID" || true
    fi
  fi
done
echo "Port $PORT freed."