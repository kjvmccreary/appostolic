#!/usr/bin/env bash
set -euo pipefail
# Dev Header Decommission Guard (RDH Story 5 – PARTIAL ENFORCEMENT)
# Purpose: Hard‑fail CI if any legacy development authentication artifacts are reintroduced
# outside of explicit historical/documentation allowlists.
#
# Monitored patterns (literal substrings):
#   - x-dev-user / x-tenant (legacy headers)
#   - DevHeaderAuthHandler (removed handler)
#   - BearerOrDev (removed composite scheme id)
#   - AUTH__ALLOW_DEV_HEADERS (removed feature flag)
#   - AuthTestClient.UseTenantAsync (legacy mint helper – should now only appear in historical docs)
#
# Current Scope (Story 5 initial): restrict scanning to API runtime + tests to avoid failing on pending web/client cleanup.
# Future Story 6/7 will expand scope to web and docs after replacement examples are published.
#
# Allowlist paths (regex union) – documentation & intentional negative-path regression tests & dev-only endpoints pending rewrite:
#   - apps/api.tests/Auth/DevHeadersDisabledTests.cs
#   - apps/api.tests/Auth/DevHeadersRemovedTests.cs
#   - apps/api/App/Endpoints/Dev.* (temporary dev endpoints; will be refactored or removed)
#   - devInfo/ (historical sprint plans & story logs)
#   - docs/ (upgrade guides, architecture snapshots)
#   - SnapshotArchitecture.*.md (legacy snapshots)
#
# Exit codes:
#   0 = Clean (only allowlisted occurrences)
#   1 = Violations found

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT_DIR"

# Assemble grep command
PATTERNS=("x-dev-user" "x-tenant" "DevHeaderAuthHandler" "BearerOrDev" "AUTH__ALLOW_DEV_HEADERS" "AuthTestClient.UseTenantAsync")
ALLOWLIST_REGEX='apps/api.tests/Auth/DevHeadersDisabledTests.cs|apps/api.tests/Auth/DevHeadersRemovedTests.cs|apps/api/App/Endpoints/Dev|devInfo/|docs/|SnapshotArchitecture'

FOUND=()
FILES=$(git ls-files | grep -E '^(apps/api/|apps/api.tests/)' | grep -E '\.(cs|md|sh)$')
for p in "${PATTERNS[@]}"; do
  while IFS= read -r file; do
    [[ -z "$file" ]] && continue
    if [[ "$file" =~ $ALLOWLIST_REGEX ]]; then
      continue
    fi
    FOUND+=("$p :: $file")
  done < <(echo "$FILES" | xargs grep -Il --null "$p" 2>/dev/null | tr '\0' '\n')
done

if ((${#FOUND[@]})); then
  echo "[dev-header-guard] Forbidden dev header artifacts detected:" >&2
  printf '  - %s\n' "${FOUND[@]}" >&2
  echo "[dev-header-guard] Please migrate/remove these references before merging." >&2
  exit 1
fi

echo "[dev-header-guard] OK: no forbidden dev header artifacts outside allowlist.";
