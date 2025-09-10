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
