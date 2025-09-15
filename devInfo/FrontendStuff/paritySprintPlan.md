# Frontend Parity Sprint Plan — Backend → Web Pages

Date: 2025-09-15
Owner: Web Team
Scope: Ensure that every existing backend capability with public/admin endpoints has a matching, styled frontend page or feature in the Next.js app. Focus on tenant-aware, role-gated Admin features and essential Dev/Studio capabilities that already exist on the API.

## Goals

- Close parity gaps so users can perform all supported operations via the web UI without needing raw API calls.
- Maintain server-first authorization; UI visibility mirrors session flags (TenantAdmin, Approver, Creator, Learner).
- Ship accessible, styled pages consistent with current design tokens and patterns (TopBar/NavDrawer, focus rings, dialog semantics).
- Keep tests green with coverage thresholds; add targeted unit/integration tests for new pages and proxy routes.

## Quality Gates

- Typecheck: PASS
- Unit tests (web): PASS; maintain coverage ≥ 90% lines
- Lint: PASS with a11y checks
- API unchanged (unless flagged); existing API + tests remain green

## Inventory: Existing Backend Capabilities → Planned Web Pages

1. Tenants / Memberships (Admin)

- Backend: Members listing; Roles update; Invites create/list/resend/revoke.
- Web parity:
  - Page: `/studio/admin/members` (exists; styling polish + controls refinement)
  - Page: `/studio/admin/invites` (exists; styling polish + create/resend/revoke UX)
  - Improvements: Add empty states, validation errors, and success toasts; confirm dialogs for revoke.

2. Audits (Admin)

- Backend: `GET /api/tenants/{tenantId}/audits` with filters and paging.
- Web parity:
  - Page: `/studio/admin/audits` (exists; polish filters and table visuals; add details drawer if needed)

3. Notifications Admin

- Backend: DLQ listing and replay endpoints; resend (manual/bulk) admin endpoints.
- Web parity:
  - Page: `/studio/admin/notifications` (exists for DLQ; add bulk replay UI, filter presets, and action feedback)

4. Agent Tasks (Dev/Studio)

- Backend: `GET /api/agent-tasks` (filters, paging, X-Total-Count), `POST /api/agent-tasks/{id}/cancel`, `POST /api/agent-tasks/{id}/retry`, `GET /api/agent-tasks/{id}/export`.
- Web parity:
  - Pages: `/studio/tasks` (exists), `/studio/tasks/[id]` (exists). Polish table (density, columns), details layout, and export affordances.

5. Agents (Studio)

- Backend: Agents CRUD and tooling metadata.
- Web parity:
  - Pages: `/studio/agents` and `/studio/agents/[id]` (exist). Polish forms (validation, helper text), and surface `isEnabled` state more clearly.

6. Auth + Tenant Selection

- Backend: Signup/login; `GET|POST /api/tenant/select` for cookie; dev-session inspector.
- Web parity:
  - Pages: `/signup`, `/login`, `/select-tenant` (exist). Ensure single-membership auto-select (implemented), refine UX for multiple tenants; ensure callback `next` handling.

7. Dev Utilities (Optional, gated)

- Backend: Dev endpoints for notifications health, ping, and role grants (guarded by config/key).
- Web parity:
  - Pages: Minimal internal-only diagnostics under `/dev/*` (already partially present) with clear dev-only banners.

## Stories and Acceptance Criteria

### ✅ (DONE) Story A — Admin: Invites UX polish and actions

- Scope: Style `/studio/admin/invites` with consistent table, form controls, and action buttons; add confirmation for revoke; inline validation messages; success/error toasts.
- Acceptance:
  - Create invite validates email/role; on success, shows toast and refreshes list.
  - Resend and revoke show confirmation (revoke) and a toast; table updates.
  - Keyboard + screen reader: form labels accessible; buttons have names; dialog semantics for confirms.
- Tests: Unit tests for happy path and error toast; snapshot of table header; confirm revoke.

### ✅ (DONE) Story B — Admin: Members roles UX polish

- Scope: Improve `/studio/admin/members` visuals and interactions; add loading states and save feedback; keep last-admin guard messaging.
- Acceptance:
  - Role toggles reflect pending state; save path shows success; last-admin guard surfaced in UI.
  - Accessible checkbox labels; disable states explained via aria-describedby.
- Tests: Unit test last-admin UI guard; save success path; error surface.

### ✅ (DONE) Story C — Admin: Audits table and filters refinement

- Scope: Enhance `/studio/admin/audits` with quick filters (date range presets, user selectors), column formatting (role names, timestamps), and pager controls.
- Acceptance:
  - Filters submit via GET; results reflect `X-Total-Count`; clear empty state.
  - Timezone-aware formatting; role flags decoded; table keyboard nav intact.
- Tests: Proxy route already covered; add page unit test for filter->URL query sync.

### ✅ (DONE) Story D — Admin: Notifications DLQ + bulk actions

- Scope: Extend `/studio/admin/notifications` to expose bulk replay (with caps), per-row replay, and filter presets (status/kind); show remaining cap when returned via headers.
- Acceptance:
  - Bulk replay shows confirmation modal; displays remaining capacity; lists replay summary.
  - DLQ list paginated; empty state and warning banner.
- Tests: Unit tests for header propagation and confirm flow; route tests for guard remain green.

### ✅ (DONE) Story E — Tasks: Details/export ergonomics

- Scope: Refine `/studio/tasks` and `/studio/tasks/[id]` visuals; add copy-to-clipboard for IDs; expose Export as download link with proper filename.
- Acceptance:
  - Export triggers browser download with filename `task-<id>.json`.
  - Details view shows trace kinds clearly; badges styled per status.
  - Copy-to-clipboard available for Task ID in both list and detail.
- Tests: Unit test export link `download` attribute; tests for copy ID in detail and list.

### ✅ (DONE) Story F — Agents: Editor form polish

- Scope: Improve validation feedback, tool allowlist hints, and disabled-state surfacing; add “New Agent” empty-state CTA improvements.
- Acceptance:
  - Invalid fields show inline messages; helper text for tools; `isEnabled` toggles clearly.
- Tests: Unit test invalid field path; isEnabled toggle behavior (added).

### ✅ (DONE) Story G — Auth/Tenant: Multi-tenant UX polish

- Scope: For users with multiple memberships, enhance `/select-tenant` to show roles badges and remember last choice; ensure return to deep links via `next` param.
- Acceptance:
  - List shows role badges; once selected, next visits default to last tenant unless overridden.
- Tests: Unit test that `next` is respected and only same-origin absolute paths are allowed. (Added)

## Delivery Phases and Milestones

- Phase 1 (Week 1): Stories A, B
- Phase 2 (Week 2): Stories C, D
- Phase 3 (Week 3): Stories E, F
- Phase 4 (Week 4): Story G + buffer

## Cross-Cutting

- Styling: Use existing tokens and utilities (focus-ring, surface colors, border lines). Keep TopBar elevation and a11y patterns consistent.
- Accessibility: Ensure aria-current, aria-labels, aria-expanded booleans, dialog roles, and keyboard behavior.
- Server-first: All changes mirror server guards; UI hides actions when unauthorized; proxy routes continue to enforce guards.
- Observability: Add minimal console.info logs for dev-only scenarios; avoid sensitive data.

## Acceptance Demo Checklist

- Admin
  - Invite user flow (create, resend, revoke) works and is styled
  - Members role changes with proper guard messaging
  - Audits list with filters and paging; counts match
  - Notifications DLQ and bulk replay
- Tasks
  - Inbox + detail pages refined; export works as download
- Agents
  - Editor polish and isEnabled clarity
- Auth/Tenant
  - Single membership auto-select (already implemented)
  - Multi-tenant selection UX polish
