# Auth Sprint Plan — Executable Stories

This plan breaks the Auth MVP design into concrete, sequential stories. Each story includes a clear outcome, acceptance criteria, and key tasks.

## ✅ (DONE) Auth-01 — Schema & Migrations (Users/Memberships/Invitations)

Summary

- Add DB support for user management and invitations: role enum on memberships, invitations table, indexes, and seeds.

Dependencies

- None

Acceptance Criteria

- EF Core migration adds:
  - memberships.role enum in {Admin, Creator, Reviewer, Learner}
  - invitations table: id, tenant_id, email (citext), role, token, expires_at (now + 7d), invited_by_user_id, accepted_at, created_at
  - uniques and FKs: unique active invite per (tenant_id, email); FKs for tenant, invited_by
  - ensure users.email (citext unique) and tenants.slug unique
- Seed SuperAdmin (from env) and a default tenant with Admin membership
- Migrations apply cleanly via `make migrate`

Key Tasks

- Extend EF models/configurations; create migrations
- Add enum mapping for MembershipRole
- Add Invitations entity + configuration (indexes, constraints)
- Seed SuperAdmin + default tenant (idempotent)

---

## ✅ (DONE) Auth-02 — Password Hashing & Signup API

Summary

- Create secure signup endpoint storing argon2id hashes; create personal tenant except invite path.

Dependencies

- Auth-01

Acceptance Criteria

- POST /api/auth/signup accepts { email, name?, password }
- Stores password as argon2id (pepper via env)
- Self-serve (no invite token): creates personal tenant (slug: {email_local}-personal, collision-safe) and Admin membership
- Invite token present for a brand-new user: accepts invite; does NOT create personal tenant; membership created on inviting tenant
- Returns 201 + minimal user payload; auto-sign-in token available to web

Key Tasks

- Add argon2 helpers (reuse existing `hash.ts` or port to API if needed)
- Implement signup handler and personal-tenant creation logic
- Handle optional invite token during signup (DB transaction)

---

## ✅ (DONE) Auth-03 — Credentials Auth via DB (NextAuth)

Summary

- Switch NextAuth authorize to DB lookup (users table) instead of env seed values.

Dependencies

- Auth-02

Acceptance Criteria

- NextAuth Credentials authorizes by fetching user by email and verifying argon2id hash
- Env-only fallback removed (or behind feature flag disabled by default)
- Login succeeds/fails appropriately; no plain-text password comparisons

Key Tasks

- Add minimal DB access from web (direct or via API) to verify password
- Update `apps/web/src/lib/auth.ts` authorize accordingly; keep pages unchanged

---

## ✅ (DONE) Auth-04 — Two-Stage Login: /select-tenant

Summary

- After login, if multiple memberships: show tenant selection page; auto-select if only one.

Dependencies

- Auth-03

Acceptance Criteria

- /select-tenant lists memberships (tenant name + role)
- Selecting a tenant sets selectedTenantSlug (+ role) in JWT/session and cookie
- Auto-redirect to /studio if only one membership

Key Tasks

- Add `/select-tenant` page (SSR), server action to set selection
- Update session callbacks to include selected tenant/role

---

## Auth-05 — Header Tenant Switcher

Summary

- Allow switching active tenant from the app header; refresh session to reflect selection.

Dependencies

- Auth-04

Acceptance Criteria

- Header shows current tenant; dropdown lists user’s memberships
- Switching updates session/JWT and reloads current page in the new tenant context

Key Tasks

- Build header component + server action to update selection
- Persist selection in session/JWT; test proxy header mapping

---

## Auth-06 — Members List (Admin Read-Only)

Summary

- Admin-only page to view current members of selected tenant.

Dependencies

- Auth-04

Acceptance Criteria

- `/studio/admin/members` renders list of members (name, email, role, joinedAt)
- Access requires Admin role in selected tenant

Key Tasks

- API: GET /api/tenants/{tenantId}/members (dev headers for MVP)
- Web: SSR page + table UI
- Guard route via middleware and server checks

---

## Auth-07 — Invite API & Email (Dev)

Summary

- Admin can invite a user with a role; send tokenized link (Mailhog in dev).

Dependencies

- Auth-06

Acceptance Criteria

- POST /api/tenants/{tenantId}/invites { email, role } returns 201 with invite id
- Generates single-use token; expires in 7 days; enforces 1 active invite per (tenant,email)
- Sends email with `/invite/accept?token=...` (dev via Mailhog)

Key Tasks

- Implement token generation and invitation row
- Add mail sender (dev-only)
- Swagger updated; basic error cases (already a member, invalid role)

---

## Auth-08 — Invite Acceptance Flow

Summary

- Accept invite; handle signed-in or new user paths.

Dependencies

- Auth-07, Auth-02

Acceptance Criteria

- POST /api/invites/accept { token } validates token, tenant, expiry
- If signed in and email matches invite → create membership with invite.role
- If not signed in → redirect to sign-in/sign-up, then accept
- On success, redirect: multi-tenant → /select-tenant; single → /studio

Key Tasks

- API accept handler with transactional updates
- Web page `/invite/accept` orchestrating the flows

---

## Auth-09 — Members Management (Admin Write)

Summary

- Admin can change a member’s role or remove a member.

Dependencies

- Auth-06

Acceptance Criteria

- PATCH /api/tenants/{tenantId}/members/{userId} { role } updates role
- DELETE /api/tenants/{tenantId}/members/{userId} removes membership (with safeguards)
- UI updates in `/studio/admin/members` reflect changes

Key Tasks

- API endpoints and validations
- Web UI actions (role select, remove with confirm)

---

## Auth-10 — Proxy Header Mapping & Guards

Summary

- Ensure server proxy routes include x-dev-user and x-tenant from session; block when tenant not selected.

Dependencies

- Auth-04

Acceptance Criteria

- Proxies use selectedTenantSlug; 401 when no session or no selected tenant
- Unit tests cover header mapping (session → headers) and 401 paths

Key Tasks

- Adjust `proxyHeaders.ts` to require selected tenant
- Update tests for `/api-proxy/*` routes

---

## Auth-11 — Route Protection (Role-based)

Summary

- Guard Admin-only routes and pages.

Dependencies

- Auth-04

Acceptance Criteria

- `/studio/admin/*` requires Admin role; redirect to `/login?next=` when unauth; 403 when lacking role
- Middleware + server-side guards enforced

Key Tasks

- Update middleware config and server checks
- Add tests for redirects/403s

---

## Auth-12 — API Integration Tests (Security & Flows)

Summary

- Integration tests for core auth flows and security contracts.

Dependencies

- Auth-01..Auth-11

Acceptance Criteria

- Tests cover: signup (OK/KO), invite create/accept, role change, remove member, list members
- Contract: unauthenticated `/api/*` returns 401/403; Swagger JSON remains public

Key Tasks

- Add test project cases; use Development env and in-memory/dev DB

---

## Auth-13 — Web Tests (Sign-up, Invite, Two-Stage, Switcher)

Summary

- Unit/integration tests in web for new flows and UI.

Dependencies

- Auth-02, Auth-04, Auth-07, Auth-08, Auth-09

Acceptance Criteria

- Sign-up happy/invalid
- Invite accept (signed-in and new-user paths)
- /select-tenant works; auto-select single tenant
- Header switcher updates session and proxies

Key Tasks

- Vitest + RTL + MSW tests for pages/components

---

## Auth-14 — Docs & Runbook Updates

Summary

- Document auth flows, env, setup, and troubleshooting.

Dependencies

- Auth-01..Auth-13

Acceptance Criteria

- README/RUNBOOK updated with:
  - Env vars (AUTH_SECRET, ARGON2_PEPPER, etc.)
  - Migrations, seeding and dev email
  - Flows: signup, two-stage login, invite, role mgmt
- SnapshotArchitecture updated to reflect new endpoints and pages

Key Tasks

- Write docs and update architecture snapshot

---

## Sequencing & Notes

- Suggested order: Auth-01 → Auth-02 → Auth-03 → Auth-04 → Auth-05 → Auth-06 → Auth-07 → Auth-08 → Auth-09 → Auth-10 → Auth-11 → Auth-12 → Auth-13 → Auth-14
- Invite role is enforced as provided by the inviting tenant; tokens expire after 7 days
- Personal tenant auto-creation only for self-serve signup (no invite)
- Two-stage login ensures tenant context before accessing tenant-scoped routes; auto-select when single membership
