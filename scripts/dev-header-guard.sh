#!/usr/bin/env bash
set -euo pipefail
# Dev Header Decommission Guard (RDH Story 2)
# Fails if deprecated dev auth artifacts are detected outside explicit allowlists.
# Patterns monitored:
#   - x-dev-user / x-tenant headers
#   - DevHeaderAuthHandler
#   - AuthTestClient.UseTenantAsync (legacy mint helper)
#   - BearerOrDev composite scheme (will be removed in Story 3/4)
# Allowlist paths (regex):
#   - apps/api.tests/Guard/NoUseTenantAsyncLeftTests.cs (self-referential guard until fully removed)
#   - apps/api.tests/Auth/DevHeadersDisabledTests.cs (negative coverage retained until removal)
#   - apps/api.tests/Auth/DevHeadersRemovedTests.cs (future negative coverage post removal)
#   - Any story plan or documentation under devInfo/ or docs/ (documentation may reference strings)
# Exit codes:
#   0 = No forbidden patterns (or only in allowlist)
#   1 = Forbidden pattern detected

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT_DIR"

# Assemble grep command
PATTERNS=("x-dev-user" "x-tenant" "DevHeaderAuthHandler" "AuthTestClient.UseTenantAsync" "BearerOrDev")
ALLOWLIST_REGEX='apps/api.tests/Guard/NoUseTenantAsyncLeftTests.cs|apps/api.tests/Auth/DevHeadersDisabledTests.cs|apps/api.tests/Auth/DevHeadersRemovedTests.cs|devInfo/|docs/'

FOUND=()
for p in "${PATTERNS[@]}"; do
  while IFS= read -r file; do
    # Skip allowlisted files
    if [[ "$file" =~ $ALLOWLIST_REGEX ]]; then
      continue
    fi
    FOUND+=("$p :: $file")
  done < <(git ls-files | grep -E '\.(cs|md|sh|ts|tsx)$' | xargs grep -Il --null "$p" 2>/dev/null | tr '\0' '\n')
# Note: grep -I avoids binary files; we capture file names containing pattern.
  # Remove duplicates for same pattern/file (grep -l ensures uniqueness anyway)
done

if ((${#FOUND[@]})); then
  echo "[dev-header-guard] Forbidden dev header artifacts detected:" >&2
  printf '  - %s\n' "${FOUND[@]}" >&2
  echo "[dev-header-guard] Please migrate/remove these references before merging." >&2
  exit 1
fi

echo "[dev-header-guard] OK: no forbidden dev header artifacts outside allowlist.";
