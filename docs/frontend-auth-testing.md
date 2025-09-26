# Frontend Auth Testing Guide

This guide captures the agreed upon conventions for exercising the JWT-based authentication flow inside the web test suite. The goal is to keep every test in sync with the production refresh rotation pipeline so regressions around cookie precedence, tenant selection, or refresh reuse are caught locally.

## Core Fixtures & Helpers

### Session factories (`apps/web/test/fixtures/authSession.ts`)

Use the exported helpers instead of hand-rolling `useSession` payloads:

- `makeMembership(options)` – builds deterministic tenant memberships (slug, id, roles).
- `makeSession(options)` – produces a full NextAuth session shape with derived role flags.
- `makeNeutralSession(options)` – creates a neutral (no tenant selected) session.
- `makeTenantSession(options)` – creates a session with a selected tenant (defaults to the first membership).
- `makeUnauthenticatedSession()` – mirrors the unauthenticated session state for guard rails.

When you mock `useSession`, populate its `data` with one of these helpers. This keeps derived booleans (`isAdmin`, `canApprove`, etc.) aligned with the backend `computeBooleansForTenant` logic.

### Auth MSW handlers (`apps/web/test/fixtures/mswAuthHandlers.ts`)

`test/setup.ts` registers shared handlers for `/api/tenant/select` and `/api/auth/refresh`. Tests can inspect and tweak their behavior via the exported utilities:

- `authMockState.tenantSelect.calls` – inspect POST bodies/headers issued during tenant switches.
- `authMockState.refresh.calls` – inspect refresh requests.
- `configureRefreshResponse(factory)` – override the default JSON body or cookie returned by the refresh handler.
- `resetAuthMocks()` – invoked automatically after each test; call manually only if a test alters state mid-run.

Avoid spying on `global.fetch` directly. Instead, let the MSW handlers capture requests, then assert against `authMockState` or customise the handler with `configureRefreshResponse`/`server.use`.

### MSW server access inside tests

`test/setup.ts` exposes the MSW server on `globalThis.__mswServer`. In component tests that need bespoke responses, import `http`/`HttpResponse` from `msw` and register overrides:

```ts
const server = (globalThis as { __mswServer: import('msw/node').SetupServer }).__mswServer;

server.use(
  http.put('http://localhost/api-proxy/tenants/settings', async ({ request }) => {
    const body = await request.json();
    return HttpResponse.json({ ok: true });
  }),
);
```

Handlers registered inside a test are reset automatically in `afterEach` via `server.resetHandlers()`.

### Proxy header integration harness (`apps/web/src/lib/proxyHeaders.test.ts`)

Integration tests for `buildProxyHeaders` exercise the real rotation bridge with a stub cookie jar. Key patterns to reuse:

- Use the `StubCookieStore` helper from the test to seed cookies (`rt`, `selected_tenant`, session token).
- Let MSW handle refresh responses; the test covers rotation caching, reuse eviction, and concurrency coalescing.
- Pass a diagnostics object (`ProxyDiagnostics`) when you want to assert failure reasons (`missing_refresh`, `access_unavailable`, etc.).

When adding new scenarios, prefer extending this suite instead of mocking internals of `proxyHeaders.ts`.

## Writing New Tests

1. **Always render with the shared fixtures** – import the session helpers and MSW auth state as needed. This keeps session shape drift-free.
2. **Prefer MSW to fetch spies** – register a handler with `server.use` to validate request payloads and status handling.
3. **Reset custom handler state** – rely on `resetAuthMocks()` (already invoked in `afterEach`) to clear captured calls, and avoid leaking overrides between tests.
4. **Assert cookie/tenant precedence through integration tests** – if a feature depends on cookie vs session state, add or update the `buildProxyHeaders` tests rather than stubbing behavior.
5. **Surface auth failures in UI tests** – use MSW to return 401/403 responses and assert that components raise their alert or error state (`role="alert"`, toast copy, etc.).

## Debugging Tips

- Enable the proxy debug logs by running tests with `NODE_ENV=development` (default). `buildProxyHeaders` logs rotation and failure paths prefixed with `[proxyHeaders]`.
- If a test unexpectedly hits the network, Vitest will warn because the MSW server is configured with `onUnhandledRequest: 'bypass'`. Add an explicit handler or adjust assertions accordingly.
- Use `configureRefreshResponse` to simulate rotated cookies, missing tenant tokens, or refresh failures without re-registering handlers.

## When to Extend This Guide

- New auth-related endpoints (e.g., session enumeration) should document their MSW fixtures here.
- If you add more shared helpers (e.g., Playwright flows or NextAuth client utilities), summarize them and reference concrete examples so future tests remain consistent.
