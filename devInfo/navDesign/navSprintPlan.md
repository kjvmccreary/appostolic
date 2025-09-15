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

### Story 1 — Scaffold Navigation Components (Desktop)

- Scope:
  - Create `apps/web/app/components/nav/{TopBar,NavItem,NavSection}.tsx` per spec.
  - Show Tenant pill, primary nav (Dashboard, Agents), and role-gated CTAs (Create Lesson/New Agent for Creator).
  - Add aria labels and `aria-current` handling.
- Acceptance:
  - TopBar renders on `/studio/*`; links navigate; active route highlighted.
  - Creator sees Create actions; non-creator does not.
- Tests:
  - Unit: NavItem active logic; role gating on actions.

### Story 2 — Mobile Nav Drawer

- Scope:
  - Add `NavDrawer.tsx` with grouped items; hamburger toggles drawer on mobile (<768px).
  - Focus trap, ESC/backdrop close, and auto-close on route change.
- Acceptance:
  - Drawer opens/closes with keyboard/mouse/touch; Admin section present only for TenantAdmin.
- Tests:
  - Playwright: open/close drawer; verify Admin visibility by role.

### Story 3 — Profile Menu & Tenant Switcher

- Scope:
  - `ProfileMenu.tsx` with Profile, Switch tenant, Sign out; Superadmin chip when claim present.
  - `TenantSwitcherModal.tsx` listing memberships; integrates `GET/POST /api/tenant/select`.
- Acceptance:
  - Selecting a tenant updates cookie/session and reloads to `/studio` for that tenant.
- Tests:
  - Unit: selection callback; modal accessibility (focus return).
  - Playwright: switch tenant end-to-end (dev headers in test harness).

### Story 4 — Admin Section (Role-Gated)

- Scope:
  - Add Admin items (Members, Invites, Audits, Notifications DLQ) and link to existing proxies/pages.
  - Ensure server routes return ProblemDetails 403 for non-admin; UI hides Admin for non-admin.
- Acceptance:
  - TenantAdmin sees Admin menu and can navigate; non-admin cannot see Admin and receives 403 if directly hitting URLs.
- Tests:
  - Playwright: confirm 403 on direct URL for non-admin; link presence/absence in nav.

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
