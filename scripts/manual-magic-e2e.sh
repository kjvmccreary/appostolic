#!/usr/bin/env bash
# Manual E2E for Magic Link using Mailhog + NextAuth credentials callback
#
# Preconditions:
# - API at http://localhost:5198 (Development)
# - Web at http://localhost:3000 (Next dev) with WEB_AUTH_ENABLED=true
# - Mailhog at http://localhost:8025
# - jq installed (brew install jq)
#
# Usage:
#   bash scripts/manual-magic-e2e.sh [email]
#
set -euo pipefail

# Enable extra logging with DEBUG=1
DEBUG="${DEBUG:-0}"

API_BASE="${API_BASE:-http://localhost:5198}"
WEB_BASE="${WEB_BASE:-http://localhost:3000}"
MAILHOG_BASE="${MAILHOG_BASE:-http://localhost:8025}"
EMAIL="${1:-ml_$(date +%s)@example.com}"
COOKIE_JAR="$(mktemp -t magic_cookies_XXXXXX)"
trap 'rm -f "$COOKIE_JAR"' EXIT

say() { printf "\033[1;34m▶ %s\033[0m\n" "$*"; }
ok()  { printf "\033[1;32m✔ %s\033[0m\n" "$*"; }
err() { printf "\033[1;31m✖ %s\033[0m\n" "$*"; }

# 0) Pings
say "Pinging services..."
http_code() { curl -s -o /dev/null -w "%{http_code}" "$1"; }
[[ "$(http_code "$API_BASE/health")" == "200" ]] || { err "API not reachable at $API_BASE"; exit 1; }
[[ "$(http_code "$WEB_BASE")" =~ ^2|3 ]] || { err "Web not reachable at $WEB_BASE"; exit 1; }
[[ "$(http_code "$MAILHOG_BASE/api/v2/messages?limit=1")" == "200" ]] || { err "Mailhog not reachable at $MAILHOG_BASE"; exit 1; }
ok "Services reachable"

# 1) Request magic link
say "Requesting magic link for $EMAIL"
curl -s -X POST "$API_BASE/api/auth/magic/request" \
  -H 'content-type: application/json' \
  -d "{\"email\":\"$EMAIL\"}" >/dev/null
ok "Requested (Accepted)"

# 2) Poll Mailhog for verify token
say "Polling Mailhog for verify link (30s)..."
DEADLINE=$(( $(date +%s) + 30 ))
TOKEN=""
while [[ $(date +%s) -lt $DEADLINE ]]; do
  BODY=$(curl -s "$MAILHOG_BASE/api/v2/messages")
  # Filter to messages addressed to EMAIL and extract the token via regex
  MATCH=$(echo "$BODY" | jq -r --arg to "$EMAIL" '
    .items[]? | select(.Content.Headers.To[]? | ascii_downcase | contains($to | ascii_downcase))
    | .Content.Body // empty' | \
    sed -nE 's/.*\/magic\/verify\?token=([A-Za-z0-9_-]+).*/\1/p' | head -n1)
  if [[ -n "$MATCH" ]]; then TOKEN="$MATCH"; break; fi
  sleep 1
done
if [[ -z "$TOKEN" ]]; then err "No magic verify token found in Mailhog"; exit 1; fi
ok "Got token: $TOKEN"

# 3) Fetch NextAuth CSRF token
say "Fetching NextAuth CSRF token"
CSRF=$(curl -s -c "$COOKIE_JAR" "$WEB_BASE/api/auth/csrf" | jq -r .csrfToken)
if [[ -z "$CSRF" || "$CSRF" == "null" ]]; then err "Failed to get CSRF token"; exit 1; fi
ok "CSRF token acquired"

# 4) Sign in via Credentials (magicToken) to set session cookie
say "Signing in via NextAuth credentials callback"
LOGIN_RES=$(curl -s -i -c "$COOKIE_JAR" -b "$COOKIE_JAR" -X POST "$WEB_BASE/api/auth/callback/credentials?json=true" \
  -H 'content-type: application/x-www-form-urlencoded' \
  --data-urlencode "csrfToken=$CSRF" \
  --data-urlencode "magicToken=$TOKEN")
# Expect a 200 JSON with ok: true or a 302; both set a session cookie
STATUS=$(printf "%s" "$LOGIN_RES" | sed -nE 's/^HTTP\/[0-9.]+ ([0-9]+).*/\1/p' | head -n1)
if [[ "$STATUS" != "200" && "$STATUS" != "302" ]]; then
  err "Sign-in failed (status=$STATUS)"; printf "%s\n" "$LOGIN_RES" | tail -n +1; exit 1
fi
ok "Signed in (status=$STATUS)"

# Optional debug: show what the server sees for session and cookies
if [[ "$DEBUG" == "1" ]]; then
  say "Debug: session before tenant select"
  curl -s -b "$COOKIE_JAR" "$WEB_BASE/api/debug/session" | jq . || true
fi

# 5) Select tenant (assume personal slug from localpart)
LOCAL="${EMAIL%@*}"
TENANT_SLUG="${LOCAL}-personal"
say "Selecting tenant: $TENANT_SLUG"
SEL_RES=$(curl -s -i -c "$COOKIE_JAR" -b "$COOKIE_JAR" -L "$WEB_BASE/api/tenant/select?tenant=$TENANT_SLUG&next=/studio/agents")
SEL_STATUS=$(printf "%s" "$SEL_RES" | sed -nE 's/^HTTP\/[0-9.]+ ([0-9]+).*/\1/p' | head -n1)
if [[ "$SEL_STATUS" != "200" && "$SEL_STATUS" != "302" && "$SEL_STATUS" != "303" && "$SEL_STATUS" != "307" && "$SEL_STATUS" != "308" ]]; then
  err "Tenant select failed (status=$SEL_STATUS)"; exit 1
fi
ok "Tenant selected"

# Optional debug: show what the server sees after tenant select
if [[ "$DEBUG" == "1" ]]; then
  say "Debug: session after tenant select"
  curl -s -b "$COOKIE_JAR" "$WEB_BASE/api/debug/session" | jq . || true
fi

# 6) Hit Agents API via proxy and print count
say "Fetching agents via /api-proxy/agents"
AGENTS_JSON=$(curl -s -b "$COOKIE_JAR" "$WEB_BASE/api-proxy/agents?take=10")
COUNT=$(echo "$AGENTS_JSON" | jq 'length' 2>/dev/null || echo "0")
HTTP=$(
  curl -s -o /dev/null -w "%{http_code}" -b "$COOKIE_JAR" "$WEB_BASE/api-proxy/agents?take=10"
)
if [[ "$HTTP" != "200" ]]; then
  err "Proxy returned $HTTP"; echo "$AGENTS_JSON"; exit 1
fi
ok "Agents OK (count=$COUNT)"

# 7) Optionally fetch Studio page
say "Loading /studio/agents (HTML)"
HTML_STATUS=$(curl -s -o /dev/null -w "%{http_code}" -L -b "$COOKIE_JAR" "$WEB_BASE/studio/agents")
if [[ "$HTML_STATUS" != "200" ]]; then err "/studio/agents returned $HTML_STATUS"; exit 1; fi
ok "Studio Agents page loads (200)"

echo
ok "Manual magic link E2E succeeded for $EMAIL"
