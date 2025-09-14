# RUNBOOK

## Monorepo + Metro (Expo React Native)

To avoid "Invalid hook call" and duplicated React in a PNPM + Turborepo monorepo:

1. Metro config (apps/mobile/metro.config.js)
   - watchFolders includes the workspace root
   - resolver.nodeModulesPaths includes project and workspace root node_modules
   - resolver.unstable_enableSymlinks = true
   - resolver.unstable_enablePackageExports = true
   - extraNodeModules maps:
     - react -> apps/mobile/node_modules/react
     - react-native -> apps/mobile/node_modules/react-native

2. Package design for shared UI
   - In packages like `@appostolic/ui`, declare:
     - peerDependencies: react, react-native
     - devDependencies: matching versions for local development
   - Do NOT put react/react-native in dependencies of shared packages.

3. Hoisting with PNPM
   - Root .npmrc should hoist react, react-dom, react-native, expo, @expo/_, @babel/_ when needed.
   - Root package.json can pin versions via pnpm.overrides to keep a single React version.

4. Verification
   - Install: `pnpm install`
   - Check copies from the mobile package perspective:
     - `pnpm -F @appostolic/mobile ls react react-native`
   - Expect a single version of each.

5. Troubleshooting
   - Clear Expo cache: `pnpm -F @appostolic/mobile exec expo start -c`
   - If mismatch persists, ensure web and mobile use the same React (e.g., 18.2.0 for Expo SDK 51).

---

## Seeding (API)

Use Makefile targets to bring up infra and load demo data:

```
make up
## Authentication flows (operations)

This section is a practical guide to exercise auth flows in Development.

Prereqs

- API running at `http://localhost:5198` (Task: Dev: api-only)
- Web running at `http://localhost:3000` (Task: Dev: web+api+mobile)
- Web env: `apps/web/.env.local` contains at minimum:

```

AUTH_SECRET=devsecret
NEXTAUTH_URL=http://localhost:3000
NEXT_PUBLIC_API_BASE=http://localhost:5198

````

### 1) Signup (anonymous)

- Browser path: `http://localhost:3000/signup`
- Expected: successful submit auto signs in and redirects to `/select-tenant`

Optional API smoke:

```sh
curl -sS -X POST http://localhost:5198/api/auth/signup \
  -H 'Content-Type: application/json' \
  -d '{"email":"you+dev@example.com","password":"Passw0rd!"}' | jq
````

### 2) Two-stage tenant selection

- Browser path after first sign‑in: `/select-tenant`
- Behavior:
  - If a single membership exists, page auto‑selects the tenant and redirects to `/studio`.
  - Otherwise, the user picks from memberships. The page sets the `selected_tenant` cookie and redirects.

### 3) Tenant switching

- Use the header TenantSwitcher (visible when authenticated) to change tenants.
- Behavior: triggers `session.update({ tenant })`, POSTs to `/api/tenant/select` to set `selected_tenant` cookie, then refreshes.

### 4) Invite acceptance

- On success, a membership is added for the invite’s tenant and role.

Troubleshooting

- Missing `NEXT_PUBLIC_API_BASE`
  - Create `apps/web/.env.local` with `NEXT_PUBLIC_API_BASE=http://localhost:5198`; restart web dev server.
- 401/redirects on protected pages
  - Ensure `WEB_AUTH_ENABLED=true` and `AUTH_SECRET` are set in `apps/web/.env.local`.
  - If you’re signed in but haven’t chosen a tenant yet, protected server pages that call `/api-proxy/*` will redirect you to `/select-tenant`. Pick a tenant and you’ll be navigated to `/studio`.
- API signup fails with 500/400
  - Check API logs for `Auth:PasswordPepper` presence (set via environment or `appsettings.Development.json`).
  - Ensure database is migrated/seeded: `make bootstrap`.

make seed
make web # terminal 2

```

Env vars used by seed:

PGPORT=55432
PGDATABASE=appdb
PGUSER=<from .env>
PGPASSWORD=<from .env>

```

---

## Email notifications (ops)

Dev flow (SMTP/Mailhog):

- Mailhog UI: http://localhost:8025
- SMTP host/port expected by the API: 127.0.0.1:1025
- Default provider in Development: smtp (Mailhog). No extra config required.

Switching to SendGrid:

- Set environment for the API service:
  - `Email__Provider=sendgrid`
  - `SendGrid__ApiKey=<your key>`
- A compatibility shim accepts `SENDGRID_API_KEY` and maps it to `SendGrid:ApiKey`.
- In Production, startup will fail if provider=sendgrid and the API key is missing.

Troubleshooting:

- No emails in Mailhog:
  - Confirm Mailhog UI is reachable at http://localhost:8025
  - Ensure SMTP port 1025 is exposed on the host and not blocked
- SendGrid 401/403:
  - Ensure the key has correct permissions and is not revoked
  - Set `Email__WebBaseUrl` (e.g., http://localhost:3000) so links are absolute

Observability:

- Metrics: `email.sent.total` and `email.failed.total` are exported via OTEL; check your collector or console exporter in Development.
- Logs: the dispatcher logs per-send with correlation fields when available; use these to trace user/tenant/invite flows.
  Quick E2E check (dev):

- POST `/api-proxy/dev/notifications/verification` with { toEmail, toName, token }

---

By default, notifications publish to the in‑process channel transport. You can optionally enable Redis Pub/Sub so multiple API instances can wake the dispatcher via a shared broker.

- Ensure the dev Docker stack is up; Redis listens on localhost:6380.
- Set the API environment (any of the following are equivalent):
  - `Notifications__Transport__Redis__Host=127.0.0.1`
  - `Notifications__Transport__Redis__Port=6380`
  - Or using a single connection string
    - `Notifications__Transport__Redis__ConnectionString=127.0.0.1:6380`
    - `Notifications__Transport__Redis__Channel=app:notifications:queued`
    - `Notifications__Transport__Redis__Ssl=false`

Then restart the API process.

- With the API running, trigger a dev email from the web proxy (headers injected):
- Switch back to in‑process by unsetting the vars or setting `Notifications__Transport__Mode=channel` and restarting the API.

Notes:

- Behavior is identical between modes; Redis only changes the signal path that wakes the dispatcher.
- In Development, the default remains the in‑process channel unless explicitly set to `redis`.

---

Long-running servers (API, web, mobile) should be started via VS Code background tasks so that subsequent one-off commands (curl, node, scripts) don't reuse and kill the same terminal session:

- Start servers from the Tasks panel:
  - Dev: web+api+mobile — runs `pnpm dev` in background
  - Dev: api-only — runs `make api` in background
- Run one-off commands in a separate terminal. Avoid typing them into the same terminal that shows server logs.
- In automation, I will always start servers via background tasks and run one-off commands in separate terminals.

If you get a port-in-use error (e.g., 5198), stop the background task from the Tasks panel first, then restart it.

---

## Web authentication & route protection

The web app uses a lightweight route-protection middleware to guard developer and studio areas:

- Protected routes: `/studio/*`, `/dev/*`
- Behavior when `WEB_AUTH_ENABLED=true` (default in Development):
  - Unauthenticated access redirects to `/login?next={originalPath}`
  - Visiting `/login` while authenticated redirects to `/studio/agents`

Configure via `apps/web/.env.local`:

```

WEB_AUTH_ENABLED=true
AUTH_SECRET=your_auth_secret
NEXTAUTH_URL=http://localhost:3000

# Seed credentials for Credentials provider are also defined here

```

Changing `WEB_AUTH_ENABLED` requires restarting the web dev server.

Quick verification (use a separate terminal; do not run these in server log terminals):

```

# Replace 3000 with the actual dev port if Next started on 3001

curl -sS -D - -o /dev/null http://localhost:3000/studio/agents # expect 307 → /login?next=...
curl -sS -D - -o /dev/null http://localhost:3000/dev # expect 307 → /login?next=...
curl -sS -D - -o /dev/null http://localhost:3000/login # expect 200

```

Tip: Next.js may choose 3001 if 3000 is occupied. You can discover the port with:

```

```

### Node version note

The monorepo targets Node 20.x. If VS Code tasks or new shells pick up a different default, set the default with nvm so background tasks use Node 20:

```

nvm alias default 20

```

---

## Compliance & data handling

- Privacy Policy (engineering draft for notifications): devInfo/Sendgrid/privacyPolicy.md
- Vendor/Subprocessors and compliance notes: devInfo/Sendgrid/vendorCompliance.md

```

```
