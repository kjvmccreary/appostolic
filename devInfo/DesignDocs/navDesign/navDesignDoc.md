# Appostolic Navigation — Grand Design (v1)

Date: 2025-09-15
Owner: Appostolic Core

## Goals

- Make core functionality discoverable without memorizing endpoints.
- Respect tenant scoping and role-based access at the UI level while keeping API as source of truth.
- Share a single mental model across desktop and mobile (Nav Drawer) with minimal divergence.
- Integrate seamlessly with existing theme and tokens; keep footprint small and testable.

## Information Architecture (IA)

- Home
  - Dashboard (/studio)
  - Agents (/studio/agents)
- Admin (TenantAdmin)
  - Members (/studio/admin/members)
  - Invites (/studio/admin/invites)
  - Audits (/studio/admin/audits)
  - Notifications DLQ (/studio/admin/notifications/dlq)
- Dev (Development)
  - Agents (dev) (/dev/agents)
  - Health (/dev/health)
- Profile
  - Profile page (/studio/profile) — placeholder
  - Switch tenant — modal → GET/POST /api/tenant/select
  - Sign out

Notes:

- “Home” can collapse to just “Agents” if Dashboard is redundant; keep Dashboard as the default landing for now.
- Future: Approvals (Approver), Content (Learner) can be slotted as their own top-levels.

## Role and State Matrix

| Area                   | Learner | Creator                 | Approver | TenantAdmin | Superadmin (dev)               |
| ---------------------- | ------- | ----------------------- | -------- | ----------- | ------------------------------ |
| Dashboard              | View    | View                    | View     | View        | View                           |
| Agents                 | View    | View/Create/Edit/Delete | View     | View        | View                           |
| Admin: Members/Invites | —       | —                       | —        | Full        | Cross-tenant (where supported) |
| Admin: Audits          | —       | —                       | —        | View        | Cross-tenant (where supported) |
| Admin: DLQ             | —       | —                       | —        | View/Replay | Cross-tenant (where supported) |
| Approvals (future)     | —       | —                       | Approve  | —           | —                              |
| Profile/Switch Tenant  | Yes     | Yes                     | Yes      | Yes         | Yes                            |

Implementation:

- Session-derived flags (isAdmin, canCreate, canApprove, isLearner) drive UI visibility.
- Server guards remain primary; UI reflects allowed actions to avoid dead-ends.

## Responsive Patterns

- Desktop (≥768px):
  - TopBar with: Tenant pill (left), primary nav (center-left), actions + Profile (right).
  - Breadcrumbs optional below TopBar for deep pages.
- Mobile (<768px):
  - TopBar with hamburger (left), title (center), tenant + profile (right).
  - Nav Drawer (slide-in, left): sections Home, Admin (gated), Dev (dev only), Profile.
  - Drawer uses focus trap, closes on route change, ESC, or backdrop.

## Components

1. TopBar

- Props: `tenantName`, `tenantSlug`, `sessionRoles`, `onToggleDrawer()`
- Slots: left (tenant pill/hamburger), middle (nav), right (actions, profile)
- Actions: Create Lesson (Creator only), New Agent (Creator)

2. NavItem / NavSection

- NavItem: `{ href, label, icon, require?: 'canCreate'|'isAdmin'|'canApprove' }`
- NavSection: label + items; collapsible on mobile

3. NavDrawer

- State: open/closed; trap focus when open
- Renders NavSections; Admin section hidden unless isAdmin

4. ProfileMenu

- Items: Profile, Switch tenant, Sign out; shows Superadmin chip when claim present

5. TenantSwitcherModal

- Lists memberships; calls `/api/tenant/select` and reloads
- Accessibility: listbox pattern; return focus to opener

## Accessibility

- `aria-current="page"` on active links; Landmark roles: `<nav>` for primary/drawer, `<header>` for TopBar.
- Keyboard: Drawer opens with Enter/Space on hamburger; Esc to close; focus trapped.
- Screen-reader labels on icons; avatar has `aria-label="Open profile menu"`.

## Theming & Style

- Use existing tokens (tokens.css); keep elevation subtle on TopBar (var(--shadow-2)).
- Icons: lucide-react; keep outline style consistent.

## Implementation Plan (phased)

Phase 1 — Scaffold and Wire (low risk)

- Add shared components in `apps/web/app/components/nav/`: TopBar, NavDrawer, NavItem, ProfileMenu, TenantSwitcherModal.
- Hook into existing session helpers to compute flags; provide via context.
- Replace current ad-hoc links on `/studio/*` with Nav components.

Phase 2 — Role Gating and Admin

- Render Admin section conditionally; wire Members, Invites, Audits, DLQ routes.
- Ensure server proxies enforce roles and return ProblemDetails.

Phase 3 — Mobile Polish and Tests

- Add Drawer transitions, focus trap tests, and Playwright e2e for gating.
- Add breadcrumbs for deep pages as needed.

## Risks & Mitigations

- Drift between UI gating and API enforcement → Mitigate with integration tests asserting 403 and UI hides actions accordingly.
- Drawer accessibility regressions → Add unit/e2e tests for focus trap and keyboard.

## Acceptance Criteria

1. Desktop and mobile nav show correct sections based on roles.
2. Tenant Switcher updates `selected_tenant` and refreshes.
3. Admin menu only visible to TenantAdmin; Creator-only CTAs hidden when lacking permission.
4. Keyboard/ARIA pass in CI (unit + Playwright smoke).
