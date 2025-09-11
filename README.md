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
