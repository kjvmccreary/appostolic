# appostolic

Monorepo (Turborepo + PNPM) with apps:

- apps/web (Next.js)
- apps/api (.NET 8)
- apps/mobile (Expo React Native)

## Getting started (dev)

Prereqs:

- Docker Desktop
- .NET 8 SDK
- Node 20 + PNPM (recommend nvm; .nvmrc is v20)

Steps:

1. `cp .env.example .env`
2. `make bootstrap` # starts Docker, waits for DB, applies migrations, seeds demo data
3. `make api` # http://localhost:5198
4. `make web` # http://localhost:3000
5. `make mobile` # Expo Dev Tools; press 'i' for iOS or scan QR

Troubleshooting:

- DB stuck/unhealthy: `make nuke && make bootstrap`
- EF version skew: ensure `Directory.Packages.props` exists, run `make clean-dotnet`, then `dotnet restore && dotnet build`
- Expo Go SDK mismatch: upgrade project to Expo SDK 54 or use iOS simulator

---

## Development

Recommended 3-terminal workflow:

1. API

```
cd apps/api
pnpm dev
```

2. Web

```
cd apps/web
pnpm dev
```

3. Mobile (interactive)

```
cd apps/mobile
pnpm dev
```

Or from the root:

- Start Web + API only:

```
pnpm dev
```

- Start Web + API and print mobile instructions:

```
pnpm dev:all
```

Notes:

- Do not launch Expo from the root; it is interactive and works best in its own terminal.
- If simulator/device errors occur, stop Expo and run with a clean cache:

```
cd apps/mobile && pnpm exec expo start -c
```

---

## Database seed (API)

An idempotent seed tool is available at `apps/api/tools/seed`.
It inserts:

- user: `kevin@example.com` (Owner in both tenants)
- tenant A: `kevin-personal` (kind=personal, plan=free)
- tenant B: `first-baptist-austin` (kind=org, plan=org)
- memberships accordingly
- one lesson for `kevin-personal` titled "Welcome draft"

Seeding steps:

```
make up
make seed
make api    # in a terminal
make web    # in another terminal
make mobile # in another terminal
```

Env vars used by seed:

```
PGHOST=localhost
PGPORT=55432
PGDATABASE=appdb
PGUSER=<from .env>
PGPASSWORD=<from .env>
```

Note: The seed tool reuses the API `AppDbContext` and uses `set_config('app.tenant_id', <tenantId>, true)` during inserts so RLS policies apply per tenant.

---

## Authentication (dev)

Core flows implemented and test‑covered:

- Signup (anonymous) → creates user and a personal tenant; auto sign‑in from the web UI
- Invite acceptance (signed‑in) → adds membership for the invite’s tenant and role
- Two‑stage tenant selection (`/select-tenant`) → auto‑selects when only one membership exists
- Header TenantSwitcher → updates session and sets a `selected_tenant` cookie via `/api/tenant/select`

Web entry points:

- `/signup` — self‑serve signup form (supports optional invite token)
- `/invite/accept?token=...` — server route to accept an invite (redirects to `/login` if unauthenticated)
- `/select-tenant` — server route that selects tenant and redirects to the app

Environment variables (dev defaults shown where applicable):

- Web (apps/web)
  - `AUTH_SECRET` — required when web auth is enabled (generate any random string for dev)
  - `NEXTAUTH_URL=http://localhost:3000` — NextAuth base URL in dev
  - `NEXT_PUBLIC_API_BASE=http://localhost:5198` — API base used by the web app
- API (apps/api)
  - `Auth:PasswordPepper` — arbitrary secret used for Argon2id password hashing (set in your shell or appsettings for dev)
  - `Email:WebBaseUrl=http://localhost:3000` — used for absolute links in emails

Quick checks (dev):

- Signup via API (optional smoke):

```sh
curl -sS -X POST http://localhost:5198/api/auth/signup \
  -H 'Content-Type: application/json' \
  -d '{"email":"you@example.com","password":"Passw0rd!"}' | jq
```

- Visit the web app:
  - Open `http://localhost:3000/signup` to create an account and be signed in.
  - If invited, open the invite link pointing to `http://localhost:3000/invite/accept?token=...`.
  - You’ll be redirected to `/select-tenant` on first sign‑in to pick a tenant.

Troubleshooting:

- Web shows “Missing required env var NEXT_PUBLIC_API_BASE”: create `apps/web/.env.local` with `NEXT_PUBLIC_API_BASE=http://localhost:5198` and restart the dev server.
- Redirect loops or 401s on protected routes: ensure `WEB_AUTH_ENABLED=true` and `AUTH_SECRET` are set for the web app; see RUNBOOK “Web authentication & route protection”.
- Missing tenant after sign-in: if a protected page returns 401 due to no tenant selected, the page will redirect to `/select-tenant`. Choose a tenant (or it auto-selects if you have one) and you’ll be taken to `/studio`.
- Invite accept requires auth: if you see a redirect to `/login`, sign in first, then revisit `/invite/accept?token=...`.

---

## Email notifications

Local development defaults to SMTP via Mailhog.

- Mailhog UI: http://localhost:8025
- SMTP host/port (from API perspective): 127.0.0.1:1025
  - We expose Mailhog's SMTP port on the host so the API (running on your machine) can send mail.

Switching providers:

- Dev default: `smtp` (Mailhog)
- To test SendGrid locally, set:
  - `Email__Provider=sendgrid`
  - `SendGrid__ApiKey=<your key>`
  - Alternatively, export `SENDGRID_API_KEY` in your shell (compat shim maps it to `SendGrid:ApiKey`).

Configuration keys (API):

- Email\_\_Provider: smtp | sendgrid (defaults to smtp in Development)
- Email\_\_WebBaseUrl: http://localhost:3000 (used to build absolute links)
- Email\_\_FromAddress: no-reply@appostolic.local
- Email\_\_FromName: Appostolic
- Smtp\_\_Host: 127.0.0.1
- Smtp\_\_Port: 1025
- SendGrid\_\_ApiKey: your SendGrid key (required when provider=sendgrid in Production)

Safety and observability:

- Production guard: if `ASPNETCORE_ENVIRONMENT=Production` and `Email__Provider=sendgrid`, startup fails unless `SendGrid__ApiKey` is set.
- Metrics: `email.sent.total` and `email.failed.total` (tagged by kind) are emitted via OpenTelemetry.
- Logs: the email dispatcher enriches logs with correlation fields (user, tenant, invite) when present.

Try it (dev, Mailhog):

- Enqueue a verification email:
  - POST /api-proxy/dev/notifications/verification with JSON body: { "toEmail": "you@example.com", "toName": "You", "token": "abc123" }
- Enqueue an invite email:
  - POST /api-proxy/dev/notifications/invite with JSON body: { "toEmail": "you@example.com", "toName": "You", "tenant": "kevin-personal", "role": "Member", "inviter": "Kevin", "token": "xyz789" }
- Open Mailhog UI at http://localhost:8025 and check the inbox.

Optional cURL (server proxies inject dev headers):

Verification

```
curl -sS -X POST \
  -H 'content-type: application/json' \
  http://localhost:3000/api-proxy/dev/notifications/verification \
  -d '{"toEmail":"you@example.com","toName":"You","token":"abc123"}'
```

Invite

```
curl -sS -X POST \
  -H 'content-type: application/json' \
  http://localhost:3000/api-proxy/dev/notifications/invite \
  -d '{"toEmail":"you@example.com","toName":"You","tenant":"kevin-personal","role":"Member","inviter":"Kevin","token":"xyz789"}'
```

---

## Policies & compliance

- Privacy Policy (engineering draft for notifications): devInfo/Sendgrid/privacyPolicy.md
- Vendor/Subprocessors and compliance notes: devInfo/Sendgrid/vendorCompliance.md
