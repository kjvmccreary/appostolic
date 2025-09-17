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
- Denomination presets library available to the profile guardrails UI (multi-select preset picker + freeform alignment auto-fill on first add).
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
  - `presets`?: { denominations?: string[] } // multi-select denomination preset ids
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

## ✅ DONE UPROF-05 — Web: `/profile` page (read-only + edit form)

- Implemented server component route with `fetchFromProxy` (no-store) fetching current user.
- Added `ProfileView` (client) for avatar/email summary and `ProfileEditForm` for personal + social fields (display, first, last, phone, timezone, locale, website, twitter, facebook, instagram, youtube, linkedin).
- Form builds JSON merge patch (only populated sub-objects) and submits PUT `/api-proxy/users/me`; optimistic local state via callback.
- Added accessible labels (`htmlFor`/`id`), alert/status regions, disabled state while saving, minimal URL normalization (auto prepend https for bare domains).
- Added `loading.tsx` skeleton; preserves heading hierarchy and landmark semantics.
- Tests: `ProfileView` (avatar states, email render) and `ProfileEditForm` (success path, failure path) with fetch mocked.
- Deferred to later stories: guardrails/preferences, bio editor, richer validation (international phone, timezone drop-down), toast system integration.

## ✅ DONE UPROF-06 — Web: `/profile` guardrails & preferences

- Added `ProfileGuardrailsForm` client component with denomination alignment, favorite authors/books chip inputs (enter to add, button to remove), notes textarea, and preferred lesson format select (Engaging | Monologue | Games | Discussion | Interactive).
- Integrated into `/profile` page under "Guardrails & Preferences" section with server-fetched initial values.
- Constructed merge patch updating `profile.guardrails` (replacing arrays intentionally) and `profile.preferences.lessonFormat` only when provided.
- Accessibility: labeled inputs, button aria-labels for chip removals, alert/status regions for error/success, disabled submit state.
- Tests: chip add/remove flow; successful submit that includes lesson format in patch (fetch call assertion + success message).
- Deferred: denomination presets integration (UPROF-11), richer validation (duplicate canonicalization, length limits), bio editor (UPROF-10), PII hashing instrumentation (UPROF-12).

## ✅ DONE UPROF-07 — Web: Avatar upload

- File input accepts jpg/png/webp; preview prior to upload; upload via `/api-proxy/users/me/avatar`.
- On success, avatar in TopBar/ProfileMenu updates (client cache busting by query param timestamp).
- Tests: mock upload path; verify preview and form reset.

## ✅ DONE UPROF-08 — Web: Change password

- Added enhanced change password form at `/change-password` using new proxy route `/api-proxy/users/me/password` (aligns with backend user endpoint).
- Fields: current password, new password, confirm new password; client strength meter (length, upper, lower, digit, symbol heuristics) + accessible live region.
- Client validation blocks submit for: weak password (<8 or missing letter/digit), mismatch confirmation.
- Server response mapping: 204 success (form reset + status message), 400 incorrect current, 422 server-deemed weak, 401 unauthorized, generic fallback for others.
- Accessibility: labels with `htmlFor`, status/alert regions, aria-live strength hint, disabled state while submitting.
- Tests: weak password blocked (no fetch call), mismatch confirm blocked, success path triggers fetch to new route, 400 maps to error alert.
- Deferred: Password strength library / zxcvbn integration, password reveal toggle, rate-limit UI messaging.

## ✅ DONE UPROF-09 — (already documented elsewhere) S3/MinIO object storage seam

## ✅ DONE UPROF-10 — Web: Rich profile bio editor

- Added `BioEditor` component to `/profile` page enabling users to author a Markdown bio (stored as markdown; server assumed to sanitize to safe HTML when rendering elsewhere). Textarea includes live character count (max 4000 soft limit) and clear action setting bio to null via merge patch.
- Submission builds minimal JSON merge patch: when empty after clear → `{ profile: { bio: null } }`; otherwise `{ profile: { bio: { format: "markdown", content: "..." } } }`.
- Disabled submit when value unchanged or exceeds char limit; over-limit displays inline alert.
- Accessibility: label, helper/counter `aria-describedby`, error `role="alert"`, success `role="status"`, and proper `aria-invalid` when over limit.
- Tests (`BioEditor.test.tsx`): unchanged disabled, submit new bio (assert minimal fetch body), clear to null, over-limit blocked (no fetch), server error path shows alert.
- Updated `ProfileView` test to use `data-testid="avatar-img"` due to empty alt (`alt=""`) making the image presentational (no implicit role). Component patched to add the test id.
- Coverage: full web suite now 84% lines overall (thresholds maintained). No navigation or guard changes required.
- Deferred: Server-side markdown → sanitized HTML rendering preview on the profile page (future enhancement), toolbar/formatting UI, drag/drop image uploads.

## ✅ DONE UPROF-11 — Denomination presets library

- Added curated JSON list of denomination presets (id, name, notes) at `apps/api/App/Data/denominations.json`.
- API: `GET /api/metadata/denominations` (auth required) returns `{ presets: [...] }`.
- Web: Guardrails form now provides a searchable multi-select; adding first denomination auto-fills alignment if blank; selections stored at `profile.presets.denominations[]` (arrays replace entirely on patch).
- Tests: API integration (401 + 200 shape); Web tests for selection, auto-fill, non-overwrite, chip removal, and patch payload.
- Deferred: preset versioning, tenant overrides, server validation of unknown ids, ETag caching, analytics on selection trends.

## UPROF-12 — PII hashing & redaction

- Add a `PIIHasher` utility (SHA-256 + pepper) for hashing emails/phones when emitting metrics/logs.
- Update logging/tracing filters to redact raw PII; include only hashed derivatives when necessary.
- Tests: verification of hashing determinism and redaction in representative log entries.

### Sub-stories & checklist

- [x] UPROF-12A: Privacy options & configuration (`Privacy:PIIHashPepper`, `Privacy:PIIHashingEnabled`) — Added `PrivacyOptions` bound via `Program.cs`.
- [x] UPROF-12B: Core `IPIIHasher` + `Sha256PIIHasher` (email/phone normalization + peppered hash) — Implemented normalization (email lowercase/trim, phone digits-only) and peppered SHA-256.
- [x] UPROF-12C: `PIIRedactor` (email + phone) replacing/augmenting existing `EmailRedactor` — New unified redactor; legacy `EmailRedactor` marked `[Obsolete]` and delegates.
- [x] UPROF-12D: Logging scope/enricher utilities (adds redacted + hashed fields; respects toggle) — Added `LoggingPIIScope` helpers for email/phone scopes.
- [x] UPROF-12E: Refactor duplicated token/email hash helpers — Centralized PII hashing; token hashing left unchanged (not PII) and documented implicitly by new abstraction.
- [x] UPROF-12F: Integration of redaction & hashing in auth/profile/tenant settings endpoints (no raw emails in logs) — Added `LoggingPIIScope` to user + tenant endpoints; structured scope fields only.
- [x] UPROF-12G: Tests — hasher determinism, pepper variance, redaction edge cases, log capture (no raw PII) — Added unit tests (determinism, pepper variance, normalization + redaction) and integration logging tests for `GET /api/users/me` asserting presence of redacted + (when enabled) hashed fields and absence of raw email; full API suite now 175/175.
- [x] UPROF-12H: Documentation updates (SnapshotArchitecture section, storyLog entry, LivingChecklist tick)
- [x] UPROF-12I: Optional OTEL/metrics enrichment (hashed identifiers only when justified) — can defer if time constrained

Deferred / Post‑1.0 candidates

- ➕ Phone number canonicalization via libphonenumber for international format
- ➕ Automatic span processor for on-the-fly PII scrubbing (cross-cutting) if future services expand
- ➕ Historical log backfill/analyzer to validate absence of PII retroactively

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
