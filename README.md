# appostolic

Monorepo (Turborepo + PNPM) with apps:

- apps/web (Next.js)
- apps/api (.NET 8)
- apps/mobile (Expo React Native)

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
- one empty lesson for `kevin-personal`

Run (uses the same POSTGRES\_\* env vars as Docker compose):

```
# From repo root
export POSTGRES_HOST=localhost
export POSTGRES_PORT=55432
export POSTGRES_DB=app
export POSTGRES_USER=app
export POSTGRES_PASSWORD=app

cd apps/api/tools/seed
dotnet run
```

Expected output includes created/existing IDs, for example:

```
Connected to database.
User exists: 6e8c... kevin@example.com
Tenant exists: 4a3b... kevin-personal
Tenant exists: 1b2c... first-baptist-austin
Membership exists: tenant=4a3b... user=6e8c... role=Owner status=Active
Membership exists: tenant=1b2c... user=6e8c... role=Owner status=Active
Lesson exists for tenant=4a3b... title=''
Seed complete.
```
