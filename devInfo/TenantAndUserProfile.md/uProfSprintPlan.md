# Sprint Plan — User & Tenant Profile (Release 1.0 scope)

Sprint goal

- Deliver a cohesive User Profile and Tenant Settings experience:
  - Users can manage personal info, avatar, social links, guardrails, and preferred lesson format.
  - Users can change their password.
  - Tenant Admins can manage basic tenant settings (display name, logo, contact, social).
  - Web UI matches current design system and navigation patterns, per `devInfo/DesignDocs/UI-Spec.md`.

Scope (included)

- Backend: EF model extensions, migrations, and endpoints for user profile, password change, avatar upload; tenant settings and logo upload.
- Frontend: `/profile` and `/studio/admin/settings` pages with server-first auth/guards, forms, and uploads via proxy.
- Storage: Full S3-compatible object storage integration (MinIO in dev, S3-compatible in prod) for avatars/logos with signed URLs and simple public-read option in dev; abstraction surfaces a stable relative URL in API responses.
- Rich profile bio editor and safe rendering (sanitized HTML or Markdown→HTML) aligned with UI tokens.
- Denomination presets library available to the profile guardrails UI (preset picker + freeform overrides).
- Hashing/redaction of PII where appropriate (logs/metrics); derive-only hashed values when needed for analytics or dedupe.
- Tests: API unit/integration tests and Web unit tests; basic happy-path smoke; adherence to coverage thresholds.
- Docs: SnapshotArchitecture and storyLog updates; LivingChecklist ticks.

Out of scope (post‑1.0 candidates)

- Third-party OAuth providers for avatar sync.
- Advanced security features (MFA, passwordless everywhere, email change verification flow).

Assumptions

- Profile data is stored at the User level (not per-membership); tenant settings are stored at Tenant level.
- Web continues server-first authorization; API continues to rely on dev headers locally and session/claims in prod.
- Acceptable image formats: JPEG/PNG/WebP; max 2MB; thumbnails at 256x; store original in object storage; optionally return 256px variant URL if available.

Global acceptance (Definition of Done)

- Build/Typecheck/Lint PASS across API and Web.
- Tests PASS; coverage thresholds unchanged and satisfied.
- UI aligns with `UI-Spec.md`: server-first guards, a11y baselines, theming, navigation, mobile responsiveness.
- Docs updated: `SnapshotArchitecture.md`, `devInfo/storyLog.md`, and items ticked in `devInfo/LivingChecklist.md` as applicable.

UI alignment checklist (from UI-Spec)

- Navigation: use existing TopBar, ProfileMenu, and Admin section patterns. Profile page reachable from ProfileMenu. Tenant Settings only visible to TenantAdmin.
- Server-first guards: unauthenticated → `/login`, non-admin → 403 ProblemDetails on admin routes.
- Accessibility: labeled inputs, `aria-describedby` for help text, error summaries, proper `aria-current`/`aria-expanded` in nav, keyboard-friendly dialogs.
- Theming: Light/Dark/AMOLED supported; respect tokens and existing typography.
- Mobile: responsive layout; forms stacked; avatar/logo preview scales to container.

Data model changes

- User (table `app.users`)
  - Add `profile jsonb null` to hold:
    - `name`: { first, last, display }
    - `contact`: { phone?, timezone?, locale? }
    - `social`: { website?, twitter?, facebook?, instagram?, youtube?, linkedin? }
    - `avatar`: { url, key?, width?, height?, mime? }
    - `bio`: { format: "markdown"|"html", content: string } // store markdown preferred; API returns sanitized HTML alongside
    - `guardrails`: {
      denominationAlignment?: string, // free text or preset id label
      favoriteAuthors?: string[],
      favoriteBooks?: string[],
      notes?: string
      }
    - `preferences`: { lessonFormat?: "Engaging"|"Monologue"|"Games"|"Discussion"|"Interactive" }
    - `presets`?: { denomination?: string } // key referencing presets library id/slug
- Tenant (table `app.tenants`)
  - Add `settings jsonb null` to hold:
    - `displayName?: string`
    - `contact?: { email?, website? }`
    - `social?: { twitter?, facebook?, instagram?, youtube?, linkedin? }`
    - `branding?: { logo: { url, key?, width?, height?, mime? } }`
    - `privacy?: { piiHashing: { enabled: boolean } }` // initial toggle for log redaction policy

API contracts (initial)

- User Profile
  - GET `/api/users/me` → 200 { id, email, profile }
  - PUT `/api/users/me` { profilePatch } → 200 { id, email, profile }
  - POST `/api/users/me/avatar` multipart/form-data: file → 200 { avatar: { url, width, height, mime } }
  - POST `/api/users/me/password` { currentPassword, newPassword } → 204 (rate-limited; returns 400 on wrong current)
  - GET `/api/metadata/denominations` → 200 { presets: Array<{ id, name, notes? }> }
- Tenant Settings (current tenant)
  - GET `/api/tenants/settings` → 200 { id, name, settings }
  - PUT `/api/tenants/settings` { settingsPatch } → 200 { id, name, settings }
  - POST `/api/tenants/logo` multipart/form-data: file → 200 { logo: { url, width, height, mime } }

Security & auth

- All routes require auth; tenant settings require `TenantAdmin` policy.
- Password change requires `currentPassword` verification using Argon2id; update `password_hash`, `password_salt`, `password_updated_at`.
- Upload validation: content type whitelist, size limit 2MB; store to object storage (MinIO/S3). In dev, use MinIO with bucket from `.env`. Return signed or public URLs (dev: public), and include key for later deletion/replacement.
- Bio content sanitized server-side; prefer Markdown input with server-rendered sanitized HTML to avoid XSS.
- Logs/metrics redact PII (emails, phones); hashed derivatives used for analytics when needed.

Stories & acceptance criteria

## ✅ DONE UPROF-01 — EF model & migration for profiles

- Add `profile jsonb` to users; add `settings jsonb` to tenants.
- Migration generated with `.Designer.cs`; `make migrate` runs cleanly.
- SnapshotArchitecture updated (schema + examples).

## ✅ DONE UPROF-02 — API: GET/PUT `/api/users/me`

- GET returns current user with `profile` (default empty object).
- PUT accepts partial updates; merges JSON server-side (objects deep-merged; arrays/scalars replace) with normalization (trim strings, validate social URLs; invalid URLs dropped).
- Implementation guards EF tracking (AsNoTracking + Attach, property-level update) and clones JsonNode assignments to avoid parenting exceptions.
- Tests: integration tests cover happy path, deep-merge behavior, and invalid body (400). Full API suite PASS (142/142).

## ✅ DONE UPROF-03 — API: POST `/api/users/me/password`

- Implemented POST `/api/users/me/password` requiring `currentPassword` and `newPassword`.
- Behavior: 204 on success, 400 when current password is invalid, 422 when new password is weak (>=8 chars, contains letter and digit required for MVP).
- Security: Uses Argon2id hasher (`IPasswordHasher`) with per-user salt and optional pepper; updates `PasswordHash`, `PasswordSalt`, and `PasswordUpdatedAt`.
- Persistence: Uses AsNoTracking + Attach and property-level modification to avoid EF double-tracking on record types.
- Telemetry: Traces success/failure without leaking secrets.
- Tests: Integration tests cover success (204), wrong current (400), and weak password (422). Full API suite PASS (145/145).

## ✅ DONE UPROF-04 — API: POST `/api/users/me/avatar`

- Implemented avatar upload endpoint accepting multipart/form-data. Validates content type (png/jpeg/webp) and size (<= 2MB).
- Introduced storage abstraction `IObjectStorageService` with a local filesystem implementation `LocalFileStorageService` (configurable base path). Files are written under `users/{userId}/avatar.*` and served via static files at `/media/*`.
- Program wiring: registers `IObjectStorageService` → `LocalFileStorageService` and maps a `PhysicalFileProvider` for `/media` pointing at `apps/web/web.out/media` by default. Returns a stable, relative URL (e.g., `/media/users/<id>/avatar.png`) and MIME in API response.
- Persistence: updates `profile.avatar` to `{ url, key, mime }`, replacing any existing avatar reference (old files not deleted in MVP).
- Notes: width/height metadata is deferred for now to avoid adding image processing; plan to add lightweight dimension read later.
- Tests: integration tests cover success (PNG under 2MB → 200 with url+mime), unsupported type (415), and too-large payload (413). Targeted and full API suites pass.

## UPROF-05 — Web: `/profile` page (info + social)

- Server-first route; fetches via internal proxy; pre-fills form from `profile`.
- Fields: Display name, First, Last, Phone, Timezone, Locale; Social links; Avatar preview.
- Save performs PUT `/api-proxy/users/me` and updates UI; success toast; errors inline with role=alert.
- Tests: rendering with initial data, submit success, client validation for URL format.

## UPROF-06 — Web: `/profile` guardrails & preferences

- Section: Denomination Alignment (text/select), Favorite Authors (chips), Favorite Books (chips), Notes (textarea), Preferred lesson format (select).
- Save merges into existing profile; preserved on reload.
- Tests: chip add/remove logic; merging behavior; a11y labels present.

## UPROF-07 — Web: Avatar upload

- File input accepts jpg/png/webp; preview prior to upload; upload via `/api-proxy/users/me/avatar`.
- On success, avatar in TopBar/ProfileMenu updates (client cache busting by query param timestamp).
- Tests: mock upload path; verify preview and form reset.

## UPROF-08 — Web: Change password

- Form fields: Current password, New password, Confirm new password; minimal strength meter.
- POST to `/api-proxy/users/me/password`; success toast; on 400 show error under Current password.
- Tests: validation, error mapping, happy path; ensure no logging of secret inputs.

## TEN-01 — API: GET/PUT `/api/tenants/settings` (TenantAdmin)

- GET returns current tenant `settings` (default empty object).
- PUT merges JSON; validates URLs; requires TenantAdmin.
- Tests: role guard enforced; happy path update.

## TEN-02 — API: POST `/api/tenants/logo` (TenantAdmin)

- Upload and store tenant logo in object storage at `tenants/{tenantId}/logo.*`; return url + dimensions (and key).
- Tests: content type/size enforced; success path.

## TEN-03 — Web: `/studio/admin/settings` page

- Server-first; visible only to TenantAdmin; shows Tenant display name, contact email/website, social links, logo upload.
- Save integrates with proxy endpoints; success toast; a11y intact.
- Tests: Admin gating in nav and direct route; form submit success; logo preview + upload.

## TEN-04 — Wire ProfileMenu → `/profile`

- Add Profile link to existing ProfileMenu; ensure focus management and restore behavior preserved.
- Tests: TopBar snapshot updated; link present for all signed-in users.

## UPROF-09 — Object storage integration (MinIO/S3) for avatars/logos

- Implement storage abstraction service in API using `AWSSDK.S3` or `Minio` client.
- Configure `.env`/appsettings: endpoint, region, access keys, bucket. In dev, use MinIO from docker compose.
- Add helper to generate signed URLs (time-limited) or return public URLs in dev.
- Tests: unit test storage service with MinIO test container or local dev; mock for API route tests.

## UPROF-10 — Rich profile bio editor & sanitization

- Web: integrate a lightweight editor (e.g., textarea with Markdown preview, or minimal rich text) consistent with UI tokens.
- API: accept `bio` in profile; sanitize to HTML for read; store Markdown in `profile.bio.content`.
- Tests: XSS injection attempts are stripped; round-trip preserves intended formatting.

## UPROF-11 — Denomination presets library

- Provide a curated JSON list of denomination presets (id, name, notes) under `apps/api/App/Data/denominations.json` or seeded in DB.
- API: GET `/api/metadata/denominations` returns the list.
- Web: Profile guardrails exposes a preset select; selecting a preset sets `presets.denomination` and optionally pre-fills `guardrails.denominationAlignment`.
- Tests: endpoint returns list; UI selection persists in profile.

## UPROF-12 — PII hashing & redaction

- Add a `PIIHasher` utility (SHA-256 + pepper) for hashing emails/phones when emitting metrics/logs.
- Update logging/tracing filters to redact raw PII; include only hashed derivatives when necessary.
- Tests: verification of hashing determinism and redaction in representative log entries.

Perf & a11y tasks

- Avoid large image payloads; cap to 2MB and render 256px thumbnail in UI.
- Run quick a11y audit on forms (landmarks, labels, errors announced).

Documentation tasks

- SnapshotArchitecture: add profile/settings schema, endpoints, and flow diagrams.
- storyLog: add entries per story completion with quality gates.
- LivingChecklist: tick items under IAM/Responsive Web/Storage as they complete.

Risks & mitigations

- Image processing dependency bloat — keep MVP to metadata only; no server-side resize beyond optional dimension read.
- JSON merge complexity — keep server-side merge conservative; replace subtrees when ambiguous.
- Multi-tenant leakage — user profile is tenant-agnostic; tenant settings behind TenantAdmin and tenant-scoped middleware.
- XSS risk in bio — sanitize server-side and render trusted HTML only; prefer Markdown input.
- Credential management for object storage — read from environment; never commit secrets; dev uses MinIO from compose.

Estimate & sequencing

- Week 1: UPROF-01..04 + UPROF-09 (DB + API + storage integration)
- Week 2: UPROF-05..08 + UPROF-10 (User Profile UI + bio)
- Week 3: TEN-01..04 + UPROF-11 + UPROF-12 (Tenant UI + presets + PII hashing)

Dependencies

- Existing DevHeader auth and TenantScope middleware.
- Web internal proxy and toast/confirm patterns already in use on Admin pages.

Exit criteria

- A signed-in user can update their profile and password, see avatar reflected in UI, and a TenantAdmin can update org settings and logo, on desktop and mobile.
