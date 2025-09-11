#!/usr/bin/env bash
set -euo pipefail

# Move to repo root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT_DIR"

echo "== Dev Doctor =="

check_cmd() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "ERROR: $1 not found in PATH" >&2
    exit 1
  fi
}

# Check core tools and print versions
check_cmd docker
echo "docker: $(docker --version)"
if docker compose version >/dev/null 2>&1; then
  echo "docker compose: $(docker compose version | head -n1)"
else
  echo "ERROR: 'docker compose' subcommand not available (requires recent Docker)" >&2
  exit 1
fi

check_cmd dotnet
echo "dotnet: $(dotnet --version)"
check_cmd node
echo "node: $(node --version)"
check_cmd pnpm
echo "pnpm: $(pnpm --version)"

# Ensure .env presence
if [[ ! -f .env ]]; then
  echo "WARN: .env not found. Create it via: cp .env.example .env"
else
  echo ".env present."
fi

# Validate docker compose configuration
echo "Validating docker compose config..."
docker compose --env-file .env -f infra/docker/compose.yml config --quiet && echo "Compose config OK."

# Check Postgres health
echo "Checking Postgres health..."
CID="$(docker compose --env-file .env -f infra/docker/compose.yml ps -q postgres || true)"
if [[ -z "${CID}" ]]; then
  echo "Postgres container not running. Start it with: make up"
else
  STATUS="$(docker inspect -f '{{.State.Health.Status}}' "$CID" 2>/dev/null || echo "unknown")"
  echo "Postgres container: $CID, health: $STATUS"
fi

# Load env for psql test if available
if [[ -f .env ]]; then
  set -a
  # shellcheck disable=SC1091
  . ./.env
  set +a
fi

# Run a quick psql smoke test from inside the container
if [[ -n "${POSTGRES_USER:-}" && -n "${POSTGRES_PASSWORD:-}" && -n "${POSTGRES_DB:-}" && -n "${CID:-}" ]]; then
  echo "Running psql smoke test..."
  docker exec -e PGPASSWORD="$POSTGRES_PASSWORD" "$CID" sh -lc "psql -U \"$POSTGRES_USER\" -d \"$POSTGRES_DB\" -c 'select current_user,current_database();'" || true
else
  echo "Skipping psql test (missing env or container not running)."
fi

echo
echo "Next steps:"
echo "  make bootstrap"
