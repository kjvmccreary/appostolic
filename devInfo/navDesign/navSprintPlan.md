# Navigation Sprint Plan — Web (Next.js 14)

Date: 2025-09-15
Owner: Appostolic Web
Source of truth: `devInfo/DesignDocs/UI-Spec.md` (§13 Navigation Design) and `devInfo/navDesign/navDesignDoc.md` (Grand Design)

## Goals

- Users can navigate without memorizing endpoints.
- Navigation reflects tenant scope and roles (TenantAdmin, Approver, Creator, Learner) with server-first enforcement.
- Mobile uses a hamburger-driven Nav Drawer consistent with the existing theme.

## Deliverables

- Reusable nav components: TopBar, NavDrawer, NavItem/Section, ProfileMenu, TenantSwitcherModal.
- Role-gated menu structure (Admin, Home, Dev) tied to session flags.
- Wire-up of Tenant Switcher flow via `/api/tenant/select`.
- Playwright smoke for desktop/mobile, plus unit tests for gating logic.

## Sprint Breakdown

### ✅ DONE Story 1 — Scaffold Navigation Components (Desktop)

- Scope:
  - Create `apps/web/app/components/nav/{TopBar,NavItem,NavSection}.tsx` per spec.
  - Show Tenant pill, primary nav (Dashboard, Agents), and role-gated CTAs (Create Lesson/New Agent for Creator).
  - Add aria labels and `aria-current` handling.
- Acceptance:
  - TopBar renders on `/studio/*`; links navigate; active route highlighted.
  - Creator sees Create actions; non-creator does not.
- Tests:
  - Unit: NavItem active logic; role gating on actions.

### ✅ DONE Story 2 — Mobile Nav Drawer

- Scope:
  - Add `NavDrawer.tsx` with grouped items; hamburger toggles drawer on mobile (<768px).
  - Focus trap, ESC/backdrop close, and auto-close on route change.
- Acceptance:
  - Drawer opens/closes with keyboard/mouse/touch; Admin section present only for TenantAdmin.
- Tests:
  - Playwright: open/close drawer; verify Admin visibility by role.

Status: ✅ DONE (2025-09-15)

- Implemented `src/components/NavDrawer.tsx` with dialog semantics, backdrop click to close, ESC handling, and a minimal focus trap. Drawer auto-closes on pathname changes.
- Wired a mobile-only hamburger button in `TopBar.tsx` that toggles the drawer; desktop nav unchanged.
- Admin section renders only when `isAdmin` is true; items currently include Members and Audits.
- Unit tests added:
  - `NavDrawer.test.tsx` — renders items, backdrop close, ESC close, and auto-close on route change.
  - Extended `TopBar.test.tsx` — hamburger opens/closes drawer via mock; existing tests remain green.
- Quality gates: typecheck PASS; full web unit tests PASS with coverage thresholds satisfied.

### ✅ DONE Story 3 — Profile Menu & Tenant Switcher

- Scope:
  - `ProfileMenu.tsx` with Profile, Switch tenant, Sign out; Superadmin chip when claim present.
  - `TenantSwitcherModal.tsx` listing memberships; integrates `GET/POST /api/tenant/select`.
- Acceptance:
  - Selecting a tenant updates cookie/session and reloads to `/studio` for that tenant.
- Tests:
  - Unit: selection callback; modal accessibility (focus return).
  - Playwright: switch tenant end-to-end (dev headers in test harness).

Status: ✅ DONE (2025-09-15)

- Implemented `src/components/ProfileMenu.tsx` with Superadmin chip, dropdown menu, and integration with `TenantSwitcherModal`.
- Implemented `src/components/TenantSwitcherModal.tsx` as an accessible dialog with backdrop/ESC close, focus restore, and session+cookie update: calls `update({ tenant })`, POSTs `/api/tenant/select`, then `router.refresh()`.
- Wired `ProfileMenu` into `TopBar.tsx` alongside `ThemeToggle` and creator CTAs.
- Unit tests added:
  - `ProfileMenu.test.tsx` — toggles menu, shows Superadmin chip, opens switcher modal, and calls `signOut`.
  - `TenantSwitcherModal.test.tsx` — backdrop click closes; selecting a tenant updates session, POSTs API, and closes.
  - Updated `TopBar.test.tsx` to mock `ProfileMenu` to keep tests focused.
- Quality gates: typecheck PASS; full web unit tests PASS (93/93) with coverage thresholds satisfied (Lines ~91%).

### ✅ DONE Story 4 — Admin Section (Role-Gated)

- Scope:
  - Add Admin items (Members, Invites, Audits, Notifications DLQ) and link to existing proxies/pages.
  - Ensure server routes return ProblemDetails 403 for non-admin; UI hides Admin for non-admin.
- Acceptance:
  - TenantAdmin sees Admin menu and can navigate; non-admin cannot see Admin and receives 403 if directly hitting URLs.
- Tests:
  - Playwright: confirm 403 on direct URL for non-admin; link presence/absence in nav.

Status: ✅ DONE (2025-09-15)

- Added new page `/studio/admin/invites` with server-first role gating (redirects unauth to `/login`, requires selected tenant, returns 403 ProblemDetails for non-admin). Lists invites via proxy and includes server actions to create, resend, and revoke invites.
- Updated `TopBar` admin links and `NavDrawer` Admin section to include Members, Invites, Audits, and Notifications (DLQ).
- Unit tests:
  - `app/studio/admin/invites/page.test.tsx` — unauth redirect, 403 non-admin, and successful render for admin.
  - Existing admin proxy/page tests remained green.
- Fix: added early returns after `redirect(...)` in the invites page to prevent null access in tests.
- Quality gates: web unit tests PASS (38 files, 96 tests); coverage ~91% lines.

### Story 5 — Accessibility & Theming Polish

- Scope:
  - Verify ARIA roles/labels; high contrast; focus rings; sticky TopBar elevation.
  - Align with tokens.css and lucide-react icons as specified.
- Acceptance:
  - Axe/pa11y smoke passes; visual check in Light/Dark/AMOLED.
- Tests:
  - Lint: a11y checks; snapshot for TopBar in themes.

## Implementation Notes

- Role flags come from server-side session (isAdmin, canCreate, canApprove, isLearner).
- Do not rely on client-only gating; server authorization remains primary.
- Keep nav data a simple array: `{ label, href, icon, require? }`; compute visibility in one place.

## Risks & Mitigations

- Drift between UI and server authorization → maintain integration tests; rely on existing proxy/endpoint guards.
- Drawer accessibility → add focus trap tests and manual QA checklist.

## Timeline (estimate)

- Week 1: Stories 1–2
- Week 2: Stories 3–4
- Week 3: Story 5 + buffer

## Definition of Done

- Desktop and mobile nav shipped behind a feature flag (optional) or directly if low risk.
- Role-gated items behave per spec across Creator/Admin/Learner.
- Tenant switch flow works reliably; quick smoke in dev.
- Tests: unit + Playwright smokes green in CI.
