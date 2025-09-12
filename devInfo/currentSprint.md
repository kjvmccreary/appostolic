# Sprint Plan — Auth MVP (Credentials, Multi‑Tenant)

> Goal: Replace dev-only headers with a real sign-in flow in the web app and safely bridge session → API dev headers. Keep the API under dev headers for now, but make the web experience feel production-grade (login, logout, route protection), with seed data and minimal tests. This is a one-sprint MVP focused on correctness and safety.

## Objectives

- Add username/password authentication to the Next.js web app with secure session cookies (Auth.js Credentials provider + argon2id).
- Persist users/tenants/memberships in Postgres and seed a SuperAdmin and a default tenant.
- Protect studio/dev routes via middleware; redirect unauthenticated users to /login.
- Map the logged-in session to the API dev headers on server-only proxy routes so the API continues to work unchanged.
- Ship minimal tests for login success/failure, route protection, and header mapping.

## Non-goals (deferred)

- OAuth/SSO (Google/Microsoft), email magic links, 2FA/MFA.
- API-side real auth/claims issuance (API remains on dev headers in this MVP).
- Full user management UI (invite flows, password reset, profile edit).
- Role-based UI gating beyond route-level protection.

## Architecture decisions

- Use Auth.js (NextAuth) Credentials provider in `apps/web` with an adapter backed by Postgres.
- Store password hashes with argon2id; enforce sane defaults (memory/time cost, salt, pepper via server-side secret).
- Rate-limit login attempts and use CSRF protection from Auth.js; secure cookies (SameSite=Lax, secure in prod).
- Keep API unchanged; server-only Next.js proxy routes will read session and set `x-dev-user` and `x-tenant` headers.
- Minimal schema alignment to enable future Org features: `users`, `tenants`, `memberships` (role enum: Owner/Admin/Member).

## Data model (MVP)

- users
  - id (uuid, PK)
  - email (citext unique)
  - password_hash (text, argon2id)
  - name (text, nullable)
  - created_at, updated_at (timestamptz)
- tenants
  - id (uuid, PK)
  - slug (text unique, lowercase)
  - name (text)
  - created_at, updated_at
- memberships
  - user_id (uuid, FK users)
  - tenant_id (uuid, FK tenants)
  - role (text enum: owner|admin|member)
  - unique (user_id, tenant_id)

Seed defaults

- SuperAdmin user: email from env (AUTH_SEED_EMAIL) and password from env (AUTH_SEED_PASSWORD).
- Default tenant: slug `kevin-personal` (align with current dev headers) and membership role=owner for SuperAdmin.

## Stories and acceptance criteria

1. ~~Schema + seed (API or infra)~~

- AC1: Running `make migrate && make seed` creates `users`, `tenants`, `memberships` and inserts the SuperAdmin + default tenant + membership idempotently. (Done)
- AC2: Unique constraints and FK constraints enforced; citext or case-insensitive uniqueness on user email and tenant slug lowercased. (Done)

2. Web auth (Credentials)

- ~~AC1: `/login` renders a form (email, password); submitting valid credentials establishes a session and redirects to the originally requested page or `/studio/agents`.~~
- ~~AC2: Invalid credentials show an inline error with no redirect; form remains accessible.~~
- AC3: `/logout` ends the session and redirects to `/login`.
- AC4: Cookies are httpOnly, SameSite=Lax, secure in production; CSRF token present on the form.

3. Route protection middleware

- AC1: Visiting any page under `/studio/*` or `/dev/*` without a session redirects 302 to `/login?next=/requested/path`.
- AC2: Visiting `/login` while already authenticated redirects to `/studio/agents`.

4. Session → API headers bridge

- AC1: Server-only API proxy routes under `apps/web/app/api-proxy/*` inject `x-dev-user` (session email) and `x-tenant` (selected tenant slug) instead of env defaults when a session exists.
- AC2: If no session exists, proxy refuses the request with 401 (do not fall back to env headers) for protected resources.
- AC3: A simple tenant selector persists the chosen tenant slug in session or cookie; defaults to the first membership.

5. Tests (minimal)

- AC1: Web unit/integration tests (Vitest + RTL + MSW) cover: valid login, invalid login, protected route redirect, and header mapping on one proxy handler.
- AC2: Contract check: unauthenticated call to a protected proxy endpoint returns 401; same call with session returns 200.

## Implementation plan (by repo path)

apps/web

- Add Auth.js with Credentials provider under `app/api/auth/[...nextauth]/route.ts`.
- Create `src/lib/auth.ts` for Auth.js config (adapter, providers, callbacks), and `src/lib/hash.ts` utilities (argon2id verify/hash).
- Add `app/login/page.tsx` with form and client actions; add `app/logout/route.ts` to sign out.
- Add `middleware.ts` to guard `/studio/:path*` and `/dev/:path*` using `getToken()` or session check.
- Update server proxy routes in `app/api-proxy/*` to read session and set `x-dev-user`/`x-tenant` from session; remove env defaults fallback when session exists.
- Add tenant selection: a small server component in layout (top-right menu) hitting a server action to set selected tenant; persist in session or secure cookie.
- Env: `AUTH_SECRET`, `NEXTAUTH_URL`, `AUTH_DATABASE_URL` (pointing to dev Postgres), `ARGON2_SECRET_PEPPER`.

apps/api (optional minimal glue)

- Ensure seed includes SuperAdmin and default tenant rows matching the web side. Expose a dev-only GET `/api/dev/seed/auth` if helpful, or keep seeding under existing tools.
- No API auth change in MVP; continue to require `x-dev-user` and `x-tenant` (provided by web proxy).

infra

- Extend `infra/docker/.env` with `AUTH_SEED_EMAIL`, `AUTH_SEED_PASSWORD`, `AUTH_DATABASE_URL`.
- Document `make bootstrap` bringing up infra and seeding auth tables.

packages/sdk

- No change required for MVP; proxies remain server-only in web.

## Security and hardening

- Argon2id parameters: memoryCost >= 64MB, timeCost >= 2, parallelism 1–2; use a per-user salt + server-side pepper (`ARGON2_SECRET_PEPPER`).
- Lockout policy: exponential backoff or temporary lockout after N failed attempts per email+IP; store counters in Redis or in-memory for dev.
- CSRF: Auth.js built-in anti-CSRF; ensure forms include the token.
- Cookies: httpOnly, SameSite=Lax, secure in prod, short session duration (e.g., 7 days) with sliding refresh.

## Dev UX and docs

- Update `README.md` and `RUNBOOK.md` with the new login flow and environment variables.
- Update `apps/web/README.md` with instructions for running the web with Auth.js and the seed credentials.
- Add troubleshooting: common errors (missing AUTH_SECRET, cookie domain, database URL, seed not applied).

## Rollout

- Behind a feature flag `WEB_AUTH_ENABLED=true` to allow incremental testing; when enabled, proxies require a session.
- Replace existing env header injection only when a session exists; otherwise requests are blocked at the proxy and middleware ensures redirect to `/login`.

## Definition of done

- You can `make bootstrap`, navigate to `/login`, sign in with seeded credentials, land on `/studio/agents`, and create/list agent tasks successfully. Network shows proxy requests carrying `x-dev-user` and `x-tenant` from the session.
- All new tests pass locally (web), and existing API and web test suites remain green.
- Docs updated; no leaking of dev header env defaults when a session exists.

## Risks and mitigations

- Risk: Argon2 parameters too heavy for dev laptops → allow env override for lower-cost dev settings; keep strong defaults in prod.
- Risk: Session-cookie misconfiguration in dev (http vs https) → document `NEXTAUTH_URL` and dev cookie behavior clearly.
- Risk: Header mapping drift → add a unit test targeting one proxy route asserting headers come from session.

## Estimated sequence

1. Schema + seed plumbing (infra + api) — 0.5–1d.
2. Auth.js wiring + login/logout pages — 1–1.5d.
3. Middleware protection + tenant selection — 0.5–1d.
4. Proxy header mapping + tests — 0.5–1d.
5. Docs + cleanup — 0.5d.

Total: ~3–5 days of focused work.
