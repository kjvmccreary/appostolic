# Appostolic — Architecture Snapshot (2025-09-15)

This document describes the structure, runtime, and conventions of the Appostolic monorepo. It’s organized to group related topics together for easier navigation and future updates.

## What’s new

- Admin UX — Tenant switcher centering + Invites Accepted state (2025-09-16)
  - Web: Centered `TenantSwitcherModal` and prevented cut‑off by using a full‑screen flex container with `items-center justify-center` and making the dialog panel scrollable via `max-h` + `overflow-auto`.
- Auth Flows: Forgot Password styled with accessible form and status messaging; Reset Password now reads token from URL (hidden field), adds confirm password with validation, and provides clear success/error feedback.
  - Web: `/studio/admin/invites` now surfaces acceptance state from the API. The table shows a Status chip: Accepted (green) when `acceptedAt` is set, Pending (amber) otherwise. When an invite has been accepted, the Resend/Revoke actions are hidden to avoid invalid operations. Also fixed a broken `ConfirmSubmitButton` import and restored the Expires column cell to match the header.

- Admin Invites — roles flags + HTML email (2025-09-16)
  - Web: `/studio/admin/invites` now uses granular roles flags (TenantAdmin, Approver, Creator, Learner) in the dropdown and sends `{ email, roles: [...] }` to align with the API’s `invitations.roles` column. Server actions redirect with `?ok=` only on success to avoid false error toasts.
  - API: Invite create/resend emails sent via Mailhog now use a small HTML body with an Accept link, tenant name, selected role, and expiry; `IsBodyHtml = true` set for better dev readability.

- Web — Studio: Tasks ergonomics (Completed)
- Agents: Editor form polished (a11y helper texts, inline validation, clear isEnabled toggle) and test added for isEnabled payload.
  - Task detail and inbox now include copy-to-clipboard for Task IDs (detail header and table ID column).
  - Export action guarantees a predictable filename fallback `task-<id>.json` when Content-Disposition is absent.
  - Tests cover retry/cancel, export filename fallback, and copy actions; full web suite green.

- Web — Admin: Audits UI polish (Completed)
  - `/studio/admin/audits` now has a more complete UX: quick date presets (Today, 7/30 days), styled filter form, formatted table with role flag decoding to names, a compact pager that reads `X-Total-Count`, and clear empty/error states.
  - Server page accepts defaulted `searchParams` to support test invocation without args. Tests added to assert: non-admin 403 render, pager text computed from `X-Total-Count`, and Prev/Next link query sync.
  - Navigation note: Admin links (Members, Invites, Audits, Notifications) are visible in the desktop TopBar for admins; mobile drawer remains role-aware. Existing guard logic remains server-first.

- Web — Admin: Invites UX polish (phase 1)
  - `/studio/admin/invites` now shows status banners based on `ok`/`err` query parameters after server actions (create/resend/revoke). Revoke uses a small client helper `ConfirmSubmitButton` to ask for confirmation before submitting the server-action form, avoiding accidental revocations while keeping server-first behavior.
  - Tests extended to cover banner rendering and error fallback when invites fetch fails. Next phase will add toast notifications, empty/validation states, and an accessible confirm dialog.

- Web — Admin: Invites UX polish (phase 2)
  - Added a lightweight global toast system and switched `/studio/admin/invites` from SSR banners to client toasts derived from `ok/err` query parameters (then stripped from the URL). Replaced `window.confirm` with an accessible modal `ConfirmDialog` used by `ConfirmSubmitButton`.
  - Added an empty state when no invites exist and introduced a validated `EmailField` client component that announces inline errors using aria-invalid/aria-describedby and role=alert after first blur.

- Web — Admin: Members roles UX polish
  - `/studio/admin/members` now provides save feedback via client toasts triggered from `ok/err` query parameters after server action redirects. Checkboxes expose `data-pending` during submission, and the last-admin safety rule is surfaced with an accessible helper message using `aria-describedby`.
  - Tests updated and hardened by adding `useToastOptional()` (a no-throw hook for isolated renders) and wrapping the shared test Providers with `ToastProvider`.

- Docs — Frontend ↔ Backend Parity Sprint Plan (Added)
  - Introduced `devInfo/FrontendStuff/paritySprintPlan.md`, a concrete plan to align the Next.js web UI with existing backend capabilities. It inventories Admin (Members, Invites, Audits, Notifications), Studio (Agents, Agent Tasks), and Auth/Tenant flows, defines stories with acceptance criteria, quality gates (typecheck, tests, lint, a11y), and a phased timeline.

- Web — Navigation Sprint: A11y & theming polish (Completed)
  - TopBar now gains a subtle elevation (shadow) when the page is scrolled; preserves sticky behavior and accessibility. Mobile hamburger has a clear `aria-label`; desktop nav landmark is named; `aria-expanded` values are proper booleans; dialog semantics affirmed for `NavDrawer` and `TenantSwitcherModal` with focus handling.
  - Tests extended to validate elevation toggle and accessible labels; full web test suite remains green with coverage ~91.5% lines.

- Web — Navigation Sprint: Admin section (Role-gated) (Completed)
  - Added `/studio/admin/invites` page guarded server-side: unauthenticated users redirect to `/login`, a selected tenant is required, and non-admins receive RFC7807 403. Page lists invites through the existing `/api-proxy/tenants/[tenantId]/invites` route and supports server actions to create, resend, and revoke invites.
  - Updated nav: Admin section now includes Members, Invites, Audits, and Notifications (DLQ) in `TopBar` and `NavDrawer`. UI mirrors server roles (TenantAdmin) while authorization remains server-first.
  - Tests: unit tests cover unauth redirect, 403 for non-admin, and admin render; full web suite passes with coverage ~91% lines.

- Web — Navigation Sprint: Profile menu and tenant switching (Completed)
  - Added a lightweight `ProfileMenu` in the web header with Superadmin chip, a Switch tenant action, and Sign out. The switch action opens `TenantSwitcherModal`, which lists memberships from the NextAuth session, updates the session via `session.update({ tenant })`, persists the `selected_tenant` cookie via `POST /api/tenant/select`, and triggers a client refresh (`router.refresh()`).
  - Integrated `ProfileMenu` into `TopBar` alongside existing CTAs and `ThemeToggle`. The modal supports backdrop/ESC close and restores focus to the trigger.
  - Tests cover dropdown toggle, superadmin visibility, modal open/close, and update flow; full web suite remains green with coverage >90% lines.

- Web — Auth/Tenant: Multi-tenant UX polish (Completed)
  - `/select-tenant` now validates same-origin `next` and auto-selects when a single membership exists; added tests for safe/unsafe `next` values.
  - Tenant switcher modal shows role badges and hints the last selected tenant from localStorage for quick re-selection; cookie remains authoritative for server reads.

- Web — Various FE cleanup (auth/nav/login)
  - TopBar hides primary nav when logged out (shows Sign in). Login page styled and now links to Sign up and Magic Link (both preserve `next`). Members page includes an "Invite members" link. Mobile drawer backdrop made more opaque; Tenant Switcher modal alignment improved. Tests updated; full web suite green.

- IAM — Sprint 4.1: Seeds + dev roles utility (Completed)
  - Seeded baseline users with distinct Roles per tenant (Admin, Approver, Creator, Learner) via an idempotent seed that converges memberships and augments Owner composite flags when needed.
  - Added developer utility endpoint `POST /api/dev/grant-roles` that accepts `tenantId` or `tenantSlug`, `email`, and `roles[]` (case-insensitive names). It auto-creates the user if missing, ensures membership, and sets role flags.
  - Guard: when configuration `Dev:GrantRolesKey` is present, `POST /api/dev/grant-roles` requires header `x-dev-grant-key` with the same value; otherwise it’s open in dev/test.
  - Standardized the “pencil” model for roles: `Membership.Roles` is now mutable with a new method `ApplyRoleChange(...)` that updates flags and returns an `Audit` entry when a change occurs; the dev endpoint now persists that audit.
  - Routing reliability: removed earlier environment-gated mapping that hid dev routes under tests; temporary endpoint-enumeration diagnostics removed after validation.
  - Verification: Full API test suite PASS (138/138) after changes; added an explicit test asserting that updating an existing membership’s roles writes an `Audit` with correct `OldRoles`/`NewRoles`.

<a id="roles-matrix"></a>

### Roles → Capabilities matrix (tenant-scoped)

- TenantAdmin:
  - Manage memberships (list, invite, resend, delete, set roles)
  - Access audits listing for the tenant
  - Access admin pages in web (e.g., Studio members)
- Approver:
  - Approve/publish content (future endpoints; policy present)
- Creator:
  - Create/edit content (e.g., lessons; agents CRUD)
- Learner:
  - Read-only access to learning content

<a id="dev-grant-roles-guard"></a>

### Dev grant-roles guard

- Endpoint: `POST /api/dev/grant-roles`
- Behavior: When configuration `Dev:GrantRolesKey` is present, requests must include header `x-dev-grant-key` with the same value. When not set, the endpoint is available by default in dev/test.

- IAM — Sprint 3.3: Audit trails for membership role changes (Completed)
  - Added `app.audits` table to persist role change events with: id, tenant_id, user_id, changed_by_user_id, changed_by_email, old_roles (int), new_roles (int), changed_at (utc).
  - Endpoint `POST /api/tenants/{tenantId}/memberships/{userId}/roles` writes an audit after successful updates (works for EF InMemory and relational providers). Indexed by `(tenant_id, changed_at)` for efficient per-tenant queries.
  - New admin endpoint `GET /api/tenants/{tenantId}/audits` lists recent audit entries for the tenant. Requires TenantAdmin, validates tenant claim vs. route, supports `take`/`skip` paging with `ChangedAt DESC` ordering, optional filters `userId`, `changedByUserId`, `from`, `to`, and sets `X-Total-Count` header.
  - Migration `20250915145000_s4_02_membership_roles_audits` applied; database is up to date (`make migrate`).
  - Migration `20250915173000_s4_03_audits_view` creates SQL view `app.vw_audits_recent` as a convenience for reporting. Applied via `make migrate`.
  - Web surfacing: Added proxy `GET /api-proxy/tenants/{tenantId}/audits` with TenantAdmin guard and header forwarding; Studio page `/studio/admin/audits` lists audits with basic filters and paging (reads `X-Total-Count`).
  - Hardening: Added vitest proxy route test (`apps/web/app/api-proxy/tenants/[tenantId]/audits/route.test.ts`) asserting TenantAdmin guard (403 on non-admin) and preservation of `X-Total-Count` header on success.
  - Hardening (post‑baseline): Manual GUID format validation for `userId` / `changedByUserId` query parameters added in audits listing endpoint to return 400 early on malformed GUID strings (prevents unnecessary DB query construction). UI now decodes numeric role flag bitmasks (TenantAdmin|Approver|Creator|Learner) into human‑readable lists in the audits table via `roleNamesFromFlags`; tests updated to assert 400 behavior for malformed GUID filters.

- IAM — Sprint 2.2: Invites include Roles (Completed)
  - Invitation model now captures granular Roles flags in addition to the legacy Role. Invite creation accepts an optional array of flag names and returns both roles (string) and rolesValue (int). When omitted, flags are derived from the legacy Role for backward compatibility (Owner/Admin → Admin+Approver+Creator+Learner; Editor → Creator+Learner; Viewer → Learner).
  - Accepting an invite creates the membership with both the legacy Role and the Roles flags from the invitation. Signup flows that consume an invite token also propagate Roles; personal-tenant creation derives Owner flags by default. Last‑admin invariant enforcement remains in place.
  - Tests cover flag parsing/validation (400 on invalid), persistence/listing of roles, and acceptance behavior.

- Web — IAM Sprint 2.4: Membership admin page (Completed)
  - Added server proxies to surface assignment APIs: `GET /api-proxy/tenants/{tenantId}/memberships` and `POST /api-proxy/tenants/{tenantId}/memberships/{userId}/roles`. Both are guarded server-side (Owner/Admin) via `guardProxyRole` and forward dev headers via `buildProxyHeaders`.
  - New page `/studio/admin/members` lists members for the selected tenant and exposes checkboxes for flags: TenantAdmin, Approver, Creator, Learner. The UI disables unchecking the last remaining TenantAdmin for safety; the API remains the source of truth and enforces the invariant.
  - Tests cover proxy handlers (guard and forwarding); page-level render tests are planned alongside route gating in Sprint 3.

- Web — IAM Sprint 2.3: Roles‑aware session (Completed)
  - NextAuth JWT/session now includes memberships with Roles flags when available and derives convenience booleans for the selected tenant: isAdmin, canApprove, canCreate, isLearner. A helpers module `apps/web/src/lib/roles.ts` normalizes flags from legacy roles when flags are absent.
  - Added a dev diagnostics endpoint `GET /api/debug/session` that returns the effective session, the `selected_tenant` cookie, and the derived booleans/roles for quick verification.
  - Unit tests cover the roles helpers and the session callback derivation, including tenant switching via `session.update({ tenant })`.

- IAM — Sprint 2.1: Membership assignment APIs (Completed)
  - Added GET /api/tenants/{tenantId}/memberships to list memberships including legacy Role and Roles flags (names and numeric value). Requires TenantAdmin and ensures the `tenant_id` claim matches the route.
  - Added POST /api/tenants/{tenantId}/memberships/{userId}/roles to replace Roles flags using an array of enum names (case-insensitive). Returns 200 on change with a roles summary, 204 on no-op, 400 on invalid names, and 404 when membership is missing. Enforces the “at least one TenantAdmin per tenant” invariant across both legacy Role and Roles flags and returns 409 Conflict when violated.
    - Auditability: The roles update endpoint writes an audit row capturing tenant, target user, changer identity, old/new flags, and timestamp in `app.audits`.
  - 403 responses are formatted as RFC7807 ProblemDetails via the custom authorization result handler; the RoleAuthorizationHandler maps legacy Role to flags for compatibility during transition.

- IAM — Sprint 1.3: Role policies and uniform 403s (Completed)
  - Added policy-based authorization: TenantAdmin, Approver, Creator, Learner. Applied to critical endpoints in V1 (Creator on lesson creation; TenantAdmin on members/invites management). Legacy `MembershipRole` is mapped to `Roles` flags in the auth handler for compatibility.
  - Introduced a custom authorization result handler to return RFC7807 ProblemDetails on Forbidden, with extensions including tenantId and requiredRoles. Added a small fallback middleware to cover manual `Forbid()` responses.

- IAM — Sprint 1.4: TenantAdmin invariant (Completed)
  - Enforced invariant that every tenant must always have at least one TenantAdmin. Membership admin endpoints now return 409 Conflict when a role change or deletion would leave zero TenantAdmins (Owner/Admin). Owner-only demotion restrictions removed; invariant governs behavior. Tests updated to confirm last-admin protection and allow demotion when another admin exists.

- IAM — Sprint 1.1: Membership Roles flags (Completed)
  - Added hardcoded tenant-scoped Roles as a [Flags] enum: TenantAdmin, Approver, Creator, Learner (None=0).
  - Extended `Membership` with an `roles` column (int) to store role flags per user per tenant.
  - EF Core migration: `20250915130937_s4_01_membership_roles_flags` adds `roles integer not null default 0` to `app.memberships`.
  - Existing `MembershipRole` enum (Owner/Admin/Editor/Viewer) remains for legacy compatibility; new authorization will use `Roles` going forward.
  - No behavior change yet; enforcement and APIs will land in subsequent stories.

- Mig‑07: Transport privacy hardening — Redis subscriber no longer logs raw Pub/Sub payloads on warning/error paths; logs include channel and payload length only. Publisher and subscriber continue to send/accept GUID IDs only; no PII is present in transport payloads.
- Mig‑03b: External notifications worker — introduced `apps/notifications-worker` which hosts the notifications runtime out-of-process. Added `NotificationsRuntimeOptions` to gate hosted services (dispatchers/purge/auto‑resend) so the API can disable dispatch when the worker runs. Default behavior remains unchanged.
- Mig‑05: DLQ and replay tooling — added admin endpoints to list DLQ (`GET /api/notifications/dlq`) and bulk replay (`POST /api/notifications/dlq/replay`) with tenant scoping and summaries.
- Mig‑06: Web DLQ admin — Studio page adds pagination, status/kind filters, and per‑row replay; proxy tests cover guard/forwarding and X‑Total‑Count propagation.
- Mig‑08: Rollout plan & fallback docs — RUNBOOK now includes a staged rollout for Redis transport + external worker with instant rollback to Channel transport. Default remains Channel.
- Notif‑29: Bulk resend endpoint `/api/notifications/resend-bulk` with per-request and per-tenant daily caps, tenant scoping, and per-recipient throttling. Also enabled JSON string↔︎enum serialization globally so request bodies may use enum names.
- Notif‑30: Resend telemetry and policy surfacing — metrics `email.resend.*` and bulk header `X‑Resend‑Remaining` to expose remaining per-tenant daily cap.
- Notif‑31: Resend history endpoint `GET /api/notifications/{id}/resends` with paging (`take`/`skip`), `X‑Total‑Count` header, and tenant/superadmin scoping.
- Notif‑32: Automated resend service — background scanner detects "no‑action" originals (Sent and older than a window) and creates linked resends under caps/throttle. Feature‑gated via `Notifications:EnableAutoResend`.
- Auth‑12: API integration tests expanded for core auth flows and security contracts. Added Members list tests (Admin/Owner allowed; Viewer 403; Unauth 401/403) and confirmed signup/login/invites coverage. Full suite passing (119/119).
- Auth‑13: Web tests for Sign‑up, Invite acceptance, Two‑stage tenant selection, and Header tenant switcher added. Web suite passing (18/18 files; 45 tests). Coverage ~92% lines.
- Auth‑14: Documentation updates — README gains an Authentication (dev) section; RUNBOOK adds an "Authentication flows (operations)" run guide. See README “Authentication (dev)” and RUNBOOK “Authentication flows (operations)”.
- Auth‑ML: Magic Link (passwordless) — added endpoints `POST /api/auth/magic/request` and `POST /api/auth/magic/consume`; new DB table `app.login_tokens` (SHA‑256 token hashes, single‑use, 15‑minute TTL); Magic Link email templates and `NotificationEnqueuer.QueueMagicLinkAsync`; web proxy routes `/api-proxy/auth/magic/{request|consume}` and public pages `/magic/request` and `/magic/verify`. Dev email continues to use Mailhog (SMTP).
- Web: Server-side absolute URL helper (`apps/web/app/lib/serverFetch.ts`) now uses `x-forwarded-host`/`x-forwarded-proto` (or `NEXT_PUBLIC_WEB_BASE`) to build absolute URLs for internal `/api-proxy/*` calls. Server pages were refactored to use `fetchFromProxy(...)`, fixing “Failed to parse URL from /api-proxy/…” errors in server components.
- Auth‑15: Signup and tenant‑selection hardening — added same‑origin proxy `POST /api-proxy/auth/signup` to avoid browser CORS; introduced `GET /api/tenant/select?tenant=...&next=...` to set the `selected_tenant` cookie and then redirect (with safe, same‑origin `next` validation and default `/studio/agents`); `/select-tenant` now supports `?next=` deep‑links and auto‑selects when a single membership exists; `/studio` now redirects to `/studio/agents`; new public `/health` and protected `/dev/health` pages aid diagnosis; API signup ensures unique personal tenant slug generation.
- IAM — Hotfix: Avoid nested transactions in membership endpoints
  - Membership endpoints (`PUT /api/tenants/{tenantId}/members/{userId}`, `DELETE /api/tenants/{tenantId}/members/{userId}`, and `POST /api/tenants/{tenantId}/memberships/{userId}/roles`) now detect and reuse an ambient transaction opened by `TenantScopeMiddleware` instead of always starting a new one. This prevents runtime 500s observed during roles updates when the middleware already began a tenant-scoped transaction. Behavior under EF InMemory remains unchanged.
- Pre‑Mig‑01: Introduced `INotificationTransport` with default `ChannelNotificationTransport` bridging to the existing in‑process ID queue. `NotificationEnqueuer` now publishes via the transport seam to prepare for external brokers without changing behavior.
- Pre‑Mig‑02: Outbox publisher integration — admin/dev retry/resend (incl. bulk) and the auto‑resend scanner now publish via `INotificationTransport`. The default channel transport still bridges to the in‑process `INotificationIdQueue`, so runtime behavior is unchanged. Full API suite remains green (108/108).
- Mig‑03: Redis transport option for notifications — added `RedisNotificationTransport` (publisher) and a background `RedisNotificationSubscriberHostedService` that listens to a Redis Pub/Sub channel and forwards IDs to the in‑process dispatcher queue. Feature‑selectable via `Notifications:Transport:Mode` = `channel` (default) or `redis`; Redis settings configurable under `Notifications:Transport:Redis`.
- Dev: Notifications transport health + ping — added `GET /api/dev/notifications/health` (reports transport mode and Redis subscriber diagnostics) and `POST /api/dev/notifications/ping` (creates a synthetic queued outbox item and publishes via transport) to make e2e checks easy in Development.
- Auth‑PW: Password flows (Forgot/Reset/Change) — API endpoints added for `POST /api/auth/forgot-password` (202), `POST /api/auth/reset-password` (204), and `POST /api/auth/change-password` (204; authorized). Matching web proxy routes and minimal pages were added; Login now links to “Forgot password?” and protected layout exposes “Change password”. New unit/integration tests cover happy paths and negative cases.
- Web‑MW: Middleware consolidation — removed legacy `middleware.js` and consolidated auth/route protection in `middleware.ts`, including safe login redirects and `x-pathname` header injection for diagnostics. Added `/api/debug/session` to inspect session/cookie/headers in dev.

## Monorepo overview

- Package manager: PNPM workspaces + Turborepo
- Languages: TypeScript/Node (web/mobile/packages), C# .NET 8 (API)
- Root files: `appostolic.sln`, `package.json`, `pnpm-workspace.yaml`, `turbo.json`, `tsconfig.base.json`, `Makefile`
- Top-level folders:
  - `apps/`
    - `api/` — .NET 8 Minimal API + EF Core 8; dev header auth; agent runtime; worker/queue
    - `web/` — Next.js 14 (App Router) with server-side API proxy routes
    - `mobile/` — React Native/Expo
    - `render-worker/` — Node worker (TS)
  - `packages/` — shared packages: `sdk/`, `models/`, `ui/`, `docgen/`, `prompts/`, `video-scenes/`
  - `infra/` — Docker Compose stack, devcontainer, init SQL
  - `scripts/` — helper scripts (e.g., dev doctor)

### Full folder tree (abridged but comprehensive)

```
appostolic/
├─ appostolic.sln
├─ package.json
├─ pnpm-workspace.yaml
├─ turbo.json
├─ tsconfig.base.json
├─ Makefile
├─ README.md
├─ RUNBOOK.md
├─ SnapshotArchitecture.md
├─ apps/
│  ├─ api/
│  │  ├─ Appostolic.Api.csproj
│  │  ├─ Program.cs
│  │  ├─ App/
│  │  │  ├─ Endpoints/
│  │  │  │  ├─ V1.cs
│  │  │  │  ├─ DevToolsEndpoints.cs       # POST /api/dev/tool-call (Development only)
│  │  │  │  ├─ DevAgentsDemo.cs          # POST /api/dev/agents/demo (Development only)
│  │  │  │  ├─ DevAgentsEndpoints.cs     # GET /api/dev/agents (Development only)
  │  │  │  │  ├─ DevNotificationsEndpoints.cs # GET/POST /api/dev/notifications (Development only)
  │  │  │  │  ├─ NotificationsAdminEndpoints.cs # /api/notifications (prod: list/details/retry; tenant-scoped + superadmin)
│  │  │  │  ├─ AgentsEndpoints.cs        # /api/agents CRUD + /api/agents/tools
  │  │  │  │  └─ AgentTasksEndpoints.cs    # /api/agent-tasks (create/get/list; X-Total-Count; filters: status/agentId/from/to/q)
│  │  │  └─ Infrastructure/
│  │  │     ├─ Auth/
│  │  │     │  └─ DevHeaderAuthHandler.cs
│  │  │     └─ MultiTenancy/
│  │  │        └─ TenantScopeMiddleware.cs
│  │  ├─ Application/
│  │  │  ├─ Agents/
│  │  │  │  ├─ AgentRegistry.cs
│  │  │  │  ├─ Runtime/                   # Orchestrator + TraceWriter
│  │  │  │  │  ├─ AgentOrchestrator.cs
│  │  │  │  │  └─ TraceWriter.cs
│  │  │  │  ├─ Queue/                     # In-memory queue + worker
│  │  │  │  │  ├─ IAgentTaskQueue.cs
│  │  │  │  │  ├─ InMemoryAgentTaskQueue.cs
│  │  │  │  │  └─ AgentTaskWorker.cs
│  │  │  │  ├─ Tools/                     # Deterministic dev tools
│  │  │  │  │  ├─ WebSearchTool.cs
│  │  │  │  │  ├─ DbQueryTool.cs
│  │  │  │  │  └─ FsWriteTool.cs
│  │  │  │  └─ Model/                     # Mock model adapter
│  │  │  │     └─ MockModelAdapter.cs
│  │  │  └─ Validation/
│  │  │     └─ Guard.cs
│  │  ├─ Domain/
│  │  │  └─ Agents/
│  │  │     ├─ Agent.cs
│  │  │     ├─ AgentTask.cs
│  │  │     ├─ AgentTrace.cs
│  │  │     ├─ AgentStatus.cs
│  │  │     └─ TraceKind.cs
│  │  ├─ Infrastructure/
│  │  │  ├─ AppDbContext.cs
│  │  │  └─ Configurations/
│  │  │     └─ *.cs
│  │  ├─ Migrations/
│  │  │  └─ *.cs
│  │  ├─ tools/
│  │  │  └─ seed/                        # Idempotent seed for dev user/tenants
│  │  └─ Properties/launchSettings.json
│  ├─ web/
│  │  ├─ next.config.mjs
│  │  ├─ package.json
│  │  ├─ src/
│  │  │  └─ lib/serverEnv.ts              # API_BASE/DEV_USER/DEV_TENANT validation
│  │  └─ app/
│  │     ├─ layout.tsx
│  │     ├─ page.tsx
│  │     ├─ login/page.tsx
│  │     ├─ signup/page.tsx
│  │     ├─ forgot-password/page.tsx
│  │     ├─ reset-password/page.tsx
│  │     ├─ change-password/page.tsx
│  │     ├─ magic/
│  │     │  ├─ request/page.tsx
│  │     │  └─ verify/page.tsx
│  │     ├─ select-tenant/page.tsx       # Tenant selection with optional ?next=
│  │     ├─ studio/page.tsx              # Redirects to /studio/agents
│  │     ├─ health/page.tsx              # Public health page
│  │     ├─ dev/health/page.tsx          # Protected health page
│  │     ├─ dev/page.tsx
│  │     ├─ dev/agents/page.tsx
│  │     ├─ dev/agents/components/AgentRunForm.tsx
│  │     ├─ dev/agents/components/TracesTable.tsx
│  │     ├─ studio/agents/                # Agent Studio (CRUD UI)
│  │     │  ├─ page.tsx                   # List (defaults to enabled-only)
│  │     │  ├─ new/page.tsx               # Create
│  │     │  ├─ [id]/page.tsx              # Edit
│  │     │  └─ components/
│  │     │     ├─ AgentForm.tsx           # Create/Edit form (includes Enabled toggle)
│  │     │     └─ AgentsTable.tsx         # List table
│  │     └─ api-proxy/
│  │        ├─ auth/forgot-password/route.ts # POST /api-proxy/auth/forgot-password → API /api/auth/forgot-password (anonymous)
│  │        ├─ auth/reset-password/route.ts  # POST /api-proxy/auth/reset-password → API /api/auth/reset-password (anonymous)
│  │        ├─ auth/change-password/route.ts # POST /api-proxy/auth/change-password → API /api/auth/change-password (authorized)
│  │        ├─ auth/signup/route.ts      # POST /api-proxy/auth/signup → API /api/auth/signup (anonymous proxy)
│  │        ├─ auth/magic/request/route.ts   # POST /api-proxy/auth/magic/request → API /api/auth/magic/request
│  │        └─ auth/magic/consume/route.ts   # POST /api-proxy/auth/magic/consume → API /api/auth/magic/consume
│  │        ├─ dev/agents/route.ts        # GET /api-proxy/dev/agents → API /api/dev/agents
│  │        ├─ agents/route.ts            # GET/POST /api-proxy/agents → API /api/agents
│  │        ├─ agents/[id]/route.ts       # GET/PUT/DELETE /api-proxy/agents/{id}
│  │        └─ agents/tools/route.ts      # GET /api-proxy/agents/tools → API /api/agents/tools
│  │        └─ agent-tasks/
│  │           ├─ route.ts                 # GET/POST /api-proxy/agent-tasks → API
│  │           └─ [id]/route.ts            # GET /api-proxy/agent-tasks/{id}
│  │     └─ api/
│  │        └─ tenant/select/route.ts    # GET/POST /api/tenant/select — set cookie; GET redirects with validated next
│  │        └─ debug/session/route.ts    # GET /api/debug/session — diagnostic: session, cookie, and proxy headers
│  ├─ mobile/
│  │  ├─ app.json
│  │  ├─ package.json
│  │  └─ src/App.tsx
│  └─ render-worker/
│     └─ src/index.ts
├─ packages/
│  ├─ sdk/
│  │  ├─ package.json
│  │  ├─ tsconfig.json
│  │  └─ src/index.ts
│  ├─ models/
│  │  └─ src/*.ts
│  ├─ prompts/
│  ├─ ui/
│  └─ video-scenes/
├─ infra/
│  ├─ devcontainer/
│  │  └─ devcontainer.json
│  └─ docker/
│     ├─ compose.yml
│     ├─ docker-compose.yml
│     ├─ Dockerfile.api
│     ├─ Dockerfile.web
│     ├─ .env
│     ├─ initdb/
│     │  └─ init.sql
│     └─ data/
│        ├─ postgres/
│        ├─ minio/
│        └─ qdrant/
└─ scripts/
  └─ dev-doctor.sh
```

## Tech stack (high level)

- Backend: .NET 8, Minimal API, EF Core 8 (Npgsql provider, PostgreSQL)
- Frontend: Next.js 14 (app router), TypeScript, React 18, MUI Premium
- Mobile: Expo/React Native (TypeScript)
- Infra: Docker Compose for Postgres, Redis, MinIO, Qdrant, pgAdmin, Mailhog (dev)
- Docs/SDK: Swashbuckle for OpenAPI; custom TypeScript SDK package

---

## Runtime architecture

### API service (`apps/api`)

- Entrypoint: `apps/api/Program.cs`
- Middleware: Swagger, Authentication/Authorization, TenantScopeMiddleware, legacy sample for `X-Tenant-Id`
- Auto-migration (Dev/Test): `Database.Migrate()` at startup for relational providers
- OpenTelemetry: traces, metrics, logs with optional OTLP exporter; console exporters in Development

### Web app (`apps/web`)

- Next.js 14 (App Router); server-only proxy routes under `app/api-proxy/*` inject dev headers and avoid CORS
- Env validation: `src/lib/serverEnv.ts` ensures `NEXT_PUBLIC_API_BASE`, `DEV_USER`, `DEV_TENANT`
- Client hardening: dev-only env checks for `DEV_*` are scoped to the server to avoid client runtime crashes when `WEB_AUTH_ENABLED=false`.
- Dev pages: `/dev/agents`, Agent Studio under `/studio/agents`
- Middleware: consolidated into a single `middleware.ts` that protects routes (avoids login loops) and injects an `x-pathname` header for diagnostics.
- Server fetch helper: `app/lib/serverFetch.ts` exports `fetchFromProxy()` which:
  - Builds an absolute base URL from request headers (`x-forwarded-host`/`x-forwarded-proto`) or `NEXT_PUBLIC_WEB_BASE` when provided
  - Forwards cookies so NextAuth session reaches the proxy
  - Disables cache by default (`no-store`) with `next: { revalidate: 0 }`
    Use this helper in server components and server actions to call internal `/api-proxy/*` routes. Avoid `fetch('/api-proxy/...')` directly in server code to prevent URL parse errors.
- Signup proxy: `POST /api-proxy/auth/signup` forwards to API `/api/auth/signup` with same-origin semantics to avoid browser CORS on `/signup`.
- Password flows (web): minimal pages `/forgot-password`, `/reset-password`, and `/change-password` call the corresponding same-origin proxies under `/api-proxy/auth/*`. The login page links to Forgot Password, and the protected layout includes a Change Password link beside the TenantSwitcher.
- Tenant selection route: `GET /api/tenant/select?tenant={slug}&next={path}` sets the `selected_tenant` cookie and redirects to `next` (must be a same-origin path beginning with `/`); defaults to `/studio/agents` when `next` is missing/invalid. `POST /api/tenant/select` sets the cookie and returns JSON for programmatic updates.
- Select-tenant deep-linking: `/select-tenant` accepts `?next=...` and validates it server-side. If the user has exactly one membership, it auto-selects and redirects via the GET route above. Otherwise, the form includes a hidden `next` and redirects via GET after selection.
- Health pages: `/health` (public) and `/dev/health` (protected) help verify session/tenant state and middleware behavior.
- Session diagnostics: `GET /api/debug/session` returns session summary, the `selected_tenant` cookie value, and computed proxy headers.
- Studio landing: `/studio` redirects to `/studio/agents` to avoid 404s and improve deep-link resilience.

### Mobile (`apps/mobile`) and Render Worker (`apps/render-worker`)

- Mobile: Expo/React Native (TypeScript), minimal scaffold
- Render Worker: Node/TypeScript worker for rendering tasks (placeholder)

### Notifications worker (`apps/notifications-worker`)

- Entrypoint: `apps/notifications-worker/Program.cs`
- Purpose: Run the notifications runtime (outbox dispatcher, purge, auto‑resend, optional Redis subscriber) in a separate process from the API.
- Composition: Reuses the API’s notifications DI via `AddNotificationsRuntime(...)` and the same `AppDbContext` (PostgreSQL). Auto‑migrates in Development/Test when relational.
- Runtime gating: `NotificationsRuntimeOptions` controls hosted services. Recommended ops setting when the worker is running: set API `Notifications:Runtime:RunDispatcher=false` so only the worker processes the outbox; the worker may keep `RunDispatcher=true`.
- Transport: Shares the same `INotificationTransport` selection — `channel` (default) or `redis` when `Notifications:Transport:Mode=redis`.
- Telemetry: OpenTelemetry traces/metrics with optional OTLP exporter; console exporters in Development.

---

## Cross-cutting concerns

### Authentication & Authorization

- Dev headers (API): `x-dev-user` and `x-tenant`; emits claims `sub`, `email`, `tenant_id`, `tenant_slug`. All `/api/*` require authorization (dev headers expected). Swagger remains public.
  - Superadmin (dev/test friendly): `DevHeaderAuthHandler` can emit a `superadmin` claim when header `x-superadmin: true` is present or the user's email is included in config allowlist `Auth:SuperAdminEmails`. Used to enable cross-tenant notification views in admin endpoints.
- Web tenant selection/switcher:
  - Two‑stage login with `/select-tenant`; auto‑select when single membership.
  - Header `TenantSwitcher` updates session via `session.update({ tenant })` and sets `selected_tenant` cookie via `/api/tenant/select`.
  - Server proxies forward `x-tenant` based on session or cookie; when web auth is enabled, protected routes require a selected tenant (401), except invite acceptance route.
  - Cookie vs session: `selected_tenant` is a routing hint for the web layer; authorization uses server-side session/JWT and API claims. The cookie is httpOnly, SameSite=Lax, and secure in production.
- Role-based guards (Auth‑11): server-only helpers (`roleGuard.ts`) enforce Owner/Admin on sensitive proxy routes for defense-in-depth.
- Security contract: a dev-mode integration test verifies unauthenticated `/api/*` calls return 401/403; the same requests succeed with dev headers.

- Magic Link (passwordless) — Auth‑ML
  - API endpoints:
    - `POST /api/auth/magic/request { email }` → always `202 Accepted`; creates a login token row with `token_hash` (SHA‑256) and TTL=15m, and enqueues a Magic Link email with an absolute link to `/magic/verify?token=…`. Includes basic per‑email rate limiting.
    - `POST /api/auth/magic/consume { token }` → validates via hash+TTL, enforces single‑use (`consumed_at`), and returns minimal user payload. If the user doesn’t exist, it creates the user and a personal tenant (`{localpart}-personal`, de‑duped with `-2`, `-3`, …) and an Owner membership.
  - Persistence: `app.login_tokens` with indexes (unique on `token_hash`; `(email, created_at DESC)`; partial on `consumed_at IS NULL`). Raw tokens are never stored.
  - Email: `EmailKind.MagicLink` templates (Scriban) render subject/text/html; NotificationEnqueuer pre‑renders snapshots and stores only the token hash; logs avoid raw token.
  - Web integration: public pages `/magic/request` and `/magic/verify`; same‑origin proxies `/api-proxy/auth/magic/request` and `/api-proxy/auth/magic/consume` avoid CORS. The verify page bridges into the session via NextAuth Credentials (dual‑mode) and redirects to `/select-tenant` (honors optional `?next=`).

- Passwords — Auth‑PW
  - API endpoints:
    - `POST /api/auth/forgot-password { email }` → `202 Accepted` always; enqueues a password reset email with an absolute link to `/reset-password?token=…`. Includes basic per‑email rate limiting.
    - `POST /api/auth/reset-password { token, newPassword }` → `204 No Content` on success; validates token by hash+TTL and enforces single‑use.
    - `POST /api/auth/change-password { currentPassword, newPassword }` → `204 No Content` when authorized and current password matches.
  - Email: Password reset uses Scriban templates; raw tokens are never persisted. Token hashes follow the same PII hardening pattern as verification/invite (Notif‑21) and are only stored as hashes.
  - Web integration: minimal pages `/forgot-password`, `/reset-password`, and `/change-password` post to same-origin proxies under `/api-proxy/auth/*`. UI affordances: “Forgot password?” link on the Login page and a “Change password” link in the protected header near the TenantSwitcher. Unit tests cover happy paths and negative cases (invalid token, wrong current).

### Multi-tenancy & RLS

- `TenantScopeMiddleware` skips `/health*` and `/swagger*`; when authenticated and `tenant_id` exists, begins a DB transaction and sets `app.tenant_id` GUC for RLS.
- Legacy demo header `X-Tenant-Id` in `Program.cs` for sample `/lessons` endpoints.

---

## Domain capabilities

### Agents (runtime v1)

- Domain types: Agent, AgentTask, AgentTrace; enums AgentStatus, TraceKind
- Validation: `Guard.cs` enforces invariants (NotNull, MaxLength, InRange)
- Orchestration: `AgentOrchestrator` with deterministic `MockModelAdapter` in dev; allowlist enforcement; trace step numbering strategy
- TraceWriter: persists traces; clamps non‑negatives; retries once on unique conflicts
- Tools: `web.search`, `db.query`, `fs.write` via `ToolRegistry`
- Queue/Worker: `InMemoryAgentTaskQueue` (Channel<Guid>, SingleReader=true); `AgentTaskWorker` consumes and processes with idempotence and graceful shutdown semantics
- Agent store resolution is DB‑first with fallback to static `AgentRegistry`

Endpoints:

- `GET /api/agents` (+ paging, includeDisabled)
- `GET /api/agents/{id}` | `POST /api/agents` | `PUT /api/agents/{id}` | `DELETE /api/agents/{id}`
- `GET /api/agents/tools`
- Agent tasks: `POST /api/agent-tasks`, `GET /api/agent-tasks/{id}?includeTraces=true`, `GET /api/agent-tasks` (filters, paging; `X-Total-Count`)

### Notifications (email)

- Metrics: `email.sent.total` and `email.failed.total` counters (tagged by email kind) exposed via OTEL Meter "Appostolic.Metrics".
- Notif-30: Resend telemetry — `email.resend.total` (tags: kind, mode=manual|bulk, tenant_scope=self|superadmin|dev, outcome),
  `email.resend.throttled.total` (same tags), and histogram `email.resend.batch.size` (tags: tenant_scope, and kind when filtered).
- Bulk header: `X-Resend-Remaining` on `/api/notifications/resend-bulk` reflects remaining per-tenant daily cap when tenant context is known.

- Resend history (Notif‑31):
  - `GET /api/notifications/{id}/resends` lists child resend notifications linked to the original.
  - Paging via `take`/`skip`; sets `X‑Total‑Count` header; ordered by `CreatedAt DESC`.
  - Tenant scoping enforced: non‑superadmin limited to current tenant; superadmin may view across tenants.

- Automated resend (Notif‑32):
  - Background scanner (`AutoResendHostedService` + `AutoResendScanner`) runs on an interval to detect "no‑action" notifications and enqueue resends with reason `auto_no_action`.
  - Eligibility: originals only (no `ResendOfNotificationId`), `Status=Sent`, older than `AutoResendNoActionWindow`, no existing resend child, and not explicitly delivered/opened by provider webhook.
  - Guardrails: respects `ResendThrottleWindow`, per‑scan cap `AutoResendMaxPerScan`, and per‑tenant daily cap `AutoResendPerTenantDailyCap`.
  - Observability: reuse `email.resend.total` metrics with `mode=auto` and outcomes `created|throttled|forbidden|error`.
  - Configuration (NotificationOptions): `EnableAutoResend` (default false), `AutoResendScanInterval` (5m), `AutoResendNoActionWindow` (24h), `AutoResendMaxPerScan` (50), `AutoResendPerTenantDailyCap` (200).

- Transport: `INotificationTransport` abstracts the "notification queued" signal. Default `ChannelNotificationTransport` bridges to the existing in‑process `INotificationIdQueue` to preserve behavior and enable future broker integration.
  Transport: `INotificationTransport` abstracts the "notification queued" signal. Default `ChannelNotificationTransport` bridges to the existing in‑process `INotificationIdQueue` to preserve behavior and enable future broker integration. Optionally, set `Notifications:Transport:Mode=redis` to publish via Redis (`RedisNotificationTransport`) and enable the subscriber hosted service that forwards Pub/Sub messages to the dispatcher. The transport is now used consistently across:
  - Enqueue helpers (`NotificationEnqueuer`)
  - Admin/dev retry/resend endpoints (including bulk resend)
  - Automated resend scanner
    This ensures a single seam to swap in an external broker later without touching endpoint logic.

Redis transport configuration

- Options (NotificationTransportOptions):
  - `Notifications:Transport:Mode` — `channel` (default) or `redis`.
  - `Notifications:Transport:Redis:ConnectionString` — optional; if provided, used verbatim.
  - `Notifications:Transport:Redis:Host` (default `127.0.0.1`), `Port` (default `6380`), `Password` (optional), `Ssl` (bool, default false), `Channel` (default `app:notifications:queued`).
- Behavior: Publisher posts the outbox id as a GUID string to the channel; the subscriber pushes it to `INotificationIdQueue`, preserving the existing dispatcher path. When Mode=`channel`, no Redis client is created and behavior remains in‑process only.
- Queue: `IEmailQueue` + `EmailQueue` (in‑memory channel used by background dispatcher)
- Dispatcher (v1): `EmailDispatcherHostedService` renders and sends with retry/backoff; metrics/logging via OTEL
- Template renderer: `ScribanTemplateRenderer`
- Providers: `SmtpEmailSender` (dev), `SendGridEmailSender` (prod/real), `NoopEmailSender` fallback
- Enqueuer: `NotificationEnqueuer` helpers for verification and invite emails
- Resend (manual/bulk):
  - Manual: `POST /api/notifications/{id}/resend` (also dev variant) clones and enqueues a linked child, enforcing throttle via `(to_email, kind)` within `ResendThrottleWindow`.
  - Bulk: `POST /api/notifications/resend-bulk` filters by kind/date/recipients and enforces:
    - Per-request cap `Notifications:BulkResendMaxPerRequest` (default 100)
    - Per-tenant daily cap `Notifications:BulkResendPerTenantDailyCap` (default 500, rolling 24h)
    - Tenant scoping: non‑superadmin limited to current tenant; superadmin may filter by `tenantId`.
    - Throttle: per‑recipient pre‑check and outbox enforcement to avoid violating `ResendThrottleWindow`.
  - JSON: API accepts enum names in request bodies (global `JsonStringEnumConverter`), e.g., `{ "kind": "Verification" }`.
- PII hardening (Notif‑21):
  - Token hashing: verification/invite tokens are hashed (SHA‑256) and only the hash is stored on the outbox row (`TokenHash`); raw tokens are never persisted in Notification fields or `data_json`.
  - Pre‑rendered snapshots: subject/html/text may be pre‑rendered at enqueue time and stored; dispatcher reuses snapshots when present to avoid re‑render divergence.
  - Redacted logging: emails in logs are redacted (e.g., k\*\*\*@example.com) across SMTP/SendGrid providers and dispatcher paths.

PII scrubbing (Notif‑23):

- Early scrub of sensitive fields prior to deletion to minimize PII exposure time. A dedicated scrub pass nulls selected columns for notifications older than a scrub window but newer than the delete retention cutoff.
- Configuration (NotificationOptions):
  - Master switch `PiiScrubEnabled` (default true).
  - Scrub windows: `ScrubSentAfter`, `ScrubFailedAfter`, `ScrubDeadLetterAfter`.
  - Per‑field toggles: `ScrubToName`, `ScrubSubject`, `ScrubBodyHtml`, `ScrubBodyText`, and `ScrubToEmail` (email off by default).
- Observability: `NotificationsPurgeHostedService` logs `scrubbed` counts alongside purged counts each run.

Further reading:

- Privacy policy (engineering draft): devInfo/Sendgrid/privacyPolicy.md
- Vendor compliance and subprocessors: devInfo/Sendgrid/vendorCompliance.md

Outbox & Dispatcher (Notif‑13/14/15):

- Table `app.notifications` stores durable outbox entries (kind, to_email, data_json, dedupe_key, status, attempts, errors, timestamps; snapshots subject/html/text)
- Dispatcher `NotificationDispatcherHostedService` leases (`Queued`→`Sending`), renders, sends, and updates status with jittered backoff (0.5s/2s/8s +/-20%) and terminal `DeadLetter` on exhaustion; event‑driven via ID queue with polling fallback
- Testing note: EF InMemory provider does not support transactions; leasing logic gates transactional semantics behind `Database.IsRelational()` to keep tests stable while retaining transactions for relational providers.

Dedupe & Retention (Notif‑17/18):

- TTL dedupe table `app.notification_dedupes` (PK: dedupe_key, expires_at) is claimed before outbox insert; duplicate claims within TTL throw `DuplicateNotificationException`
- Partial unique index `ux_notifications_dedupe_key_active` applies only to in‑flight statuses (`Queued`,`Sending`); `Sent` dedupe is governed by the TTL table
- Hourly purge job removes expired dedupe claims and old notifications; retention windows configurable via `Notifications` options (e.g., Sent: 60d; Failed/Dead: 90d)
- Scrub‑then‑delete ordering (Notif‑23): For items within the scrub window but not yet at the deletion cutoff, the job nulls configured fields first; items past the deletion cutoff are removed entirely.

Dev endpoints:

- `POST /api/dev/notifications/verification` and `/invite` enqueue test emails; requires dev headers

Prod admin endpoints (Notif‑24):

- `GET /api/notifications` — list notifications
  - Non‑superadmin: tenant‑scoped using `tenant_id` claim; supports filters `status`, `kind`; paging via `take`/`skip`; `X-Total-Count` header.
  - Superadmin: cross‑tenant view allowed and may optionally filter by `tenantId`.
- `GET /api/notifications/{id}` — details
  - Non‑superadmin: 403 if the notification’s `tenant_id` doesn’t match current tenant.
  - Superadmin: allowed.
- `POST /api/notifications/{id}/retry` — retry Failed/DeadLetter
  - Transitions to `Queued` and nudges dispatcher; enforces same tenant/superadmin gating.

- `GET /api/notifications/{id}/resends` (Notif‑31)
  - Returns child resends for an original; ordered latest-first.
  - Paging via `take`/`skip`; sets `X‑Total‑Count`.
  - Tenant scoping as above; superadmin cross‑tenant allowed.

Access control:

- Non‑superadmin requests are auto‑scoped by current tenant (from `tenant_id` claim); cross‑tenant access is denied.
- Superadmin requests (claim `superadmin=true`) may access across tenants and use `tenantId` filter on list.

Provider webhooks:

- `POST /api/notifications/webhook/sendgrid` — receives SendGrid event webhooks; optional shared-secret via header. Normalizes and stores provider delivery status under `notifications.data_json.provider_status` along with event timestamp; designed to be idempotent for replayed events.

#### Field encryption (Notif-22)

#### DLQ and replay (Mig‑05)

- Endpoints (tenant-scoped with superadmin override):
  - `GET /api/notifications/dlq?status=Failed|DeadLetter&kind=...&tenantId=...&take=&skip=` — lists Failed and DeadLetter notifications (defaults to both) with paging and `X-Total-Count`.
  - `POST /api/notifications/dlq/replay` — body `{ ids?: Guid[], status?: Failed|DeadLetter, kind?: EmailKind, tenantId?: Guid, limit?: number }`; requeues selected items and publishes them via the active transport. Responds with `{ requeued, skippedForbidden, notFound, skippedInvalid, errors, ids }`.
- Behavior: enforces tenant scoping on both query and item level; only Failed/DeadLetter are eligible for replay. Uses `INotificationOutbox.TryRequeueAsync` and `INotificationTransport.PublishQueuedAsync` for idempotent handoff back to the dispatcher.

- Optional at-rest encryption for selected outbox fields: `to_name`, `subject` (optional), `body_html`, `body_text`.
- Format: `enc:v1:` prefix followed by Base64URL payload containing AES-GCM ciphertext + nonce + tag.
- Configuration (NotificationOptions):
  - `EncryptFields` (bool) — master switch; default false
  - `EncryptionKeyBase64` (string) — 256-bit key as base64; required when enabled
  - Per-field toggles: `EncryptToName`, `EncryptSubject`, `EncryptBodyHtml`, `EncryptBodyText`
- Runtime behavior:
  - Encrypt on write (`CreateQueuedAsync` and `MarkSentAsync`), decrypt on lease (`LeaseNextDueAsync`).
  - DI selects `AesGcmFieldCipher` when enabled+key is valid; otherwise `NullFieldCipher` (no-op) for backward compatibility.
- No schema migration required; ciphertext is stored in existing text columns. Downstream services receive plaintext via the lease path.

---

## API surface

### Grouped v1 endpoints (`apps/api/App/Endpoints/V1.cs`)

- `GET /api/me` — user/tenant claims
- `GET /api/tenants` — current tenant summary
- `GET /api/lessons?take=&skip=` — paginated list; `POST /api/lessons` — create (uses current tenant)
- `POST /api/auth/magic/request` — request a Magic Link (202; anonymous)
- `POST /api/auth/magic/consume` — consume a Magic Link token (anonymous)
- `POST /api/auth/forgot-password` — start password reset (202; anonymous)
- `POST /api/auth/reset-password` — perform password reset (204; anonymous)
- `POST /api/auth/change-password` — change password (204; authorized)

### Agents endpoints

- `GET /api/agents?take=&skip=&includeDisabled=` — list (enabled‑only by default)
- `GET /api/agents/{id}` | `POST /api/agents` | `PUT /api/agents/{id}` | `DELETE /api/agents/{id}`
- `GET /api/agents/tools` — tool catalog for allowlists

### Agent tasks endpoints

- `POST /api/agent-tasks` — create, enqueue, returns `201 Created` with summary
- `GET /api/agent-tasks/{id}` — details; `?includeTraces=true` includes traces
- `GET /api/agent-tasks` — list with filters (status/agentId/from/to/q) and paging; sets `X-Total-Count`

### Dev-only endpoints

- `POST /api/dev/tool-call` — deterministic tool tests
- `GET /api/dev/agents` — seeded agents for UI dropdowns
- `POST /api/dev/agents/demo` — inline agent run with traces
- `POST /api/dev/notifications/verification` and `/invite` — enqueue test emails
- `GET /api/dev/notifications/health` — transport diagnostics (mode, Redis subscriber status)
- `POST /api/dev/notifications/ping` — publishes a synthetic queued outbox item through the active transport

### Swagger/OpenAPI

- Swagger JSON: `GET /swagger/v1/swagger.json` | UI: `GET /swagger/`
- Security scheme: API key (DevHeaders) for dev headers; UI remains public

---

## Data & persistence (EF Core)

- Provider: Npgsql (PostgreSQL 16 dev); default schema `app`
- Migrations under `apps/api/Migrations/`; auto‑migrate enabled in Dev/Test
- RLS strategy: set `app.tenant_id` via middleware; DB‑side policies/init in `infra/initdb` and EF migrations

Key domain tables and constraints:

- Agent runtime: `agents`, `agent_tasks`, `agent_traces` with unique index `(task_id, step_number)` and checks on step/duration
- Notifications: `notifications` (outbox) and `notification_dedupes` (TTL claims); dispatcher indexes on `(status, next_attempt_at)` and `(tenant_id, created_at DESC)`; `citext` for emails, `jsonb` for data. Resend (Notif‑27) adds metadata columns and indexes for safe resend policies: `resend_of_notification_id` (FK self‑reference, NO ACTION), `resend_reason`, `resend_count` (default 0), `last_resend_at`, `throttle_until`; indexes on `(resend_of_notification_id)` and `(to_email, kind, created_at DESC)`.
- Invitations: `invitations` with FKs to `tenants` and `users`; functional unique `(tenant_id, lower(email))` and index on `(tenant_id, expires_at)`
- Login tokens (Auth‑ML): `login_tokens` storing email (citext), token_hash (SHA‑256), purpose ('magic'), expires_at, consumed_at, created_at, optional created_ip/ua/tenant_hint; indexes: unique(token_hash), (email, created_at DESC), and partial on consumed_at IS NULL.
- Auth/tenant integrity: unique slug `tenants(name)`; FKs cascade/set‑null as appropriate

---

## Observability

- OpenTelemetry resource: `Appostolic.Api`
- Traces: ASP.NET Core, HTTP client, custom sources (`Appostolic.AgentRuntime`, `Appostolic.Tools`)
- Metrics: ASP.NET Core, HTTP client, runtime, `Appostolic.Metrics` (e.g., email sent/failed)
- Logs: structured logs; console exporters in Development; optional OTLP endpoint via `OTEL_EXPORTER_OTLP_ENDPOINT`

- Privacy (Notif‑25): Recipient emails are redacted across dispatcher/providers and log scopes; metrics include only non‑PII tags (e.g., kind). See devInfo/Sendgrid/privacyPolicy.md for practices and devInfo/Sendgrid/vendorCompliance.md for provider details.

---

## Infra & local development

- Docker Compose: Postgres, Redis, MinIO, Qdrant, Mailhog, pgAdmin
- Init SQL: `infra/initdb/init.sql` sets extensions, schemas, GUC helpers
- Devcontainer configuration available

Makefile highlights:

- `make migrate` — apply EF migrations
- `make sdk` — regenerate OpenAPI/SDK
- `make down` — stop local Docker stack
- `make up` — start infra Docker stack
- `make bootstrap` — nuke volumes, bring up infra, wait for Postgres, migrate, and seed
- `make api` — run API with dotnet watch (http://localhost:5198)
- `make web` — run Next.js dev server
- `make mobile` — run Expo dev server (port 8082)
- `make doctor` — run dev-doctor script

Important URLs:

- API base: http://localhost:5198
- Swagger UI: http://localhost:5198/swagger/
- Swagger JSON: http://localhost:5198/swagger/v1/swagger.json
- Postgres: localhost:55432 (container `postgres`)
- Redis: localhost:6380
- MinIO: http://localhost:9002 (API) / http://localhost:9003 (Console)
- Mailhog UI: http://localhost:8025
- Qdrant UI/API: http://localhost:6334
- pgAdmin: http://localhost:8081

Dev credentials (local only): see `infra/docker/.env` for Postgres/Redis/MinIO/Mailhog/Qdrant/pgAdmin

Dev auth for API testing: send headers `x-dev-user` and `x-tenant` (default seed: `kevin@example.com` / `kevin-personal`)

Troubleshooting:

- Swagger shows JSON: use `/swagger/` (trailing slash) or rely on `/swagger` redirect
- 401 on `/api/*`: send dev headers; run `make seed` (or `make bootstrap`) if you haven’t seeded
- Tenant scoping: verify `tenant_id` claim exists or use legacy `X-Tenant-Id` for sample `/lessons`
- Postgres connection errors: ensure Docker is up and port `55432` matches env
- Node engines: repo requires `node >=20 <21`; using Node 19 may error (URL.canParse). Use Node 20.x.

---

## SDK & OpenAPI (`packages/sdk`)

- OpenAPI generated via Swashbuckle CLI (output at `packages/sdk/openapi.json`)
- SDK currently provides minimal fetch helpers/types; can be expanded via codegen

---

## What’s new since last snapshot

- Agents CRUD: endpoints added, tool catalog surfaced; Agent enable/disable via `IsEnabled` with default true and includeDisabled query
- Agent Studio (web): list/create/edit pages; server proxies for Agents and Tools
- Agent resolution: `AgentStore` for DB‑first resolution with static fallback
- Database: migration for `is_enabled` on `app.agents`
- Notifications v1: in‑process queue + dispatcher with retry/backoff and OTEL metrics; SMTP/SendGrid/Noop providers and production guard; enqueue helpers for verification/invite with absolute links
- Outbox (Notif‑13): durable table `app.notifications` with indexes and dedupe
- Dedupe/Retention (Notif‑17/18): TTL dedupe table `app.notification_dedupes`, narrowed partial unique (Queued,Sending), and hourly purge job
- Notifications (Notif‑24): production admin endpoints under `/api/notifications` with tenant‑scoped access for Owner/Admin and optional cross‑tenant superadmin view; superadmin claim available via dev header or allowlist for testing and operations.
- Notifications (Notif‑27): outbox resend metadata added — self‑FK `resend_of_notification_id` (NO ACTION), `resend_reason`, `resend_count` (default 0), `last_resend_at`, `throttle_until`; supporting indexes on `(resend_of_notification_id)` and `(to_email, kind, created_at DESC)`.
- Notifications (Notif‑28): manual resend endpoints — `POST /api/notifications/{id}/resend` (prod admin, tenant‑scoped with superadmin override) and `POST /api/dev/notifications/{id}/resend` (dev). Returns `201 Created` with `Location` on success; enforces resend throttling with `429 Too Many Requests` and `Retry‑After` seconds header. Throttle window configurable via `NotificationOptions.ResendThrottleWindow` (default 5m).
- Migrations hygiene: proper EF migrations were scaffolded (Designer files + updated ModelSnapshot). An obsolete `token_aggregates` migration was converted to a no‑op to avoid conflicts.

---

## Prompt starters (copy into ChatGPT)

- “Expose read-only endpoints for dev agents (already available at `/api/dev/agents`) and consider promoting to `/api/agents` for non-dev contexts. Update Swagger and web to consume the stable path.”
- “Extend EF configurations for Agent runtime with additional indexes and constraints [describe]. Create and apply a migration and confirm defaults in PostgreSQL.”
- “Regenerate OpenAPI and update the TypeScript SDK to include new endpoints (AgentTasks, DevAgents). Update `apps/web` to call through the SDK or keep using server proxies.”
- “Implement AgentTask flows (done: create, get by id with traces, list with filters). Add delete/cancel if needed and write integration tests.”
- “Harden `TenantScopeMiddleware`: ensure GUC set/reset semantics, add diagnostics, and handle exceptions explicitly.”
- “Add integration tests for `/api/lessons` using dev auth and a test Postgres; include test harness and migrations in CI.”

### Database — Notifications Outbox (Notif-13)

- Table: `app.notifications`
  - Columns: id (uuid, pk), kind (text enum), to_email (citext), to_name (text), subject/body_html/body_text (text snapshots), data_json (jsonb), tenant_id (uuid, null), dedupe_key (varchar(200), null), status (text enum), attempt_count (int2), next_attempt_at (timestamptz, null), last_error (text), created_at/updated_at (timestamptz, defaults UTC now), sent_at (timestamptz, null)
  - Indexes:
    - (status, next_attempt_at) for dispatcher eligibility
    - (tenant_id, created_at DESC) for admin/dev listings
    - (created_at DESC)
    - Partial unique on (dedupe_key) where status IN ('Queued','Sending','Sent')
  - Extensions: `citext` enabled for case-insensitive email storage

### Notifications — Dispatcher (Notif-15)

- A background service `NotificationDispatcherHostedService` consumes IDs from an in-process `INotificationIdQueue` and also polls the DB periodically.
- It leases the next due outbox row by transitioning `Queued` → `Sending`, renders the templated content, sends via the configured provider (SMTP or SendGrid), and updates status:
  - On success: `Sent` with snapshots (`subject`, `body_html`, `body_text`) and `sent_at`.
  - On transient failure: increments `attempt_count`, records `last_error`, sets `next_attempt_at` using jittered backoff (0.5s, 2s, 8s +/-20%).
  - After max attempts: marks `DeadLetter`.
- Enqueue path (Notif-14) writes `Queued` rows and pushes the new `id` to the ID queue to wake the dispatcher quickly; polling is a safety net.

### Notifications — Dedupe & Retention (Notif-17/18)

- Dedupe TTL table: `app.notification_dedupes` with primary key `dedupe_key` and `expires_at`; used to claim dedupe keys before inserting outbox rows. Duplicate claims within TTL raise a friendly `DuplicateNotificationException`.
- Partial unique index change: `ux_notifications_dedupe_key_active` now applies to in-flight statuses only (`Queued`,`Sending`), while Sent dedupe is governed by the TTL table.
- Purge job: a hosted service runs hourly and removes expired dedupe claims and notifications older than retention windows (Sent: 60d; Failed/DeadLetter: 90d; configurable via `Notifications` options).

## Running locally (dev)

- Monorepo dev: `pnpm dev` (spawns web/api/mobile if configured)
- API only: `dotnet run --project apps/api/Appostolic.Api.csproj`
- Swagger UI: http://localhost:5198/swagger/ (trailing slash)
- Health: `GET /health`, `GET /healthz`

Seeding dev data:

- Use the Makefile to migrate and seed a known user/tenant pair for dev headers:
  - `make bootstrap` (nukes local volumes, starts infra, migrates, seeds)
  - Or run `make migrate` then `make seed`

Important URLs:

- API base: http://localhost:5198
- Swagger UI: http://localhost:5198/swagger/
- Swagger JSON: http://localhost:5198/swagger/v1/swagger.json
- Postgres: localhost:55432 (container `postgres`)
- Redis: localhost:6380
- MinIO API/Console: http://localhost:9002 / http://localhost:9003
- Mailhog UI: http://localhost:8025
- Qdrant UI/API: http://localhost:6334
- pgAdmin: http://localhost:8081

Makefile highlights:

- `make migrate` — apply EF migrations
- `make sdk` — regenerate OpenAPI/SDK
- `make down` — stop local Docker stack
- `make up` — start infra Docker stack
- `make bootstrap` — nuke volumes, bring up infra, wait for Postgres, migrate, and seed
- `make api` — run API with dotnet watch at http://localhost:5198
- `make web` — run Next.js dev server
- `make mobile` — run Expo dev server (port 8082)
- `make sdk` — build API and generate OpenAPI/SDK
- `make doctor` — run dev-doctor script

## API service (`apps/api`)

### Composition

- Entrypoint: `apps/api/Program.cs`
- EF Core DbContext: partial `AppDbContext` in `Program.cs` + additional partials under `Infrastructure/`
- Swagger registration:
  - Services: `AddEndpointsApiExplorer()`, `AddSwaggerGen(...)`
  - Middleware: `UseSwagger()`, `UseSwaggerUI(...)` with `RoutePrefix = "swagger"` and `SwaggerEndpoint("/swagger/v1/swagger.json", ...)`
  - Redirect: `/swagger` → `/swagger/index.html`

### Auth (dev)

- `DevHeaderAuthHandler` reads headers:
  - `x-dev-user` — user email
  - `x-tenant` — tenant slug
- Emits claims: `sub`, `email`, `tenant_id`, `tenant_slug`
- All `/api/*` endpoints require authorization (dev headers expected)

Security contract (A11-11):

- A development-mode integration test verifies that unauthenticated requests to `/api/*` (e.g., `GET /api/agent-tasks`) return 401 (or 403), while the same requests succeed with the dev headers (200 OK). Swagger endpoints remain public and accessible without authentication.

Seeded defaults (via `apps/api/tools/seed`):

- `x-dev-user: kevin@example.com`
- `x-tenant: kevin-personal`

### Tenant scoping

- `TenantScopeMiddleware` (conventional middleware):
  - Skips `/health*` and `/swagger*`
  - If authenticated and `tenant_id` exists, begins a DB transaction and sets PostgreSQL GUC `app.tenant_id` via `set_config(...)` for tenant RLS
- Additional header-based sample (`X-Tenant-Id`) in `Program.cs` for legacy demo endpoints (`/lessons`)

### Web selector/switcher (Auth‑04/05)

- Two-stage login includes `/select-tenant` page. When only one membership exists, it auto-selects and sets a `selected_tenant` cookie.
- Header `TenantSwitcher` component (rendered in `apps/web/app/layout.tsx`) lets the user change tenants at any time:
  - Calls `session.update({ tenant })` so NextAuth JWT/session reflect the choice.
  - POSTs to `/api/tenant/select` to set `selected_tenant` cookie with `{ httpOnly: true, sameSite: 'lax', secure: NODE_ENV==='production' }`.
  - Server proxy (`buildProxyHeaders`) reads session.tenant or the cookie to forward `x-tenant` to the API in dev.
  - Auth‑10 hardening: when web auth is enabled, proxy headers now require a selected tenant for protected routes (401 if missing). A single exception exists for invite acceptance (`POST /api-proxy/invites/accept`) which allows user‑only auth and omits `x-tenant` during acceptance flow, matching the API guard.

### Auth‑11 — Route protection (role-based)

- Added `apps/web/src/lib/roleGuard.ts` with server-only helpers:
  - `guardProxyRole({ tenantId, anyOf })` for API proxy routes → returns `Response(401|403)` or `null`.
  - `pickMembership(session, { tenantId|tenantSlug })` utility for SSR contexts.
- Enforced Owner/Admin on tenant-sensitive proxy routes:
  - Members: list (GET), update (PUT), remove (DELETE)
  - Invites: list/create (GET/POST), resend (POST), revoke (DELETE)
- SSR page `/studio/admin/members` already required Owner/Admin; proxies now mirror this at the server boundary for defense-in-depth.
- Tests under `apps/web/test/api-proxy/*guard.test.ts` verify 403 when insufficient and successful proxy when authorized.

### Endpoints

Agents endpoints (in `AgentsEndpoints.cs`):

- `GET /api/agents?take=&skip=&includeDisabled=`
  - Returns paged `AgentListItem[]` ordered by `CreatedAt DESC`.
  - Default filters to enabled agents only; pass `includeDisabled=true` to include all.
- `GET /api/agents/{id}` → `AgentDetails`.
- `POST /api/agents` → create agent; enforces unique name; defaults `isEnabled=true`.
- `PUT /api/agents/{id}` → update existing agent; preserves `isEnabled` unless provided.
- `DELETE /api/agents/{id}` → delete agent.
- `GET /api/agents/tools` → list tool catalog for building allowlists in UI.

- Grouped in `apps/api/App/Endpoints/V1.cs` under `/api`:
  - `GET /api/me` — returns user/tenant claims
  - `GET /api/tenants` — current tenant summary
  - `GET /api/lessons?take=&skip=` — paginated list
  - `POST /api/lessons` — create lesson (uses current tenant)
- Non-grouped sample endpoints (dev/testing) in `Program.cs`:
  - `/` — service info
  - `/health`, `/healthz`
  - `/lessons` (GET/POST) — uses `X-Tenant-Id` GUID header

Development-only endpoints and test hooks (mapped only when `ASPNETCORE_ENVIRONMENT=Development`):

- `POST /api/dev/tool-call` (in `DevToolsEndpoints.cs`)
  - Contract: `{ name: string, input: object }`
  - Tools available: `web.search`, `db.query`, `fs.write`
  - Useful for DB inspection and deterministic tool tests without direct SQL access
- `GET /api/dev/agents` (in `DevAgentsEndpoints.cs`)
  - Lists seeded agents from `AgentRegistry` for UI dropdowns
  - Requires dev headers
- `POST /api/dev/agents/demo` (in `DevAgentsDemo.cs`)
  - Runs a `ResearchAgent` inline via the orchestrator and returns `{ task, traces }`
  - Requires dev headers; writes `AgentTask`/`AgentTrace` rows under current tenant
- `POST /api/dev/notifications/verification` and `/invite` (in `DevNotificationsEndpoints.cs`)
  - Enqueues a verification or invite email via the notifications queue for E2E testing against Mailhog
  - Requires dev headers
  - E2E (Notif‑20): Verified full outbox path in Development — enqueue → DB row → SMTP (Mailhog) → `Sent`. Tests gate EF transactions behind `Database.IsRelational()` to support InMemory provider.

Agent task endpoints (in `AgentTasksEndpoints.cs`):

- `POST /api/agent-tasks`
  - Validates `agentId` and non-empty `input` JSON
  - Captures dev headers into `RequestTenant`/`RequestUser`
  - Persists `AgentTask` with `Pending` status, enqueues its id
  - Development-only test hooks: optional `x-test-enqueue-delay-ms`, `x-test-suppress-enqueue`
  - Returns `201 Created` with `AgentTaskSummary`
- `GET /api/agent-tasks/{id}`
  - Returns `AgentTaskDetails` (status, timestamps, optional result/error)
  - With `?includeTraces=true`, returns `{ task, traces }` where traces are ordered `AgentTraceDto[]`
- `GET /api/agent-tasks`
  - Lists `AgentTaskSummary[]` ordered by `CreatedAt DESC`
  - Optional filters: `status` (case-insensitive), `agentId`, `from`, `to`, `q` (free-text)
  - Free-text search is provider-aware: Npgsql uses `EF.Functions.ILike`, others fall back to case-insensitive `Contains`
  - Paging via `take`/`skip`; sets `X-Total-Count` header with total before paging

### Swagger/OpenAPI

- Swagger JSON: `GET /swagger/v1/swagger.json`
- Swagger UI: `GET /swagger/`
- Security scheme: API key ("DevHeaders") — uses `x-dev-user` (with `x-tenant` also expected by auth handler)

## Database and EF Core

- Provider: Npgsql (PostgreSQL 16 in dev)
- Default schema: `app`
- Migrations: `apps/api/Migrations/` (includes `20250911124311_s1_09_agent_runtime` and others)
- Auto-migration in Development/Test: `Database.Migrate()` at startup
- RLS strategy: set `app.tenant_id` via middleware; DB-side policies/init in `infra/initdb` and EF migrations

Agent runtime tables (key): `agents`, `agent_tasks`, `agent_traces` with:

- Unique index on `agent_traces(task_id, step_number)`
- Check constraints: `step_number >= 1`, `duration_ms >= 0`

Agent fields (highlights):

- `IsEnabled boolean default true` — used to soft-disable agents. API list endpoint hides disabled by default unless `includeDisabled=true`.

Auth/Multi-tenant schema updates (MVP prep):

- Tenants use `name` as a slug with a unique index (`app.tenants(name)`), aligning with existing seed and dev headers.
- Foreign keys enforce integrity:
  - `app.memberships(user_id) → app.users(id)` (cascade delete)
  - `app.memberships(tenant_id) → app.tenants(id)` (cascade delete)
  - `app.lessons(tenant_id) → app.tenants(id)` (cascade delete)

Invitations (Auth‑01):

- Table: `app.invitations`
  - Columns: `id uuid PK`, `tenant_id uuid NOT NULL`, `email text NOT NULL`, `role int NOT NULL`, `token text UNIQUE NOT NULL`, `expires_at timestamptz NOT NULL`, `invited_by_user_id uuid NULL`, `accepted_at timestamptz NULL`, `created_at timestamptz DEFAULT now()`
  - FKs: `tenant_id → app.tenants(id)` (CASCADE), `invited_by_user_id → app.users(id)` (SET NULL)
  - Indexes: unique on `token`; functional unique index `UX_invitations_tenant_email_ci` on `(tenant_id, lower(email))` for case-insensitive per‑tenant de‑dup; supporting index `(tenant_id, expires_at)`

Invite acceptance (Auth‑08):

- Endpoint: `POST /api/invites/accept { token }` (authorized)
  - Validates token and expiry; email must match the signed-in user.
  - Creates a `memberships` row for the invite’s tenant with the invite’s role (under RLS via tenant context), idempotent if membership already exists.
  - Marks the invitation `accepted_at`.
- Web: `/invite/accept` SSR route handles signed-in vs. redirect-to-login and calls the API; invite emails link to this route.

Auth‑02 — Passwords & Signup

- Password hashing: Argon2id with per‑user random salt and a configurable pepper (`Auth:PasswordPepper`). Stored fields on `users`: `password_hash text`, `password_salt bytea`, `password_updated_at timestamptz`.
- Anonymous signup endpoint: `POST /api/auth/signup`
  - Input: `{ email, password, inviteToken? }`
  - Behavior: creates `users` row with hashed password; if `inviteToken` is present and valid, creates a `memberships` row for the invite’s tenant with the invite’s role. Otherwise, ensures a personal tenant slug `{localpart}-personal` exists (appending `-2`, `-3`, … if needed for uniqueness) and creates an Owner membership there. Membership insertion is executed under tenant RLS by setting `app.tenant_id` within a transaction.
  - Output: `201 Created` with `{ id, email, tenant: { id, name } }` where tenant reflects either the invite’s tenant or the created/ensured personal tenant.
  - Web: `/signup` posts to the same-origin proxy `/api-proxy/auth/signup` to avoid CORS, normalizes invite token field names (`invite`, `inviteToken`, `token`), surfaces API errors clearly, then signs in via NextAuth Credentials and navigates to `/select-tenant` (or a validated `next`).

## Agent Runtime (v1)

Domain types (`apps/api/Domain/Agents/`):

- `Agent` — fields: Id, Name (<=120), SystemPrompt (text), ToolAllowlist (string[]/jsonb), Model (<=80), Temperature [0..2], MaxSteps [1..50], CreatedAt/UpdatedAt
- `AgentTask` — Id, AgentId (required), InputJson (required), Status, timestamps, optional ResultJson/ErrorMessage
- `AgentTrace` — Id, TaskId (required), StepNumber >= 1, Kind, Name (<=120), InputJson/OutputJson (required), DurationMs/PromptTokens/CompletionTokens (>= 0), CreatedAt
- Enums: `AgentStatus`, `TraceKind`

Validation utilities (`apps/api/Application/Validation/Guard.cs`):

- `NotNull`, `NotNullOrWhiteSpace`, `InRange`, `MaxLength` — used in constructors to enforce invariants with clear param names

EF Core configuration (`apps/api/Infrastructure/Configurations/*`):

- Tables under schema `app`, PostgreSQL-specific mappings (jsonb, text), check constraints, default timestamps, and indexes
- `AppDbContext` applies them via `ApplyConfigurationsFromAssembly(...)`

Registry (`apps/api/Application/Agents/AgentRegistry.cs`):

- Read-only in-memory list of two deterministic agents (v1), exposed for quick list/lookup

Runtime orchestration (`Application/Agents/Runtime`):

- `AgentOrchestrator` drives a loop of model decisions and optional tool calls.
  - Uses `IModelAdapter` (`MockModelAdapter`) for deterministic decisions in dev.
  - Enforces allowlist on tools and guardrails (empty tool name, max steps).
  - Step numbering: reserves two step numbers per iteration (model at N, tool at N+1), then advances to N+2 to satisfy the unique index on traces.
- `TraceWriter` persists traces with clamped non-negative durations/token counts and one-time retry on unique key conflicts.
- `ToolRegistry` wires deterministic tools: `web.search`, `db.query`, `fs.write`.

Queue and worker (`Application/Agents/Queue`):

- `InMemoryAgentTaskQueue` exposes a process-local `Channel<Guid>` with `SingleReader=true` and backpressure.
- `AgentTaskWorker` is a hosted service consuming the channel’s `Reader` so endpoints and worker share the same instance (DI singleton mapping for concrete + interface).
- Processing semantics:
  - Idempotent: only `Pending` tasks are transitioned to `Running`.
  - Graceful cancellation: on shutdown, any in-flight `Running` task is marked `Canceled`.
  - Transient resilience: retry orchestrator once on timeouts/deadlocks before failing.
  - Dequeue/load race mitigation: brief retry loop to read the task after enqueue (handles read-before-commit).
  - Logs dequeued count and last in-flight task id on stop.

Agent resolution (`Application/Agents/AgentStore.cs`):

- Registered as scoped service; used by `AgentTaskWorker` to resolve agents.
- Lookup strategy is DB-first by Id, then falls back to static `AgentRegistry.FindById(id)`.

## Notifications (Email)

Components (`apps/api/App/Notifications/*`):

- IEmailQueue + EmailQueue — in-memory channel used by the background dispatcher.
- EmailDispatcherHostedService — consumes queue, renders templates, sends emails with retry/backoff.
- ITemplateRenderer — `ScribanTemplateRenderer` composes subject/text/html bodies.
- IEmailSender — providers: `SmtpEmailSender` (dev), `SendGridEmailSender` (prod/real), and `NoopEmailSender` fallback.
- NotificationEnqueuer — helpers to enqueue verification and invite emails with correct absolute links.

Configuration keys (Options bound via .NET configuration):

- Email:Provider — "smtp" (default in Development) or "sendgrid" (default in non-Dev if unset)
- Email:WebBaseUrl — base URL for absolute links (default "http://localhost:3000")
- Email:FromAddress — default sender address (default "no-reply@appostolic.local")
- Email:FromName — default sender name (default "Appostolic")
- Smtp:Host — SMTP host (default 127.0.0.1 in Development)
- Smtp:Port — SMTP port (default 1025 in Development; Mailhog)
- Smtp:User / Smtp:Pass — optional credentials
- SendGrid:ApiKey — required when provider=sendgrid in Production

Environment variables (double-underscore syntax maps to nested keys):

- Email**Provider, Email**WebBaseUrl, Email**FromAddress, Email**FromName
- Smtp**Host, Smtp**Port, Smtp**User, Smtp**Pass
- SendGrid\_\_ApiKey (preferred)
- Compatibility shim: if legacy SENDGRID_API_KEY is set and SendGrid:ApiKey is empty, it is used automatically at startup.

Safety/guards:

- In Production, Email:Provider=sendgrid requires a non-empty SendGrid:ApiKey or the app fails fast at startup with a clear message.
- Development defaults to SMTP/Mailhog (127.0.0.1:1025) so no secrets are required for local testing.

Observability:

- Metrics: `email.sent.total` and `email.failed.total` counters (tagged by email kind) exposed via OTEL Meter "Appostolic.Metrics".
- Logs: Dispatcher adds correlation fields to scopes when present on the message data: `email.userId`, `email.tenantId`, `email.inviteId` (plus `email.tenant`, `email.inviter` fallbacks). In Development, logs/metrics are also exported to console.
- Privacy (Notif-25): Recipient emails are redacted in all logs/scopes/providers (e.g., `u***@example.com`). Metrics include only non-PII tags (kind), with no raw emails or tokens.

Local dev (Mailhog):

- With the default SMTP provider, emails appear in Mailhog at http://localhost:8025; no extra setup required.
- To test SendGrid locally, export `SendGrid__ApiKey` in your shell and set `Email__Provider=sendgrid` (or leave provider unset in non-Dev). Never commit real API keys.

## Web app (`apps/web`)

- Next.js 14 (App Router); Node runtime for server routes.
- Server-only API proxy routes under `app/api-proxy/*` inject dev headers and avoid CORS:
  - Agents CRUD:
    - `GET/POST /api-proxy/agents` ↔ API `/api/agents`
    - `GET/PUT/DELETE /api-proxy/agents/{id}` ↔ API `/api/agents/{id}`
    - `GET /api-proxy/agents/tools` ↔ API `/api/agents/tools`
  - `GET /api-proxy/dev/agents` → API `/api/dev/agents`
  - Auth (passwords):
    - `POST /api-proxy/auth/forgot-password` ↔ API `/api/auth/forgot-password`
    - `POST /api-proxy/auth/reset-password` ↔ API `/api/auth/reset-password`
    - `POST /api-proxy/auth/change-password` ↔ API `/api/auth/change-password`
  - `GET /api-proxy/notifications/dlq` and `POST /api-proxy/notifications/dlq` → API `/api/notifications/dlq` and `/api/notifications/dlq/replay` (Owner/Admin)
  - `POST /api-proxy/dev/notifications/verification` → API `/api/dev/notifications/verification`
  - `POST /api-proxy/dev/notifications/invite` → API `/api/dev/notifications/invite`
  - `POST /api-proxy/agent-tasks` → API `/api/agent-tasks`
  - `GET /api-proxy/agent-tasks/{id}?includeTraces=true` → API `/api/agent-tasks/{id}`
  - Tenants/Members (auth):
    - `GET /api-proxy/tenants/{tenantId}/members` ↔ API `/api/tenants/{tenantId}/members`
    - `PUT /api-proxy/tenants/{tenantId}/members/{userId}` ↔ API `/api/tenants/{tenantId}/members/{userId}`
    - `DELETE /api-proxy/tenants/{tenantId}/members/{userId}` ↔ API `/api/tenants/{tenantId}/members/{userId}`
- Env validation in `src/lib/serverEnv.ts` for `NEXT_PUBLIC_API_BASE`, `DEV_USER`, `DEV_TENANT`.
- Dev UI: `/dev/agents` (SSR) lists agents and renders a client `AgentRunForm` to create a task and live-poll details+traces via `useTaskPolling` (750ms until terminal).
- Agent Studio: `/studio/agents` (list), `/studio/agents/new` (create), `/studio/agents/[id]` (edit)
  - Uses `AgentForm` (create/edit) and `AgentsTable` (list)
  - Create/Update payload includes `isEnabled` and `toolAllowlist` selections
  - List defaults to enabled-only agents; UI can be extended to show disabled
- Result panel shows parsed JSON on success; error message shown on failure/cancel; recent run ids tracked client-side.
- Depends on `@appostolic/sdk` for general types/helpers; current panel uses direct fetch via server proxies.

- Magic Link (web):
  - Public pages: `/magic/request` (email form) and `/magic/verify` (consumes token and redirects to `/select-tenant`).
  - Proxies: `POST /api-proxy/auth/magic/request` → API `/api/auth/magic/request`; `POST /api-proxy/auth/magic/consume` → API `/api/auth/magic/consume`.

### Test coverage (Phase 0 additions)

- Middleware gating tests ensure unauthenticated users are redirected to `/login` and authenticated users are kept away from `/login`.
- Logout page smoke test verifies `signOut({ redirect: false })` is called and the client navigates to `/login?loggedOut=1`.
- Login page includes a “Forgot password?” link with the correct href.
- Password flow tests cover forgot/reset/change (happy paths) and negative cases (invalid token, wrong current password).
- API proxy smokes for AgentTasks:
  - `GET /api-proxy/agent-tasks` returns 401 when unauthenticated and 200 when headers are present (mocked).
  - `POST /api-proxy/agent-tasks` returns 201 and forwards the `Location` header from the API.

## SDK and OpenAPI (`packages/sdk`)

- TS SDK scaffolding under `packages/sdk`
- OpenAPI generated from compiled API via Swashbuckle CLI (output at `packages/sdk/openapi.json`)
- SDK currently provides minimal fetch helpers/types; can be expanded via codegen

## Infra (`infra/`)

- Docker Compose services: Postgres, Redis, MinIO, Qdrant, Mailhog, pgAdmin
- Init SQL: `infra/initdb/init.sql` sets extensions, schemas, GUC helpers
- Devcontainer configuration available

### Dev credentials (local only)

From `infra/docker/.env` (for local development):

- Postgres:
  - Host: `localhost`
  - Port: `55432`
  - Database: `appdb`
  - User: `appuser`
  - Password: `apppassword`
- Redis:
  - Host: `localhost`
  - Port: `6380`
- MinIO:
  - Root User: `minio`
  - Root Password: `minio123`
  - API: `http://localhost:9002`
  - Console: `http://localhost:9003`
- Mailhog:
  - UI: `http://localhost:8025`
  - SMTP: `1025`
- Qdrant:
  - Host: `localhost`
  - Port: `6334`
- pgAdmin:
  - UI: `http://localhost:8081`
  - Email: `admin@example.com`
  - Password: `admin123`

Dev auth for API testing:

- Send headers on `/api/*` requests:
  - Default seed: `x-dev-user: kevin@example.com`, `x-tenant: kevin-personal`
  - If you use different seed data, adjust these accordingly. Some older docs may still reference `dev@example.com` / `acme`.

## Troubleshooting

- Swagger shows JSON instead of UI: use `/swagger/` or rely on `/swagger` redirect; ensure `UseSwaggerUI` configured and only registered once
- 401 on `/api/*`: send `x-dev-user` and `x-tenant` headers; if you haven’t seeded, run `make seed` (or `make bootstrap`)
- Tenant scoping not applied: verify `tenant_id` claim exists (set by dev auth) or use `X-Tenant-Id` for legacy `/lessons`
- Postgres connection errors: ensure Docker is up and port `55432` matches env
- Next.js server error “Failed to parse URL from /api-proxy/…”: A server component used a relative fetch to a Next API route. Fix by using `fetchFromProxy('/api-proxy/...')` from `app/lib/serverFetch.ts` (which builds an absolute URL from headers), or set `NEXT_PUBLIC_WEB_BASE` and restart the web server.
- Browser CORS on `/signup`: ensure the form posts to `/api-proxy/auth/signup` (same-origin proxy) rather than the API base URL directly.
- “Cookies can only be modified in a Server Action or Route Handler” when selecting tenant: perform selection via `GET /api/tenant/select?tenant=...&next=...` which sets the cookie server-side and redirects.

Node/pnpm engines:

- The repo requires `node >=20 <21` (see root `package.json`). Using Node 19 may error with `TypeError: URL.canParse is not a function` via Corepack.
- Fix locally by switching to Node 20.x (e.g., `nvm use 20`) and ensuring Corepack/pnpm use that runtime.

Development-only tools:

- Use `POST /api/dev/tool-call` with `db.query` to inspect tables under schema `app` and to count `agent_tasks`/`agent_traces` without psql.

## Prompt starters (copy into ChatGPT)

- “Expose read-only endpoints for dev agents (already available at `/api/dev/agents`) and consider promoting to `/api/agents` for non-dev contexts. Update Swagger and web to consume the stable path.”
- “Extend EF configurations for Agent runtime with additional indexes and constraints [describe]. Create and apply a migration and confirm defaults in PostgreSQL.”
- “Regenerate OpenAPI and update the TypeScript SDK to include new endpoints (AgentTasks, DevAgents). Update `apps/web` to call through the SDK or keep using server proxies.”
- “Implement AgentTask flows (done: create, get by id with traces, list with filters). Add delete/cancel if needed and write integration tests.”
- “Harden `TenantScopeMiddleware`: ensure GUC set/reset semantics, add diagnostics, and handle exceptions explicitly.”
- “Add integration tests for `/api/lessons` using dev auth and a test Postgres; include test harness and migrations in CI.”

---

Paste the above into ChatGPT when asking for changes so it can reference exact file paths, endpoints, and conventions used here.
