## IAM Sprint Plan — Hardcoded Roles (TenantAdmin, Approver, Creator, Learner)

Context

- Grounded in the 1.0 cut (see devInfo/DesignDocs/MvpCutMatrix.md): hardcoded system-level roles; tenants can only assign/unassign roles to members, not create/edit roles.
- Baseline exists for Users and Tenants; this sprint adds Roles and tenant-scoped enforcement across API and Web.
- Roles set: TenantAdmin, Approver, Creator, Learner. Multiple roles per member allowed; enforcement is server-first with client hints.

Guiding principles

- Hardcoded roles; no runtime role creation or permission editing.
- Roles are tenant-scoped (membership-level). Session reflects the current tenant’s effective roles.
- API is the source of truth and enforces authorization; Web surfaces role-aware UI but never relies on client-only checks.
- Simple, evolvable model: start with [Flags] enum for speed; can migrate to normalized join later if needed.

Deliverables and documentation cadence

- At the end of every story: update SnapshotArchitecture.md; append the exact story summary to devInfo/storyLog.md; commit with message and sync; mark the story ✅ DONE here.

---

Sprint 1 — Backend Roles Core

Story 1.1 — Add Roles enum and migration — ✅ DONE

- Scope:
  - Define enum Roles: None=0, TenantAdmin=1, Approver=2, Creator=4, Learner=8 with [Flags].
  - Add Roles (int, non-null, default 0) to TenantMembership.
  - EF migration; existing memberships default to None (no auto-elevation).
- Acceptance:
  - DB updated; migration applies cleanly; build/tests pass.
- Tests:
  - Mapping roundtrip (enum <-> int); migration smoke.

Story 1.2 — Role enforcement primitives — ✅ DONE

- Scope:
  - Authorization helpers: RequireTenantAdmin, RequireApprover, RequireCreator, RequireLearner.
  - Minimal API filters or attribute-based handlers to check roles after tenant resolution.
- Acceptance:
  - Reusable helpers; 403 with ProblemDetails on insufficient role.
- Tests:
  - Unit tests per role check (allow/deny).

Story 1.3 — Apply enforcement to critical endpoints — ✅ DONE

- Scope:
  - Creator: lesson create/draft endpoints.
  - Approver: approve/publish endpoints.
  - TenantAdmin: tenant settings and membership admin.
  - Learner: reserved (no-op initially).
- Acceptance:
  - 403 for insufficient role; happy-path remains 2xx.
- Tests:
  - Integration tests per endpoint (200 allowed, 403 denied).

Implementation notes:

- Added uniform 403 ProblemDetails via a custom IAuthorizationMiddlewareResultHandler and a small fallback middleware for manual forbids (content-type application/problem+json with extensions { tenantId, requiredRoles }).
- Implemented RoleRequirement/Handler mapping legacy MembershipRole to new Roles flags for compatibility.
- Applied policies:
  - Creator: POST /api/lessons.
  - TenantAdmin: members and invites endpoints (list, create, resend, delete, change role, remove member).
  - Approver: no-op for now (approve/publish endpoints not yet present in V1).

Story 1.4 — Seed guardrails and invariants — ✅ DONE

- Scope:
  - Invariant: at least one TenantAdmin per tenant.
  - Service ops to promote/demote with invariant enforcement.
- Acceptance:
  - Cannot remove last TenantAdmin; return 409 with clear message.
- Tests:
  - Demote last admin → 409; add another admin then demote → 204.

Implementation notes:

- Enforced invariant in V1 endpoints:
  - PUT /api/tenants/{tenantId}/members/{userId} blocks demotions from Owner/Admin to non-admin when it would leave zero TenantAdmins (409 Conflict).
  - DELETE /api/tenants/{tenantId}/members/{userId} blocks removal of the last Owner/Admin (409 Conflict).
- Removed legacy owner-only demotion logic in favor of the TenantAdmin invariant. Self-removal remains blocked with 400 when not the last admin; invariant check takes precedence.
- Updated tests to reflect 409 semantics and added a demotion-allowed case when another admin exists.

---

Sprint 2 — Assignment APIs and Web Admin UI

Story 2.1 — Assignment APIs — ✅ DONE
Story 3.3 — Audit trail for assignments — ✅ DONE

- Scope:
  - Persist audit entries when membership roles flags are changed by TenantAdmins.
  - Include who changed it (userId/email), old/new flags, when, and tenant/user.
- Implementation:
  - Added `Audit` entity and `app.audits` table with index on (tenant_id, changed_at).
  - Endpoint `POST /api/tenants/{tenantId}/memberships/{userId}/roles` writes audit after successful change in both InMemory and relational paths.
  - Unit test `AuditTrailTests` verifies audit row content.

- Scope:
  - GET /api/tenants/{tenantId}/memberships — list members with roles.
  - POST /api/tenants/{tenantId}/memberships/{userId}/roles — set roles flags (replace).
  - Optional: POST add/remove endpoints for additive operations.
  - All require TenantAdmin.
- Acceptance:
  - 403 if caller not admin; 404 if membership missing; 409 on last-admin removal.
- Tests:
  - Integration tests for list/set/add/remove; last-admin protection.

Story 2.2 — Invites include roles — ✅ DONE

- Scope:
  - Extend invite DTO to include initial Roles flags (array of enum names).
  - On accept, create membership with provided Roles; if omitted, derive flags from the legacy Role for compatibility (Owner/Admin → Admin+Approver+Creator+Learner; Editor → Creator+Learner; Viewer → Learner).
- Acceptance:
  - Invites without Roles derive flags from the legacy Role; invites with Roles use the provided flags. Operations that would leave zero TenantAdmins remain blocked (409) by invariant.
- Tests:
  - Create/List/Resend include roles string and rolesValue int; invite with invalid flag returns 400; accept sets membership.Roles correctly; invariant holds.

Story 2.3 — Web: Roles-aware session — ✅ DONE

- Scope:
  - NextAuth JWT/session: include memberships[].roles and booleans for current tenant: isAdmin, canApprove, canCreate, isLearner.
- Acceptance:
  - /api/debug/session (or equivalent) reflects flags + booleans for selected tenant.
- Tests:
  - Session callback unit tests for role flags and derived booleans.

Implementation notes:

- Added `apps/web/src/lib/roles.ts` with helpers to derive flags from legacy and compute booleans for a selected tenant.
- Extended NextAuth callbacks in `apps/web/src/lib/auth.ts` to carry memberships[].roles and derive booleans + rolesForTenant on each JWT calc; surfaced in session.
- Enhanced dev endpoint `GET /api/debug/session` to include derived booleans/roles alongside session and cookies.
- Added unit tests: `roles.test.ts` and `auth.session.test.ts` covering derivations and tenant switching.

Story 2.4 — Web: Membership admin page — ✅ DONE

- Scope:
  - Route: /studio/admin/members.
  - Table of members with checkboxes (Admin, Approver, Creator, Learner).
  - Save via API proxy; disable unchecking last admin; inline error UX.
- Acceptance:
  - TenantAdmin can view/edit; non-admin sees 403 component.
- Tests:
  - Added proxy route tests for list and save handlers.
  - Page-level server component tests to cover 403 rendering and last-admin UI guard are planned alongside 3.x gating stories.
  - For now, proxy integration tests validate end-to-end wiring; server guards remain primary.

---

Sprint 3 — Role-Aware UX and Guarding

Story 3.1 — Hide/show actions by role — ✅ DONE

- Scope:
  - Creator-only actions (Create Lesson) shown if canCreate.
  - Agents index “New Agent” CTA (header + empty-state) shown if canCreate.
  - Agents per-row: Edit shown if canCreate; Delete disabled unless canCreate; Run available to all.
  - Admin-only nav remains visible if isAdmin (no changes required in this slice).
- Acceptance:
  - Dashboard/TopBar and Agents pages reflect effective roles; no client-only elevation.
- Tests:
  - TopBar render tests toggling session.canCreate to assert Create Lesson visibility.
  - AgentsTable tests assert empty-state CTA gating and row Edit/Delete behaviors by canCreate.

Story 3.2 — Gate routes in Web (flags-based) — ✅ DONE

- Scope:
  - Server components for protected pages check roles server-side and render 403 component if lacking permission.
  - API remains primary enforcement.
- Acceptance:
  - Visiting /studio/admin/members as non-admin renders AccessDenied.
- Tests:
  - Server component tests with mocked headers/session.

Implementation notes

- Converted server guards to flags-based checks using session-derived booleans:
  - Agents proxies: POST/PUT/DELETE require canCreate.
  - Tenants admin proxies (members, memberships, invites): legacy Owner/Admin guard now maps to TenantAdmin via flags under the hood.
  - Notifications DLQ proxy continues to allow Owner/Admin, mapped to TenantAdmin via flags.
- Tests added/passing:
  - app/api-proxy/agents/route.test.ts — 403 when canCreate=false.
  - app/api-proxy/agents/[id]/route.test.ts — 403 for PUT/DELETE when canCreate=false.
  - Existing guard tests for invites/members/memberships/DLQ remain green with flags mapping.
- Server page (/studio/admin/members) renders a 403 component for non-admins as expected.

Guard usage note (for new routes)

- Prefer using session-derived flags for concision:
  - Proxy example (Creator): use `guardByFlags({ tenantIdOrSlug: { id: tenantId }, require: 'canCreate' })`.
  - Proxy example (Admin): for legacy Owner/Admin call sites, continue passing `anyOf: ['Owner','Admin']` to `guardProxyRole` — it maps to TenantAdmin flags under the hood.
  - Server Page example: when asserting access, use `requirePageRole(['Admin'])` for admin-only.

Story 3.3 — Audit trails for assignments — ✅ DONE

- Scope:
  - Persist audit entries when roles change (who/when/old→new) under tenant scope.
- Acceptance:
  - Entries persisted and queryable.
- Implementation:
  - Added `Audit` entity and `DbSet<Audit>`; mapped to `app.audits` with columns { id, tenant_id, user_id, changed_by_user_id, changed_by_email, old_roles, new_roles, changed_at } and index on (tenant_id, changed_at).
  - Hooked audit creation into `POST /api/tenants/{tenantId}/memberships/{userId}/roles` after successful change (both InMemory and relational transaction paths). Caller identity inferred from claims (sub/email) when available.
  - Migration `s4_02_membership_roles_audits` adds the table; snapshot updated.
- Tests:
  - `apps/api.tests/Api/AuditTrailTests.cs` verifies an audit row is written with correct old/new flags and ChangedByEmail upon roles change.

---

Sprint 4 — Hardening, Docs, and Seeds

Story 4.1 — Seeds and fixtures

- Scope:
  - Seed baseline tenants with one admin, one creator, one approver, one learner.
  - Dev convenience: CLI or dev endpoint to grant roles locally.
- Acceptance:
  - make seed leads to consistent roles in dev.
- Tests:
  - Seed smoke tests in integration harness.

Story 4.2 — Docs and SnapshotArchitecture

- Scope:
  - Document roles model, enforcement policies, APIs, and Web behavior.
- Acceptance:
  - SnapshotArchitecture updated; role matrix included.

Story 4.3 — QA checklist and polish

- Scope:
  - Manual checks: can/can’t create/approve/admin based on role; consistent 401/403 ProblemDetails.
- Acceptance:
  - Checklist complete; no blockers.

---

Controller/Endpoint Authorization Plan

- Register policies in Program.cs, mapping 1:1 to Roles flags:
  - Policies: "TenantAdmin", "Approver", "Creator", "Learner".
- Tenant resolution early in the pipeline:
  - Middleware/endpoint filter extracts tenantId from route or from header (x-tenant) and stores Guid in HttpContext.Items.
- Role requirement + handler:
  - AuthorizationHandler<RoleRequirement> loads membership by (user, tenant), evaluates flags, and caches for ~45s.
  - Return 403 with ProblemDetails on insufficient role.
- Resource-based auth for entity routes:
  - Handlers that resolve entity → tenantId (e.g., lessonId) then apply the same role check.
- Apply policies:
  - Controllers: [Authorize(Policy = "TenantAdmin")] or specific policies per action.
  - Minimal APIs: .RequireAuthorization("Creator"), etc.
- Consistent ProblemDetails for 401/403 with extensions { tenantId, requiredRole }.
- Dev vs prod:
  - Dev-only auth handler maps x-dev-user/x-tenant to ClaimsPrincipal and tenant id; production continues via server proxy until JWT is introduced.

---

Backlog (Optional Enhancements) — Approved to include

- Per-route/per-action policy mapping table in docs.
- Role change notifications (email) to affected users.
- Role badges/pills in UI near user avatar or TenantSwitcher.
- Exportable audit log for tenant admins.
- Rate limits on assignment endpoints.
- Short-TTL cache for (user, tenant) roles; clear on role changes.

---

Notes

- We will prioritize simplicity and stability for 1.0. The [Flags] enum gets us moving fast; we can evolve storage later without breaking the API surface.
- All role checks are tenant-scoped. Cross-tenant actions are out of scope for 1.0.
