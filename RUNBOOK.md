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
make seed
make api    # terminal 1
make web    # terminal 2
make mobile # terminal 3
```

Env vars used by seed:

```
PGHOST=localhost
PGPORT=55432
PGDATABASE=appdb
PGUSER=<from .env>
PGPASSWORD=<from .env>
```

Note: The seed tool reuses the API `AppDbContext` and issues `SET LOCAL app.tenant_id` before RLS-protected operations so policies apply per tenant.
