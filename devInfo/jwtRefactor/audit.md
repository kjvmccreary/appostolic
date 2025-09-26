# Frontend Auth Fixture Audit — 2025-09-25

## Purpose

- Catalog the current mocks, fixtures, and helpers used by the web test suite when simulating authentication, tenant selection, and API proxying.
- Highlight confidence gaps created by the mismatch between mocked flows and the real JWT + refresh rotation pipeline.
- Provide the remediation backlog required to align tests with production behavior before finalizing Story 8 (silent refresh) rollouts.

## Fixture & Mock Inventory

### Global Test Harness

- `apps/web/test/setup.ts` spins up an empty MSW server (`setupServer()` with no default handlers). Tests rarely register handlers, so most network calls are intercepted by ad-hoc `global.fetch` mocks rather than shared MSW flows.
- Base URL is forced to `http://localhost` and `NEXT_PUBLIC_WEB_BASE` mirrors that value, but there is no fixture ensuring cookie propagation or simulating refresh responses.

### API Proxy Route Tests (`apps/web/test/api-proxy/*`)

- Each route test (`agents`, `invites.accept`, `notifications.dlq`) mocks `buildProxyHeaders` outright. The real header builder handles refresh rotation, cookie bridging, and tenant-cookie precedence, but tests simply assert the mocked context is forwarded.
- `serverEnv.API_BASE` is stubbed per test via `vi.mock`, keeping assertions focused on URL composition but skipping auth failure scenarios tied to refresh cookie reuse or tenant claim divergence.
- No coverage ensures the proxy retries 401s via refresh or respects rotated cookies.

### Component Tests Mocking `useSession`

- `TenantSwitcher.test.tsx`, `TenantSwitcherModal.test.tsx`, `ProfileMenu.test.tsx`, `AgentsTable.test.tsx`, and `TopBar.test.tsx` all provide bespoke `useSession` mocks. They hard-code `tenant`, `memberships`, `roles`, and `canCreate` flags instead of deriving state from a shared fixture.
- Session updates are asserted by spying on `mockSession.update` (or returning a static object). None of the tests confirm that `/api/tenant/select` responses rotate refresh cookies or that `router.refresh()` resolves after SSR hydration.
- Local storage hints (recent tenant selection) are verified in `TenantSwitcherModal.test.tsx`, but there’s no test exercising cookie precedence logic or the `selected_tenant` cookie itself.

### Server Components & Layout Tests

- `app/logout/logout.multiTenantFlow.test.tsx` mocks `getServerSession` directly and uses `render` on the serialized JSX. It confirms TopBar visibility toggles but does not model the server cookie store or `selected_tenant` cookie. There’s no guard ensuring SSR still behaves correctly when the cookie and session fall out of sync.
- `app/layout.multiTenantNoSelection.test.tsx` covers the pure function `shouldShowTopBar` in isolation, not the real request context.

### Form & Mutation Tests in Tenant Settings

- `TenantSettingsForm`, `TenantLogoUpload`, `TenantGuardrailsForm`, and `TenantBioEditor` tests patch `global.fetch` with inline spies returning success. They validate payload shapes but bypass proxy header construction, session claims, and cookie forwarding. CSRF/refresh failure paths are not exercised.

### Middleware & Auth Boundary Tests

- `test/middleware.test.ts` stubs `next-auth/jwt.getToken` to emulate logged-in vs logged-out states. Requests are lightweight objects without cookies, so the test cannot detect regressions in cookie parsing or header forwarding.
- Login page tests mock `signIn` and `useSession` but don’t validate refresh cookies or access token scheduling coming from the backend.

### Gaps & Risks

- No fixture reproduces the neutral → tenant token handoff; we never assert that the client replays requests after a 401 using the refresh endpoint.
- Cookie precedence (`selected_tenant` vs session claim) is untested on the client; regressions like the blank Tenant B scenario slip through.
- The MSW server is underutilized, so network-heavy components rely on fetch mocks that can’t detect header omissions or cookie handling regressions.
- Session mocks differ per test file, increasing drift risk when claim shapes evolve.

## Recommended Remediation Steps

1. **Establish shared session factories**
   - Create a `test/fixtures/authSession.ts` exporting helpers such as `makeNeutralSession`, `makeTenantSession`, and `makeMembership` that match real NextAuth session shapes (including `membership.roles` arrays and expiration timestamps).
   - Update component and layout tests to import these factories instead of hard-coded objects.

2. **Introduce MSW auth/select handlers**
   - Provide default MSW handlers for `/api/tenant/select` and `/api/auth/refresh` that emulate refresh rotation (set-cookie `rt`, JSON payload with access expiry).
   - Refactor tenant-switcher tests to rely on these handlers so we validate that `fetch` calls include credentials and handle rotated cookies.

3. **Proxy header integration tests**
   - Add a focused suite executing the actual `buildProxyHeaders` implementation with a mocked `RequestCookies` store and session fixture. Cover:
     - Neutral session without tenant claim → returns null (`401`).
     - Session + cookie mismatch → prefers cookie slug (assert debug log or header result).
     - Expired cached access token → triggers refresh, stores rotated cookie, and surfaces new Authorization header.

4. **Tenant settings form end-to-end tests**
   - Replace direct fetch mocks with MSW handlers that require Authorization headers. Assert that missing headers (simulating stale tenant token) cause the component to surface auth errors.

5. **SSR alignment harness**
   - Introduce a server-side test helper that builds a `RequestCookies` jar plus session output from `getServerSession`. Use it in layout/logout tests to cover divergence between cookie and session and ensure the SSR guard rails stay intact.

6. **Regression guard for cookie precedence**
   - Add a unit test around `buildProxyHeaders` (or a dedicated helper) verifying that when both session tenant and `selected_tenant` exist but differ, the cookie wins and a warning is logged.

7. **Document playbook**
   - Capture the new fixtures/handlers in `docs/frontend-auth-testing.md` (or similar) so future tests default to the shared helpers instead of ad-hoc mocks.

## Next Steps (Immediate Work Queue)

1. ✅ Shared session fixture module landed (`test/fixtures/authSession.ts`) and wired into `TenantSwitcher*`, `ProfileMenu`, `AgentsTable`, and `TopBar` tests.
2. ✅ Default MSW auth handlers shipped (`test/fixtures/mswAuthHandlers.ts`), registered in `test/setup.ts`, and tenant switcher tests now use them instead of manual fetch spies.
3. Author initial `buildProxyHeaders` integration tests covering cookie precedence and refresh rotation.
4. Circle back to tenant settings forms once the auth fixtures land, replacing fetch mocks with MSW flows and adding assertions for 401/403 surfacing.
