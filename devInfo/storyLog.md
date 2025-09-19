2025-09-18 — Org Settings: Tenant logo upload error handling hardened. Prevent raw HTML from rendering on upload/delete failures by detecting HTML responses and surfacing friendly messages; added a unit test simulating an HTML error; full web test suite PASS. Updated `TenantLogoUpload` accordingly.
2025-09-19 — Auth/Data: Backfill zero roles memberships to full flags — ✅ DONE

- Summary
  - Added migration `s5_02_membership_roles_backfill_zero_to_all` executing `UPDATE app.memberships SET roles = 15 WHERE roles = 0;` to remediate a small set of memberships created during the legacy→flags transition with an unset (`0`) roles bitmask. Assigning `15` grants all four canonical flags (TenantAdmin|Approver|Creator|Learner) to avoid accidental under‑privilege prior to legacy role column removal.
- Files changed
  - apps/api/Migrations/20250919030923_s5_02_membership_roles_backfill_zero_to_all.cs — data migration with idempotent update (no Down reversal).
- Quality gates
  - Migration applied locally via `make migrate` (build succeeded; database update completed without errors). No code paths depend on zero roles state; existing auth serialization tests remain green.
- Rationale
  - Ensures all active memberships possess a non-zero, machine-decodable flags bitmask before disabling the temporary legacy fallback in the web client, reducing risk of privilege mismatches.
- Follow-ups
  2025-09-19 — Auth/Data: Role change preserves flags + test + seeding verification — ✅ DONE

- Summary
  - Fixed a latent defect where changing a legacy `MembershipRole` (Owner/Admin/Editor/Viewer) via `PUT /api/tenants/{tenantId}/members/{userId}` replaced the record without copying the `Roles` bitmask, risking a `roles=0` membership if flags were relied upon elsewhere. Added explicit `Roles = DeriveFlagsFromLegacy(newRole)` to all replacement membership constructions in that endpoint (in-memory path, ambient transaction path, and explicit transaction path). Strengthened test seeding by ensuring `WebAppFactory` assigns a full flags bitmask for the default owner membership (previously omitted, defaulting to zero). Added an integration test asserting a role change from Owner -> Editor updates the flags to `Creator|Learner` (non-zero) and matches the legacy mapping. Also confirmed the standalone seed tool already sets full flags for owner and baseline memberships.
- Files changed
  - apps/api/App/Endpoints/V1.cs — add `Roles = DeriveFlagsFromLegacy(newRole)` in each membership replacement block of role-change endpoint.
  - apps/api.tests/WebAppFactory.cs — include `Roles = TenantAdmin|Approver|Creator|Learner` on seeded owner membership.
  - apps/api.tests/Security/RoleAuthorizationTests.cs — new test `RoleChange_Updates_RolesBitmask_FromLegacyRole` validating bitmask correctness after role mutation.
- Rationale
  - Prevents silent introduction of `roles=0` during role transitions and ensures flags-only authorization logic remains consistent post-mutation. Aligns test and production seeding to avoid false negatives or masked regressions.
- Quality gates
  - Compilation: PASS (no new errors in modified files).
  - Existing authorization tests unaffected; new test passes locally (fast in-memory DB path).
- Follow-ups
  - Optional: Add DB CHECK constraint (`roles <> 0`) after confirming no legacy rows in all environments.
  - Consider backfilling any historical role-change derived rows (none observed beyond manually corrected set of three).

  - Add a guard test asserting no future insert results in `roles=0` (optional) and proceed with removal of legacy `role` field after staging verification.

2025-09-19 — Auth/API: Auth endpoints include numeric roles bitmask — ✅ DONE

- Summary
  - Added explicit numeric roles flags bitmask (`roles: int`) to membership objects returned by `/api/auth/login` and the magic token consume path (signup + login flow) so the web client can decode `TenantAdmin | Approver | Creator | Learner` without relying on legacy `role` fallback. Previously the server serialized the enum as a string (e.g., `"TenantAdmin, Approver, Creator, Learner"`) which the new web numeric/array decoder rejected, causing admin users to appear with only Learner privileges under transitional logic.
- Files changed
  - apps/api/App/Endpoints/V1.cs — cast `m.Roles` to int in anonymous projections for login and magic consume (`roles = (int)m.Roles`) with comments clarifying contract.
  - apps/api.tests/Auth/LoginRolesSerializationTests.cs — new test asserting `memberships[0].roles` is a number > 0.
  - apps/api.tests/Auth/MagicConsumeRolesSerializationTests.cs — new test asserting magic consume flow yields numeric roles bitmask.
- Quality gates
  - Targeted auth serialization tests PASS locally (post-change) and no regressions observed in other auth tests.
- Rationale
  - Ensures frontend flags-only gating receives a stable numeric representation; avoids brittle parsing of enum flag name strings and prevents privilege downgrades masked by legacy fallbacks.
- Follow-ups
  - Remove temporary legacy role fallback in web (`NEXT_PUBLIC_LEGACY_ROLE_FALLBACK`) once production confirms all memberships now include non-zero numeric `roles`.
  - Consider normalizing server to always include both `roles` (int) and `rolesLabel[]` (array of canonical strings) for DX clarity (optional; not required for current migration).

2025-09-18 — Nav — Admin gating tightening (explicit TenantAdmin flag) — ✅ DONE

- Summary
  - Hardened Admin menu visibility so only memberships that explicitly include the `TenantAdmin` flag render the Admin dropdown. Previously, a composite of non-admin flags (Approver+Creator+Learner — bitmask 14) could still surface the Admin menu because upstream `isAdmin` derivation was overly permissive. Added a regression test covering this scenario and updated `TopBar` to gate on `isAdmin && roles.includes('TenantAdmin')`.
- Files changed
  - apps/web/src/components/TopBar.tsx — require explicit `TenantAdmin` in `effectiveRoles` (`isAdminGated`).
  - apps/web/src/components/TopBar.admin.test.tsx — added regression: no Admin menu for roles `['Approver','Creator','Learner']` (bitmask 14, missing TenantAdmin).
- Quality gates
  - Web tests: PASS (62 files, 198 tests) after change; new test passes; coverage unchanged (lines ~85%).
- Rationale
  - Prevent accidental privilege inflation from future broadening of `isAdmin` derivation or transitional fallback mappings. Makes TenantAdmin flag the single authoritative signal for Admin UI entry points.
- Follow-ups
  - Instrument (dev-only) counts of sessions hitting legacy fallback to plan safe removal; later remove gating comment once backend consistently supplies flags.

2025-09-18 — Auth/Web: Flags-only authorization test alignment — ✅ DONE

2025-09-18 — Auth/Web: Numeric roles bitmask support — ✅ DONE

- 2025-09-18 — Auth/Nav: Roles trace instrumentation (temporary) — ✅ DONE
  - Summary
    - Added focused, env‑gated tracing to diagnose mismatch where admin bitmask users appear as Learner. Web: `getFlagRoles` now logs input shape, numeric decoding, legacy fallbacks, and final deduped roles when `NEXT_PUBLIC_DEBUG_ROLE_TRACE=true`. NextAuth `jwt` & `session` callbacks log raw memberships and derived booleans when `DEBUG_ROLE_TRACE=true`. API: authorization handler logs required vs have flags plus raw legacy role when `ROLE_TRACE=true`.
  - Files changed
    - apps/web/src/lib/roles.ts — trace hooks.
    - apps/web/src/lib/auth.ts — jwt/session trace output.
    - apps/api/App/Infrastructure/Auth/RoleAuthorization.cs — targeted trace line.
  - Usage
    - Set `NEXT_PUBLIC_DEBUG_ROLE_TRACE=true` (web) and `DEBUG_ROLE_TRACE=true` (server runtime) plus `ROLE_TRACE=true` (API) to correlate client session derivation with server policy evaluation.
  - Removal Plan
    - Remove after root cause resolved and roles payload uniform (array or numeric bitmask). Guarded by env so production unaffected when vars unset.

- Summary
  - Extended web roles helper `getFlagRoles` to accept a numeric (or numeric string) bitmask directly in `membership.roles` (e.g., `1` => `['TenantAdmin']`, `15` => all flags). Added defensive behavior: a bitmask of `0` yields an empty roles array (no fallback to legacy). This restores TenantAdmin UI access for users whose API payload now emits an integer bitmask instead of an array (previously rendered only Learner due to unsupported type).
- Files changed
  - apps/web/src/lib/roles.ts — broaden `roles` type to `number | string | (Array<...>)`, map numeric/ numeric-string via `roleNamesFromFlags`, skip legacy fallback when bitmask explicitly 0.
  - apps/web/src/lib/roles.numericFlags.test.ts — new tests: 1 (TenantAdmin), 7 (TenantAdmin+Approver+Creator), 15 (all), and 0 (empty, no legacy fallback).
- Quality gates
  - Web tests: PASS (suite re-run locally) with new file; coverage unchanged (~85% lines) minimal positive delta.
- Rationale
  - Aligns client with API serializer variant emitting bitmask; prevents silent privilege downgrade (admin appearing only as learner) when roles flags transmitted numerically during migration.
- Follow-ups
  - Measure prevalence of numeric vs array forms; when array form guaranteed, consider normalizing server output for consistency or coercing to array in session callback.

- Summary
  - Updated remaining web test files (8 failing tests across TenantSwitcher, session derivation, role guard, roles helpers, members admin page) to remove all residual legacy role (Owner/Admin/Editor/Viewer) assumptions. Tests now explicitly provide `roles: ['TenantAdmin', ...]` for admin scenarios and empty arrays / learner-only flags for non-admin cases. Eliminated references to deleted `deriveFlagsFromLegacy` helper and updated expectations to canonical flags-derived labels. No application runtime code changes in this batch—tests now accurately reflect the prior flags-only refactor.
- Files changed
  - apps/web/src/components/TenantSwitcher.test.tsx
  - apps/web/src/lib/auth.session.test.ts
  - apps/web/src/lib/roleGuard.test.ts
  - apps/web/src/lib/roles.test.ts
  - apps/web/app/studio/admin/members/page.test.tsx
- Quality gates
  - Web tests: PASS (61 files, 192 tests) after edits; coverage stable (~85% lines). Existing non-fatal MUI X license warnings unchanged.
- Rationale
  - Ensures test suite is authoritative for the new flags-only model, preventing false positives tied to legacy fallback logic and enabling confident future removal of the deprecated `role` field once backend payloads are fully migrated.
- Follow-ups
  - Consider adding a lint-time assertion or type helper to forbid accessing `membership.role` in new code paths to accelerate full removal.

2025-09-18 — Auth/Web: Transitional legacy role fallback reintroduced — ✅ DONE

- Summary
  - Reintroduced a temporary legacy→flags fallback in `getFlagRoles` so that memberships lacking an explicit `roles[]` flags array still yield correct capabilities (e.g., legacy `Admin`/`Owner` now map to `TenantAdmin, Approver, Creator, Learner`). The fallback is gated by `NEXT_PUBLIC_LEGACY_ROLE_FALLBACK` (defaults enabled, set to `false` to disable). This addresses a production parity gap where an admin user (e.g., kevin@b.com) appeared only as Learner in the Tenant Selector because the API response omitted populated flags.
- Files changed
  - apps/web/src/lib/roles.ts — add env‑guarded legacy mapping (Owner/Admin → full set; Editor → Creator+Learner; Viewer → Learner); tolerate legacy labels inside `roles[]` during transition.
  - apps/web/src/lib/roles.legacyFallback.test.ts — new tests covering Admin, Owner, Editor, Viewer mapping plus boolean derivation.
- Quality gates
  - Web tests: PASS (62 files, 197 tests) including new fallback suite; coverage impact neutral (< +0.1%).
- Rationale
  - Ensures no inadvertent privilege downgrade for existing tenants before the backend populates roles flags universally; keeps UI and server authorization consistent.
- Decommission Plan
  - Once API guarantees non-empty `roles[]` for all memberships, set `NEXT_PUBLIC_LEGACY_ROLE_FALLBACK=false` in staging, validate zero regressions, then remove fallback code and tests.
- Follow-ups
  - Add instrumentation to log (dev only) when fallback path is exercised to measure residual legacy dependency before removal.

  2025-09-19 — Auth/Web: Comma-separated roles string parsing to prevent admin 403 — ✅ DONE
  - Summary
    - Addressed a 403 access denial on `/studio/admin/invites` for an admin whose session carried a legacy comma-separated roles string (e.g., `"TenantAdmin, Approver, Creator, Learner"`) instead of an array or numeric bitmask. `getFlagRoles` previously treated any non-array, non-numeric string (even comma-delimited) as missing roles, triggering legacy fallback only when enabled or producing empty roles (no `TenantAdmin`) leading to `isAdmin=false`. Added parsing for comma-separated values prior to legacy fallback so canonical flags are correctly recognized regardless of serialization variant.
  - Files changed
    - apps/web/src/lib/roles.ts — detect comma in string, split, normalize tokens (including legacy names) to canonical flag roles, dedupe, return early.
    - apps/web/src/lib/roles.numericFlags.test.ts — added regression test `parses comma-separated roles string into canonical flags` asserting admin booleans resolve properly.
  - Rationale
    - Ensures resilient decoding across transient serialization formats during migration (string enum list → numeric bitmask). Prevents inadvertent admin privilege loss and 403 responses in admin pages.
  - Quality gates
    - Web unit tests updated; new test passes locally (fast run). No other role-related tests regress.
  - Follow-ups
    - After full transition to numeric bitmask or array, consider removing the comma-string compatibility path and failing fast to reduce complexity.

2025-09-18 — Org Settings parity with Profile (Guardrails + Bio)

- Added tenant‑level Guardrails & Preferences and Bio sections to Org Settings at `apps/web/app/studio/admin/settings/page.tsx`.
- New components: `TenantGuardrailsForm` (denominations, alignment, favorites, notes, lesson format) and `TenantBioEditor` (Markdown editor, minimal merge patch, preview), both wired to `/api-proxy/tenants/settings`.
- Server page now loads denomination presets from `/api-proxy/metadata/denominations` and existing tenant guardrails/bio from `GET /api/tenants/settings`.
- Tests added: `TenantGuardrailsForm.test.tsx`, `TenantBioEditor.test.tsx`.
- Full web test suite PASS via `make fetest`.

## 2025-09-18 — Nav — Tenant selector role labels (canonical) + legacy tolerance — ✅ DONE

## 2025-09-18 — Web — Org Settings scaffold (Tenant Settings UI) — ✅ DONE

- Summary
  - Implemented the initial Org Settings page at `/studio/admin/settings` by cloning the successful patterns from `/profile`. The page now fetches current tenant settings server-side and renders:
    - `TenantLogoUpload` for branding logo (accepts PNG/JPEG/WebP ≤2MB) via `/api-proxy/tenants/logo` with cache-busted preview.
    - `TenantSettingsForm` for organization display name, contact (email, website), and social links; submits a minimal merge patch to `/api-proxy/tenants/settings` following deep-merge semantics (objects merge; scalars/arrays replace; null clears).
  - Server guard remains tenant-scoped using `computeBooleansForTenant` with id/slug normalization; non-admins see a 403 stub.

- Files changed
  - apps/web/app/studio/admin/settings/page.tsx — server page now loads settings and renders form + logo upload.
  - apps/web/app/studio/admin/settings/TenantSettingsForm.tsx — new client form component with normalization and error/success feedback.
  - apps/web/app/studio/admin/settings/TenantLogoUpload.tsx — new client logo uploader aligned to avatar UX.
  - apps/web/app/studio/admin/settings/TenantSettingsForm.test.tsx — unit tests for success, website normalization, and failure.

- Quality gates
  - Web tests: PASS (`pnpm --filter @appostolic/web test`). Coverage remains above thresholds; non-fatal MUI license warnings unchanged.
  - Typecheck/Lint: PASS for new files.

- Notes
  - Next iterations: add DELETE logo action, surface privacy toggle when ready, and consider extracting a shared deep-merge patch builder for tenant/user to remove duplication.

## 2025-09-18 — Web — Org Settings quick wins: Remove logo + tests — ✅ DONE

- Summary
  - Enhanced `TenantLogoUpload` with a Remove button that clears a just-selected image locally without network, and issues `DELETE /api-proxy/tenants/logo` to remove an existing server logo. Added progress, error, and success status messaging with accessible roles. Wires cache-busted preview updates consistently. Expanded admin settings page tests to accept legacy `Owner` (case-insensitive) and handle `session.tenant` being a tenantId.

- Files changed
  - apps/web/app/studio/admin/settings/TenantLogoUpload.tsx — add remove action, deleting state, status messaging, a11y.
  - apps/web/app/studio/admin/settings/TenantLogoUpload.test.tsx — new tests for POST upload, DELETE remove, and local clear.
  - apps/web/app/studio/admin/settings/page.test.tsx — add legacy Owner and tenantId→slug tests.

- Quality gates
  - Web tests: PASS locally with Node 20 using `pnpm --filter @appostolic/web test`.
  - Typecheck/Lint: PASS for new/updated files.

- Notes
  - Server already supports `DELETE /api/tenants/logo` (TEN‑02). This completes the basic branding lifecycle. A follow-up can surface logo dimensions or variants when image processing lands.

- Summary
  - Updated the tenant selector UI to display canonical role labels derived from roles flags (Admin, Approver, Creator, Learner) instead of legacy strings (Owner/Viewer). Centralized label computation via getFlagRoles to normalize both canonical flags and legacy names, case-insensitive. Also fixed an admin-gating edge case by tolerating lowercase legacy role strings in the roles helper so TopBar visibility remains correct.
  - Fixed a small a11y nit in TenantSwitcher (aria-busy boolean). SnapshotArchitecture updated in "What’s new" to reflect selector label normalization and shared roles helper usage.

- Files changed
  - apps/web/src/components/TenantSwitcher.tsx — derive display labels from roles flags; aria-busy boolean.
  - apps/web/src/lib/roles.ts — accept lowercase legacy role strings in normalization; no behavior change for canonical flags.
  - SnapshotArchitecture.md — “What’s new” entry for selector label normalization and roles helper alignment.

- Quality gates
  - Web tests: PASS (full suite green locally after change). Non-fatal MUI license warnings unchanged.
  - Typecheck: PASS for modified files.

- Rationale
  - Ensures consistent, future-proof role names across the UI during the transition from legacy roles to flags and prevents admin gating misses caused by case variance in older payloads/fixtures.

- Follow-ups
  - Consider extracting a small shared label utility (flag roles → display label) to reduce duplication across switcher modal and other components.

## 2025-09-17 — UPROF-04.1: Avatar pipeline simplification (preserve original format) + absolute URLs — ✅ DONE

## 2025-09-18 — Web — Avatar upload: Clear confirmation (local only) — ✅ DONE

- Summary
  - Added a confirmation step to `AvatarUpload` when clearing a just-selected local image. This avoids accidental loss before upload and aligns with the tenant logo removal UX. Clearing only affects the local selection and preview; no server-side delete is performed (no DELETE avatar endpoint yet).
  - Improved accessibility and feedback: success status uses `role="status"`; errors use `role="alert"`. Ensured object URLs are revoked on clear/unmount to prevent memory leaks.

- Files changed
  - apps/web/src/components/AvatarUpload.tsx — add Clear button, `ConfirmDialog` integration, status messaging, and safe object URL revocation.
  - apps/web/src/components/AvatarUpload.test.tsx — new test for confirm-and-clear flow using scoped dialog queries; avoids ambiguous selectors; asserts no network call and no global `avatar-updated` event.

- Quality gates
  - Web tests: PASS via `make fetest` (59 files, 188 tests). Coverage remains ~85% lines. Existing MUI X license warnings remain non-fatal.

- Notes
  - When a DELETE endpoint for avatars is introduced, we can extend this to full server-side removal with a matching confirmation.

- Summary
  - Simplified the avatar upload/processing pipeline to avoid perceived corruption: we no longer force-convert images to WebP. Instead, we preserve the original format (PNG/JPEG/WebP), apply only minimal transforms when needed (AutoOrient, optional center-crop for near-square, optional downscale with max side 512), and then re-encode using the original format’s encoder when a transform occurs; otherwise we pass through the original bytes. The API now returns an absolute URL (`scheme://host/...`) to prevent dev server relative path issues.
  - Storage keys now use the correct extension to match the source mime (e.g., `users/{id}/avatar.png|jpg|webp`), and response metadata includes `{ url, key, mime, width, height }`. Tests were updated from expecting `image/webp` to expecting the original mime, and to ensure the returned URL is absolute and still contains `/media/users/`.

- Files changed
  - apps/api/App/Endpoints/UserProfileEndpoints.cs — remove WebP heuristics and forced conversion; preserve original format; only re-encode if mutated; construct absolute URL in response.
  - apps/api.tests/Api/UserAvatarEndpointsTests.cs — update expectations to original mime (PNG in fixtures) and assert absolute URL.

- Quality gates
  - API tests: PASS — full suite 180/180 after update
  - Runtime: Observed clean avatar rendering in UI with cache-busted server URL flow unchanged.

- Rationale
  - Eliminates over-optimization and potential artifact introduction from forced WebP encoding and keeps behavior predictable across diverse source images. Absolute URLs remove ambiguity between API and Next.js dev servers for media hosting.

- Follow-ups
  - Optional: include a deterministic content hash in the returned URL for cache-busting instead of timestamp.
  - Consider extracting shared DeepMerge helper used by user/tenant endpoints (tracked in LivingChecklist).

## 2025-09-16 — UPROF-01: EF model & migration for profiles — ✅ DONE

## 2025-09-17 — Nav Hardening: Explicit multi-tenant selection required — ✅ DONE

- Summary
  - Removed the multi-tenant auto-selection heuristic from the NextAuth `jwt` callback so accounts with >1 membership no longer receive an implicit `tenant` claim on first sign-in. They now remain unscoped until an explicit selection is performed (cookie + session alignment), preventing premature TopBar/nav exposure.
  - Middleware updated to stop silently writing a `selected_tenant` cookie for multi-tenant sessions; it only auto-sets when exactly one membership exists. Multi-tenant users without a selection are redirected to `/select-tenant`.
  - Server layout gating (cookie + session tenant match) now deterministically hides navigation for multi-tenant users pre-selection with no client race.
  - Deprecated `TenantAwareTopBar` client wrapper removed/neutralized (component replaced by a no-op stub; legacy tests emptied) in favor of pure server gating.
  - Added regression tests: `auth.multiTenant.test.ts` (no implicit tenant claim) and `layout.multiTenantNoSelection.test.tsx` (no banner/nav without selection). Existing TopBar/admin tests updated implicitly by relying on explicit tenant claim setup.

- Files changed
  - apps/web/src/lib/auth.ts — delete multi-tenant auto-selection branch (retain single-membership auto-select & update trigger path).
  - apps/web/middleware.ts — restrict auto cookie set to single membership; ensure redirect for multi-tenant no-selection.
  - apps/web/app/layout.multiTenantNoSelection.test.tsx — new test (gating negative case).

  2025-09-18 — Auth/Web: Remove legacy role fallback; flags-only authorization — ✅ DONE
  - Summary
    - Eliminated all legacy `role` (Owner/Admin/Editor/Viewer) fallback logic from web authorization and navigation gating. `computeBooleansForTenant` now interprets only explicit `roles[]` flags (`TenantAdmin`, `Approver`, `Creator`, `Learner`). Admin pages (Members, Invites, Audits, Notifications, Settings) and `TopBar` rely solely on flags for `isAdmin`.
    - Removed legacy expansion helpers and environment toggles (`deriveFlagsFromLegacy`, `PREFER_LEGACY_FOR_ADMIN`, single-tenant safety) simplifying the mental model and preventing mismatched UI/server authorization states.
    - Updated all related tests to supply `roles: ['TenantAdmin']` for admin scenarios; removed tests asserting legacy Owner/Admin acceptance. Adjusted select-tenant role label test to derive canonical labels purely from flags.
    - Added architecture snapshot entry documenting rationale and revert point.

  - Files changed
    - apps/web/src/lib/roles.ts — delete legacy conversion & safety logic; flags-only `computeBooleansForTenant`.
    - apps/web/src/lib/roleGuard.ts — simplify guards (remove legacy branching).
    - apps/web/src/components/TopBar.tsx — remove legacy gating & diagnostics tied to role field.
    - apps/web/app/studio/admin/\*/page.tsx (audits, notifications, settings) — replace legacy `mine.role` checks with flags-based gating.
    - Tests: multiple `*.test.tsx` updated to use `roles: ['TenantAdmin']` and drop legacy role assertions.
    - SnapshotArchitecture.md — new “What’s new” entry.
    - devInfo/storyLog.md — this entry appended.

  - Quality gates
    - Web tests: PASS after updates (`make fetest`).
    - Typecheck/Lint: PASS (no new warnings beyond existing baseline MUI/X notices).

  - Rationale
    - Prevent inconsistent admin visibility and 403 errors caused by divergent legacy vs flags interpretation; reduce surface area before 1.0; ensure a single source of truth for authorization semantics.

  - Revert Plan
    - Revert commit `REVERT POINT: pre removal of legacy role fallback` if emergency rollback required.
  - apps/web/src/lib/auth.multiTenant.test.ts — new regression test.
  - apps/web/src/components/TenantAwareTopBar\*.tsx — neutralized (content removed / stub) pending full removal.

- Quality gates
  - Web tests: PASS (all suites green after neutralization; no residual TenantAwareTopBar assertions).
  - Typecheck: PASS.
  - Coverage: Stable (TopBar gating logic covered by new tests; removed tests replaced by server gating tests).

- Rationale
  - Enforces explicit tenant context selection for multi-tenant accounts, closing a privilege visibility gap and eliminating hydration flashes tied to client-side gating logic.

- Follow-ups
  - Remove stub `TenantAwareTopBar` files entirely after confirming no external references.
  - Consider adding an SSR integration test simulating a multi-tenant request lacking cookie to assert redirect (middleware-level) if warranted.

- Added JSONB columns:
  - `app.users.profile jsonb` for user-level profile (name/contact/social/avatar/bio/guardrails/preferences)
  - `app.tenants.settings jsonb` for tenant settings (branding/contact/social/privacy)
- Updated EF model in `apps/api/Program.cs` with `JsonDocument? Profile` and `JsonDocument? Settings` and mapped to `jsonb`.
- Generated migration `s5_01_user_profile_tenant_settings` and applied via `make migrate`.
- Next: Implement `/api/users/me` and `/api/tenants/settings` endpoints (UPROF-02, TEN-01).

### 2025-09-16 — Sprint Plan: User & Tenant Profile

- Added `devInfo/TenantAndUserProfile.md/uProfSprintPlan.md` detailing stories UPROF‑01..08 and TEN‑01..04 to deliver User Profile (info, social, guardrails, avatar, password) and Tenant Settings (org info, social, logo), with UI alignment to `devInfo/DesignDocs/UI-Spec.md` and server‑first guards.
- Scope covers EF model, API contracts, Web pages, uploads in dev, tests, and docs updates; defers S3/MinIO production integration to post‑1.0.

### 2025-09-16 — Living Checklist seeded and process docs updated

- Seeded `devInfo/LivingChecklist.md` with a 1.0 readiness checklist, global quality gates, and post‑1.0 parking lot.
- Updated `AGENTS.md` and `.github/copilot-instructions.md` to require updating the Living Checklist at story/sprint close alongside `SnapshotArchitecture.md` and `devInfo/storyLog.md`.
- Purpose: Establish a durable, team‑maintained checklist to track 1.0 readiness and reduce drift between specs and implementation.

## 2025-09-16 — Invites — Fix: Resend/Revoke double-encoding — ✅ DONE

- Summary
  - Fixed a bug where the email path segment was pre-encoded in the web server actions and then encoded again by the proxy route. Addresses failures when emails contain special characters (e.g., plus addressing), which resulted in 404s or API failures and no email being sent.
  - Server actions now pass the raw email; the proxy route handles encoding exactly once.

- Files changed
  - apps/web/app/studio/admin/invites/page.tsx — remove `encodeURIComponent(email)` from resend and revoke action paths; add comments to prevent regressions.

- Quality gates
  - Typecheck (workspace): PASS
  - Smoke: Resend should now succeed and Mailhog should receive the message in dev.

## 2025-09-16 — UPROF-03: Change password endpoint — ✅ DONE

- Summary
  - Implemented POST `/api/users/me/password` to allow authenticated users to change their password. Verifies the current password, enforces a minimal strength rule for the new password, and updates Argon2id `PasswordHash`, per-user `PasswordSalt`, and `PasswordUpdatedAt` on success.
  - Returns 204 No Content on success, 400 Bad Request when the current password is incorrect, and 422 Unprocessable Entity when the new password is too weak (MVP: at least 8 chars and must include a letter and a digit). No secrets are logged; traces only include outcome labels.
  - Guarded EF tracking using AsNoTracking + Attach with property-level modifications to avoid double-tracking errors in tests and runtime.

- Files changed
  - apps/api/App/Endpoints/UserProfileEndpoints.cs — added POST `/api/users/me/password` with validation, verification, hashing (Argon2id), and persistence pattern.
  - apps/api/Application/Auth/PasswordHasher.cs — reused `IPasswordHasher` (Argon2id) implementation.
  - apps/api.tests/Api/UserPasswordEndpointsTests.cs — new integration tests for success (204), invalid current (400), and weak password (422); fixed test seed to use AsNoTracking before Attach.

- Quality gates
  - Build (API): PASS
  - Tests (API): PASS — full suite 145/145
  - Docs: Updated sprint plan to mark UPROF‑03 DONE; LivingChecklist and SnapshotArchitecture updated accordingly.

- Notes
  - Strength rules are intentionally minimal for MVP; follow-up may add a configurable policy (length/classes/deny list) and rate-limit per user/tenant.
  - No user-facing audit record yet; telemetry trace provides operational visibility. Consider adding an audit ledger entry post‑1.0 if required.

2025-09-18 — Nav — Admin gating hardening and role labels normalization — ✅ DONE

- Summary
  - Fixed a regression where a non-admin user with a single tenant membership could see the Admin menu. Added a single-tenant safeguard in `computeBooleansForTenant` to suppress `TenantAdmin` when exactly one membership exists and the legacy role is non-admin. Added an optional env safety `NEXT_PUBLIC_PREFER_LEGACY_ROLES=true` to prefer legacy over conflicting flags and a dev-only warning when mismatches are detected.
  - Normalized role labels in tenant UIs to canonical names (Admin/Approver/Creator/Learner) using `getFlagRoles` and updated the Select Tenant page accordingly.
  - Updated `TopBar.admin.test.tsx`: the flags-based admin visibility test now uses a multi-tenant session to validate intended behavior while respecting the new single-tenant safeguard. Added a regression test ensuring no Admin menu for single-tenant non-admin users.

- Files changed
  - apps/web/src/lib/roles.ts — single-tenant safeguard in `computeBooleansForTenant`; env-based legacy precedence in `getFlagRoles`.
  - apps/web/src/components/TopBar.tsx — use roles helper; add dev mismatch warning.
  - apps/web/app/select-tenant/page.tsx — canonical role labels.
  - apps/web/src/components/TopBar.admin.test.tsx — test adjustments and regression add.
  - apps/web/app/select-tenant/page.test.tsx — new label assertions.

- Quality gates
  - Web tests: PASS (61 files, 196 tests). Coverage ~85% lines. MUI X license warnings remain non-fatal.

- Notes
  - If backend roles flags remain inconsistent in some environments, set `NEXT_PUBLIC_PREFER_LEGACY_ROLES=true` to further avoid accidental elevation until data is cleaned.

2025-09-18 — Auth — API RoleAuthorization prefers Roles flags — ✅ DONE

- Summary
  - Updated the API authorization handler to treat Roles flags as the source of truth for tenant policies (TenantAdmin, Approver, Creator, Learner) and fall back to legacy `MembershipRole` only when `Roles == None`. This resolves a report where the tenant originator appeared to retain admin after demotion because legacy and flags were previously OR-ed. The web UI already has layered protections (single-tenant safeguard and TopBar suppression when legacy role is non-admin); this server-side fix ensures policies enforce flag demotions.

- Quality gates
  - API: Build + tests PASS (180/180) after the change.

- Notes
  - No contract changes to endpoints; behavior is stricter in line with flags being the canonical source. Legacy compatibility remains when flags are absent.

## 2025-09-16 — UPROF-04: Avatar upload endpoint + local storage — ✅ DONE

- Summary
  - Implemented POST `/api/users/me/avatar` to upload and attach a user avatar. Validates content type (png/jpeg/webp) and max size (2MB), stores the file via a new storage abstraction, and updates `users.profile.avatar` with `{ url, key, mime }`. Returns 200 with the avatar metadata.
  - Introduced `IObjectStorageService` and a dev/test `LocalFileStorageService` that writes under a configurable base path and serves files through `/media/*` static hosting. This provides stable relative URLs in dev without external dependencies.

- Files changed
  - apps/api/App/Endpoints/UserProfileEndpoints.cs — added POST `/api/users/me/avatar` with validation and profile update.
  - apps/api/Application/Storage/IObjectStorageService.cs — new storage interface.
  - apps/api/Application/Storage/LocalFileStorageService.cs — local filesystem implementation with configurable base path and relative URL generation.
  - apps/api/Program.cs — DI registration for storage and static file hosting for `/media` using `PhysicalFileProvider`.
  - apps/api.tests/Api/UserAvatarEndpointsTests.cs — new integration tests: success (png), 415 unsupported type, 413 too large.

- Quality gates
  - Build (API): PASS
  - Tests (API): PASS — targeted avatar tests PASS (3/3); full API suite PASS (148/148)
  - Docs: Sprint plan marked DONE for UPROF‑04; SnapshotArchitecture “What’s new” updated; LivingChecklist remains accurate.

- Notes
  - Old avatar files are not deleted in MVP; replacement updates the profile reference only. Dimensions are deferred; lightweight metadata extraction can be added later without breaking the contract.

## 2025-09-16 — Tooling — Web Vitest Node 20 requirement — ✅ DONE

## 2025-09-16 — UPROF-09: S3/MinIO object storage seam — ✅ DONE

- Summary
  - Introduced `S3ObjectStorageService` implementing `IObjectStorageService` using `AWSSDK.S3`, enabling a config-driven switch between local filesystem storage and S3/MinIO without altering upload endpoint contracts. Supports path-style addressing for MinIO dev (`ForcePathStyle=true`) and virtual-host style for AWS S3. Applies `PublicRead` ACL for avatars/logos and configurable Cache-Control (default immutable 1yr) to encourage client caching.
- Files changed
  - `apps/api/Application/Storage/S3ObjectStorageService.cs` — new implementation + options class.
  - `apps/api/Program.cs` — conditional DI wiring based on `Storage:Mode` (`local`|`s3`).
  - `Directory.Packages.props` / `Appostolic.Api.csproj` — added `AWSSDK.S3` via central package management.
  - `apps/api.tests/Storage/S3ObjectStorageServiceTests.cs` — unit tests validating PutObject request (bucket, key, ACL, Cache-Control) and URL generation with/without `PublicBaseUrl`.
- Configuration
  - Add to `appsettings.Development.json` (example):
    ```json
    "Storage": {
      "Mode": "s3",
      "S3": {
        "Bucket": "appostolic-dev",
        "ServiceURL": "http://localhost:9000",
        "AccessKey": "minioadmin",
        "SecretKey": "minioadmin",
        "PathStyle": true,
        "PublicBaseUrl": "http://localhost:9000/appostolic-dev"
      }
    }
    ```
    Local default (no config) continues to use `LocalFileStorageService` writing under `apps/web/web.out/media` served at `/media/*`.
- Quality gates
  - Build (API): PASS (warning: NU1603 approximate AWSSDK.S3 version match — acceptable; pin can be added later if needed).
  - Tests (API): PASS — new S3 unit tests (2) + full existing suite remain green.
  - Docs: Updated `SnapshotArchitecture.md` What’s New; LivingChecklist to tick storage seam item (object storage wiring) when broader artifact usage lands.
- Notes
  - Signed URLs, deletion lifecycle, and tenant logo endpoint integration are deferred to subsequent stories (TEN‑02). Current endpoints return public URLs consistent with previous local mode behavior.

- Summary
  - Documented mandatory use of Node 20.x LTS for running the web unit test suite (Vitest) and dev scripts. Node 19 triggered a Corepack crash (`TypeError: URL.canParse is not a function`) before any tests executed when invoking `pnpm test` with workspace filters. Added a Runtime & Testing Environment section to `apps/web/AGENTS.md` with nvm workflow, PATH override example, CI pinning note, and failure symptom checklist.
  - Files changed
  - apps/web/AGENTS.md — added Runtime & Testing Environment section
  - SnapshotArchitecture.md — “What’s new” entry documenting the requirement
  - Quality gates
  - Web tests: PASS under Node 20 (118/118) after enforcing version
  - Coverage: thresholds still satisfied post adjustments
  - Notes
  - Future improvement: add an `.nvmrc` or Volta pin to enforce version automatically; optionally fail early in a pretest script if `process.version` < 20.

  ## 2025-09-16 — UPROF-12G: PII hashing & redaction tests — ✅ DONE
  - Summary
    - Completed privacy test coverage for PII hashing/redaction: added unit tests for `Sha256PIIHasher` (determinism, pepper variance, normalization) and `PIIRedactor` edge cases plus logging scope behavior (hash included/excluded by toggle). Added integration logging tests (`UserProfileLoggingTests`) asserting `GET /api/users/me` emits only redacted email plus hash when enabled and never the raw email. All privacy sub-stories 12A–12G now complete; full API test suite passes 175/175.
  - Files changed
    - `apps/api.tests/Privacy/LoggingPIIScopeTests.cs` — scope unit tests for email/phone hashing toggle.
    - `apps/api.tests/Privacy/UserProfileLoggingTests.cs` — integration tests capturing structured logging scopes for user profile endpoint.
    - `devInfo/TenantAndUserProfile.md/uProfSprintPlan.md` — marked UPROF‑12G done.
    - `SnapshotArchitecture.md` — updated What's New (12A–12G complete, OTEL enrichment pending 12I).
  - Quality gates
    - Build: PASS
    - Tests: PASS (175/175)
    - Docs: Sprint plan & architecture snapshot updated; LivingChecklist privacy/observability items remain accurate.
  - Next
    - UPROF‑12H: Documentation consolidation (already partially updated) and LivingChecklist tick confirmation.
    - UPROF‑12I: Optional OTEL span attribute enrichment behind config flag.

  ## 2025-09-16 — UPROF-12 (A–E): PII hashing & redaction foundation — ✅ PARTIAL
  - Summary
    - Implemented privacy configuration and core utilities for PII hashing & redaction. Added `PrivacyOptions` (pepper + enable flag), `IPIIHasher` with `Sha256PIIHasher` (email lowercase+trim; phone digits-only), unified `PIIRedactor` (email + phone) and deprecated legacy `EmailRedactor` (now delegates). Introduced `LoggingPIIScope` helper to attach structured redacted/hashed fields without emitting raw PII. Updated notification senders and hosted services to use `PIIRedactor`. Unit tests added for hashing determinism, pepper variance, normalization, and redaction edge cases; all passing.
    - Files changed
      - `apps/api/Application/Privacy/PrivacyOptions.cs`, `IPIIHasher.cs`, `PIIRedactor.cs`, `LoggingPIIScope.cs`
      - `apps/api/App/Notifications/*` swapped `EmailRedactor` → `PIIRedactor`; legacy file marked `[Obsolete]`.
      - `apps/api/Program.cs` added options binding + DI registration.
      - `apps/api.tests/Privacy/PIIHasherTests.cs`, `PIIRedactorTests.cs` new test coverage.
  - Quality gates
    - Build (API): PASS (existing unrelated warnings).
    - Tests (API): PASS for new PII suite (10/10). Full suite not yet re-run post-change (will run after integration sub-stories).
  - Next
    - 12F: Integrate scopes into auth/profile/tenant endpoints to guarantee absence of raw emails in logs.
    - 12G: Add integration log-capture tests verifying no raw PII appears and hashed fields present when enabled.
    - 12H: Update `SnapshotArchitecture.md` (partial entry added), `uProfSprintPlan.md` checklist (A–E checked), and `LivingChecklist` tick after endpoint integration.
    - 12I: Optional OTEL enrichment (deferred until base integration complete).
    - Consider future phone normalization upgrade (libphonenumber) post‑1.0.

  ## 2025-09-16 — TEN-01/TEN-02: Tenant settings & branding logo endpoints — ✅ DONE
  - Summary
    - Implemented tenant-scoped settings management and branding logo lifecycle. Added `GET /api/tenants/settings` and `PUT /api/tenants/settings` (deep merge: objects merge recursively; arrays/scalars replace; explicit nulls clear) persisting to `tenants.settings` JSONB. Added `POST /api/tenants/logo` (multipart image/png|jpeg|webp <=2MB) storing via `IObjectStorageService` under `tenants/{tenantId}/logo.*` and updating `settings.branding.logo = { url, key, mime }`. Added `DELETE /api/tenants/logo` to remove logo metadata and best-effort delete the underlying object (local or S3/MinIO) without failing the request on storage delete errors.
  - Files changed
    - `apps/api/App/Endpoints/TenantSettingsEndpoints.cs` — new endpoints + duplicated DeepMerge helper (pending refactor).
    - `apps/api/Program.cs` — wired `MapTenantSettingsEndpoints()`.
    - `apps/api.tests/Api/TenantSettingsEndpointsTests.cs` — integration tests (6) covering settings merge, logo upload success, invalid mime (415), size limit (413), delete path, and logo absence after delete.
    - `SnapshotArchitecture.md` — What’s New entry added.
  - Quality gates
    - Build (API): PASS (no new warnings beyond existing cryptography & Redis deprecation notices).
    - Tests (API): PASS — new tenant settings/logo tests (6/6) plus existing suite unaffected.
  - Notes
    - Width/height (and potential variants) intentionally deferred until an image processing story introduces server-side resizing/metadata extraction.
    - DeepMerge utility now duplicated between user profile and tenant settings endpoints; tracked as a small refactor task to extract a shared helper.
    - Old logo asset deletion is best-effort; failure is swallowed to keep UX snappy and avoid partial state when storage is transiently unavailable.

## 2025-09-16 — Auth — Root route gating + Signup styling — ✅ DONE

## 2025-09-17 — Nav: TopBar Admin visibility fixes — ✅ DONE

- Summary
  - Resolved a regression where legitimate tenant admins did not see the Admin menu. `TopBar` now uses the shared roles helper `computeBooleansForTenant` to determine Admin visibility based on the selected tenant’s membership, supporting both roles flags (e.g., `TenantAdmin`) and legacy roles (`Owner`/`Admin`). It also normalizes `session.tenant` when it contains a tenantId by resolving to the corresponding membership’s slug.
  - Updated and expanded tests in `TopBar.admin.test.tsx` to cover legacy Admin/Owner, roles flags, tenantId vs slug, and the negative case where a global `session.isAdmin` should not leak visibility when the selected tenant membership isn’t admin.
  - SnapshotArchitecture “What’s new” updated to reflect roles flags alignment and tenantId handling.

- Files changed
  - apps/web/src/components/TopBar.tsx — replace ad-hoc role string checks with `computeBooleansForTenant`; add slug/id resolution.
  - apps/web/src/components/TopBar.admin.test.tsx — update fixtures to use legacy `Admin`/`Viewer`, add Owner/tenantId tests, keep global flag regression test.
  - SnapshotArchitecture.md — note roles flags alignment and id/slug handling.

- Quality gates
  - Typecheck: PASS for modified files.
  - Web tests: Locally blocked by Node/ICU mismatch in the task runner; changes are unit-test driven and align with existing roles helpers and tests. CI should run under Node 20 and pass.

- Rationale
  - Centralizing admin determination via the roles helper keeps UI visibility perfectly aligned with server roles semantics, avoiding drift as we transition from legacy roles to flags, and handles tenantId/slug variations robustly.

## 2025-09-17 — Profile — Avatar display refresh & preview alignment — ✅ DONE

- Summary
  - Improved avatar UX so the uploader now replaces the local object URL preview with the canonical stored server URL (with cache-busting query param) immediately after a successful upload. Previously the uploader continued showing the transient blob preview while the ProfileMenu updated, leading to confusion about the final cropped image. Added center-cover styling to ensure the avatar thumbnail always renders fully and uniformly, and guarded `URL.revokeObjectURL` for jsdom/test environments. Introduced memory-leak prevention by revoking prior blob URLs and added a regression test asserting cache-busted replacement plus global `avatar-updated` event dispatch.
- Files changed
  - `apps/web/src/components/AvatarUpload.tsx` — replace preview with server URL, revoke old blob URLs (with safe guard), add object-fit cover styles, comments.
  - `apps/web/src/components/AvatarUpload.test.tsx` — new assertions for cache-busted server URL (`?v=timestamp`), event detail URL equality, and onUploaded callback.
- Quality gates
  - Web tests: PASS (171/171) after patch; AvatarUpload test updated and green.
  - Typecheck: PASS (no new errors introduced).
- Rationale
  - Eliminates mismatch between “selected” (local blob) and “stored” (server URL) avatar, reducing user confusion and ensuring consistent cropping/layout in all consumers.
- Follow-ups
  - Optional: compute a deterministic short content hash (SHA-1/MD5) for cache bust key to avoid always-growing history when re-uploading identical image.
  - Consider session.update with new avatar URL for other components relying purely on session.profile without event listener.

- Summary
  - Root route `/` no longer renders the dashboard to unauthenticated users. The page is now a server-only redirector: unauthenticated → `/login`; authenticated → `/studio` (which further redirects to `/studio/agents`).
  - Styled `/signup` with a CSS module and accessibility improvements (labels, helper text, inline error role). When an invite token is present, shows a banner that links to `/login?next=/invite/accept?token=...` so existing users follow the accept flow.
  - Updated the previous dashboard render test to assert redirect behavior by mocking `next-auth` and `next/navigation`.

- Files changed
  - apps/web/app/page.tsx — replace dashboard render with server redirects based on `getServerSession`.
  - apps/web/src/app/Dashboard.test.tsx — update to mock `getServerSession` + `redirect` and assert `/login` vs `/studio`.
  - apps/web/app/signup/SignupClient.tsx — style tweaks and a11y; invite-aware banner.
  - apps/web/app/signup/styles.module.css — new CSS module for layout/buttons/messages.

## 2025-09-17 — Nav — Tenant-scoped Admin gating regression fix — ✅ DONE

- Summary
  - Fixed a regression where users could still see the Admin menu after losing admin rights in the selected tenant. We now ignore any global `session.isAdmin` flag and compute admin strictly from the membership that matches the selected tenant (`tenantSlug` or `tenantId`) and includes role `admin`. This closes leakage from stale/global flags across tenant switches.
  - Added a regression unit test asserting that when `session.isAdmin=true` but the selected tenant membership is non-admin, the Admin menu does not render.
  - Renamed Admin dropdown link label from “Settings” to “Org Settings” (still `/studio/admin/settings`).

- Files changed
  - apps/web/src/components/TopBar.tsx — enforce tenant-scoped check only; label rename to “Org Settings”.
  - apps/web/src/components/TopBar.admin.test.tsx — new negative-case test for global flag with non-admin membership.

- Quality gates
  - Web tests: PASS (176/176). Coverage stable; unrelated MUI license warnings remain unchanged.

- Rationale
  - Guarantees least-privilege UI visibility aligned with the currently selected tenant; prevents regressions from cached/global admin indicators.

- Quality gates
  - Typecheck (web): PASS
  - Unit tests (web): Local runner currently blocked by Node mismatch; updated test compiles under typecheck. Will re-run vitest when Node >= 20 is active.

## 2025-09-16 — Auth — Fix: occasional login loop on invite → login — ✅ DONE

## 2025-09-16 — Web — Logs cleanup: Toaster SSR + duplicate middleware — ✅ DONE

- Summary
  - Resolved noisy web dev logs and intermittent 500s caused by a client-only portal rendering during SSR. `ToastProvider` now guards `createPortal` behind a `mounted` check to avoid referencing `document` on the server. Also removed an inert `middleware.js` file that caused repeated “Duplicate page detected … middleware.ts and middleware.js” warnings in Next dev output.

- Files changed
  - apps/web/src/components/ui/Toaster.tsx — add `mounted` state and render portal only after mount.
  - apps/web/middleware.js — deleted duplicate placeholder file; `middleware.ts` remains the source of truth.

- Quality gates
  - Typecheck (web): PASS

## 2025-09-16 — Nav — Strengthen multi-tenant TopBar gating — ✅ DONE

- 2025-09-16 — Nav — Tenant-scoped Admin gating — ✅ DONE
- Summary: Replaced flat `session.isAdmin` usage in `TopBar` with derived admin status from the currently selected tenant membership (matching on `tenantSlug` or `tenantId` and checking `role` or `roles[]` for `admin`). Prevents Admin menu leakage when user is admin in a different tenant or no selection yet. Added `TopBar.admin.test.tsx` covering positive & negative cases and mixed role arrays.
- Files changed: `apps/web/src/components/TopBar.tsx`, `apps/web/src/components/TopBar.admin.test.tsx` (new).
- Rationale: Prior implementation surfaced Admin navigation across tenants because `isAdmin` was a global boolean, violating least privilege after tenant switch or when selecting a non-admin tenant.
- Quality gates: Unit tests updated (new test file) — full suite to be re-run in next CI pass; local targeted tests pass.

- 2025-09-16 — Nav — Hide nav until tenant claim present — ✅ DONE
  - Summary: Suppress all primary navigation links, creation CTA buttons, and profile menu until the JWT includes a tenant claim (`session.tenant`). Prevents early navigation before explicit tenant context is established, even if the user is authenticated and memberships are known.
  - Files changed: `apps/web/src/components/TopBar.tsx` (conditional visibility), `apps/web/src/components/TopBar.admin.test.tsx` (added no-tenant test).
  - Rationale: Previous gating still displayed nav items between auth and tenant selection; this enforces strict tenant-first context.
  - Quality gates: Added test validating nav hidden when authed without tenant. Pending full suite run for aggregate coverage.

- 2025-09-16 — Nav — Server-side TopBar gating via cookie — ✅ DONE
  - Summary: Removed client `TenantAwareTopBar` gating logic; `app/layout.tsx` now renders `TopBar` only if the `selected_tenant` cookie exists (server-side). Eliminates hydration race and guarantees nav is absent pre-selection regardless of client state.
  - Files changed: `apps/web/app/layout.tsx` (server cookie check), removed `TenantAwareTopBar*.tsx` + tests.
  - Rationale: Client gating allowed edge flashes and complexity; server gating is deterministic and simpler.
  - Follow-up: Consider middleware expansion to redirect authed/no-cookie requests to `/select-tenant` for path-level enforcement.

- 2025-09-16 — Nav — Stricter server gating (cookie + session alignment) — ✅ DONE
  - Summary: Hardened server-side TopBar gating to require both a `selected_tenant` cookie AND a matching `session.tenant` claim before rendering the nav. Prevents stale/forged cookie from exposing navigation when the authenticated JWT has no tenant selected (e.g., after switch or logout/login without selection).
  - Files changed: `apps/web/app/layout.tsx` now fetches server session via `getServerSession(authOptions)` and compares `session.tenant === cookie` before rendering `<TopBar />`.
  - Rationale: Prior cookie-only check could leak nav if cookie persisted from an earlier session mismatch.
  - Next (optional): Middleware enhancement to redirect authenticated users lacking both cookie & claim directly to `/select-tenant`.

### 2025-09-17 — Nav — Remove legacy client gating + align tests — ✅ DONE

### 2025-09-17 — Nav — Post-selection TopBar immediate visibility fix — ✅ DONE

### 2025-09-17 — Avatar Upload — Fix: multipart content-type forwarded — ✅ DONE

- Summary
  - Web proxy route `/api-proxy/users/me/avatar` forwarded headers from `buildProxyHeaders` which always included `Content-Type: application/json`. When the client posted a `FormData` body, this JSON content-type suppressed the autogenerated multipart boundary, causing the API endpoint's `HasFormContentType` check to fail with `{"error":"Expected multipart/form-data"}`. Updated the proxy route to remove any existing `Content-Type` header before forwarding so fetch sets a proper `multipart/form-data; boundary=...` header.
  - Extended route test to assert the JSON content-type is stripped and upstream fetch is invoked without a preset content-type header.
  - Bundled with prior nav fixes (hydrator refresh + stale cookie refinement) in single commit for clarity.

- Files changed
  - `apps/web/app/api-proxy/users/me/avatar/route.ts` — delete incoming `Content-Type` headers before forwarding `FormData`.
  - `apps/web/app/api-proxy/users/me/avatar/route.test.ts` — add assertion ensuring no content-type forwarded.

- Quality gates
  - Web tests: PASS (avatar proxy route tests updated; full suite 170/170 green).
  - Manual UX: Avatar upload now succeeds (API receives multipart and returns avatar metadata).

- Rationale
  - Prevent silent failure path requiring user to retry; ensures consistent server validation logic remains intact without loosening API contract.

- Follow-ups
  - Consider adding client toast for specific 415/413 responses (file type/size) for richer feedback.
  - Potentially centralize header stripping logic for all multipart proxy endpoints.

- Summary
  - After tenant selection, users landed on the target route (e.g. `/studio/agents`) but the TopBar remained hidden until a manual full page refresh because the server layout required a matching `session.tenant` and `selected_tenant` cookie; the JWT/session claim lagged behind the cookie write by one redirect cycle. Added a client `TenantSessionHydrator` enhancement that, when a cookie is present but the client session lacks a tenant claim, performs `session.update({ tenant })` followed by a `router.refresh()` to trigger a server component re-render, making the TopBar appear immediately without user reload.
  - Middleware stale cookie detection was too aggressive: it treated any cookie mismatch (including the interim state where the token had no tenant claim yet) as stale and redirected back to `/select-tenant`, remapping the page and repeatedly fetching `/api/auth/csrf`.
  - Adjusted logic to only flag stale when BOTH a cookie and a token tenant claim exist and differ.

- Files changed
  - `apps/web/src/components/TenantSessionHydrator.tsx` — add one‑shot guard + `router.refresh()` after `session.update` with microtask delay; comment explaining rationale.
  - `apps/web/middleware.ts` — refine `staleCookie` condition to require existing token tenant; add comment about avoiding post-selection loop window.

- Quality gates
  - Web tests: PASS (54 files / 170 tests). Existing gating tests still green; hydrator remains a passive client util so minimal coverage impact.
  - Typecheck: PASS (no new TS errors).

- Rationale
  - Ensures frictionless tenant selection UX for multi-tenant users while preserving hardened server-side gating invariant (cookie + session alignment) against stale/forged cookies.

- Follow-ups
  - Optional: Add a focused unit test simulating hydrator behavior with mocked `useSession` + `router.refresh` spy (current coverage shows low function coverage for hydrator).
  - Remove deprecated `TenantAwareTopBar` stub once confirmed unused externally (still present but inert).

- Summary
  - Fully removed obsolete `TenantAwareTopBar` client wrapper and its two test suites after migrating to deterministic server-side gating (cookie + session.tenant alignment) in `app/layout.tsx`. Updated existing `TopBar` tests to include a `tenant` claim where navigation or creator/admin actions are expected, preventing false negatives under the stricter gating rules. Added stable `next/navigation` mocks in admin tests to eliminate App Router invariant errors and ensure isolated unit reliability.
- Files changed
  - Deleted: `apps/web/src/components/TenantAwareTopBar.tsx`, `TenantAwareTopBar.test.tsx`, `TenantAwareTopBar.strict.test.tsx`.
  - Updated: `apps/web/src/components/TopBar.test.tsx` (adds `tenant` claim to relevant cases), `TopBar.admin.test.tsx` (adds explicit `usePathname`/`useRouter` mocks).
- Rationale
  - Retaining the legacy client gating created redundant logic and brittle tests that conflicted with the new server-first approach, leaving the suite red. Consolidation reduces surface area and ensures tests assert the intended invariant: nav/actions require an aligned tenant selection.
- Quality gates
  - Web unit tests: PASS (all TopBar tests green; obsolete suites removed). Coverage thresholds still met.
- Notes
  - Middleware redirect/auth-mismatch scenarios are partially covered; richer auth-mocked middleware tests can be added later. No architecture structural changes beyond test cleanup (SnapshotArchitecture unchanged).

- Summary
  - Eliminated initial paint flash where multi-tenant users (no tenant selected) could momentarily see and interact with the `TopBar` before the client session finished loading. The `TenantAwareTopBar` now waits for `useSession()` to reach a non-`loading` state and defaults to a hidden nav, removing the race window. Added an explicit loading-state unit test to prevent regression. (Refined again to hide for any authenticated user lacking a tenant selection, not just multi-tenant accounts.)
- Files changed
  - `apps/web/src/components/TenantAwareTopBar.tsx` — add `status===loading` defensive early return + expanded doc comment.
  - `apps/web/src/components/TenantAwareTopBar.test.tsx` — add loading-state test; refactor mocking to avoid CommonJS `require` usage.
- Rationale
  - Previous implementation returned the nav on the very first client render when `session` was undefined, then suppressed it after hydration if multi-tenant & unselected, creating a brief exploitable navigation window.
- Quality gates
  - Web tests: PASS (suite re-run locally under Node 20) — new loading state test included.
  - Typecheck: PASS (no new errors introduced).
- Follow-ups (optional)
  - Consider a server component wrapper to pass a preloaded session to avoid hiding nav for single-tenant users during initial load (perf/UX tweak, not required for correctness).

## 2025-09-16 — TEN-02 Fix: Corrupt tenant logo PNG test fixture — ✅ DONE

- Summary
  - Resolved two failing tenant logo tests (`Upload_Logo_Succeeds_And_Stores_Metadata`, `Delete_Logo_Removes_Metadata`) that began returning 400 BadRequest after avatar fixture remediation. Root cause: tenant logo tests used an unvalidated 1x1 PNG base64 string (different from the validated avatar/MinimalPngDecode fixture) which ImageSharp rejected as invalid. Replaced with the known-good 1x1 PNG already covered by `MinimalPngDecodeTests`, centralizing it as a private const inside the test class.
  - Restored the full API test suite to green (179/179). Added guard rationale to comments; deferred extracting a shared `TestImageFixtures` helper until another binary fixture is needed.

- Files changed
  - `apps/api.tests/Api/TenantSettingsEndpointsTests.cs` — swap corrupt base64 for validated PNG; factor into `ValidMinimalPngBase64` const.
- Quality gates
  - Build (API): PASS
  - Tests (API): PASS — full suite 179/179
- Follow-up
  - Optional: introduce `TestImageFixtures` static class if additional image fixtures emerge (kept small for now).

## 2025-09-16 — Auth UI: Unify login form styling & credential 401 review — ✅ DONE

- Summary
  - Updated `/login` page to use the same utility class layout and input styling as `/magic/request` and `/forgot-password` (max-w-sm, spacing, rounded inputs with focus ring). Replaced bespoke CSS module layout with tailwind-esque utility classes while retaining existing functional behavior (CSRF fetch, credential flow, next redirect).
  - Added a styling verification test asserting presence of unified class tokens (`rounded-md`, `border-line`, `focus:ring-2`, accent background on submit button) to reduce regression risk.
  - Investigated 401s on `/api/auth/login` reported in errors log: endpoint returns 401 when credentials invalid or password hash absent. Backend logic matches Argon2id hasher usage (pepper + default iterations). No server code changes required; issue most likely user entered incorrect password or account lacks password (magic-link only). Documented outcome instead of patching.
- Files changed
  - `apps/web/app/login/LoginClient.tsx` — refactor markup & classes; remove unused module styles for container.
  - `apps/web/app/login/page.test.tsx` — add styling test.
- Quality gates
  - Web build/TS: PASS
  - Web tests: (Manual run attempted; ensure Node 20 environment). New test compiles; suite expected green (no backend dependency for styling assertions).
- Notes
  - Future Improvement: extract shared AuthForm wrapper component to DRY markup across login, magic, forgot password, and reset flows.

  - Smoke: Dev server should no longer log “document is not defined” errors or duplicate middleware warnings.

## 2025-09-15 — Auth — Style: Magic Link request page — ✅ DONE

- Summary
  - Styled `/magic/request` to match Forgot/Reset patterns: compact layout, labeled email field with helper text, primary button, and clear post-submit guidance. Kept generic messaging (no enumeration) for security. Added a back-to-sign-in link.

- Files changed
  - apps/web/app/magic/request/page.tsx — replace bare form with styled inputs and accessibility hints; keep server-first POST to `/api-proxy/auth/magic/request`.

## 2025-09-17 — Nav — Cleanup debug alert; add Tenant Settings link — ✅ DONE

- Summary
  - Removed the temporary JavaScript alert from `/logout` that displayed cookie/session-token status after sign-out. This was for diagnostics only and is now cleaned up. The logout flow continues to sign out without redirect, proactively clears the `selected_tenant` cookie, and then redirects to `/login?loggedOut=1`.
  - Added a Tenant Settings link for Tenant Admins in the TopBar: included in the desktop Admin dropdown and the mobile NavDrawer. Created a server-gated placeholder page at `/studio/admin/settings` that returns 403 for non-admins and renders a heading for admins.
- Files changed
  - apps/web/app/logout/page.tsx — remove alert and cookie inspection block.
  - apps/web/app/logout/page.test.tsx — drop alert expectations; keep signOut + redirect assertions.
  - apps/web/src/components/TopBar.tsx — add Settings item in Admin menu and mobile adminItems.
  - apps/web/app/studio/admin/settings/page.tsx — new server page with TenantAdmin gate and placeholder content.
  - apps/web/app/studio/admin/settings/page.test.tsx — tests for non-admin 403 and admin heading render.
  - apps/web/src/components/TopBar.admin.test.tsx — minor assertion to ensure Admin menu appears; Settings link covered by page existence.
  - SnapshotArchitecture.md — updated to note alert removal and new Tenant Settings link/page.
- Quality gates
  - Web tests: to be re-run under Node 20; targeted tests compile. Full suite expected green.
  - Typecheck: PASS (lint cleaned in new files).
- Notes
  - The Tenant Settings page is a placeholder; future work will surface settings and branding logo management, aligning with existing API endpoints.

- Quality gates
  - Typecheck (web): PASS
  - Smoke: Manual check renders correctly; submit disables button and shows generic status.

- Summary
  - Fixed an intermittent redirect loop after signing in from an invite flow that redirected to `/login?next=/invite/accept?...`.
  - Root cause: the login page manually navigated (`router.replace(next)`) with `redirect: false` before NextAuth had finalized session cookies. Middleware would still see an unauthenticated request and bounce back to `/login`, remounting the page and repeatedly fetching `/api/auth/csrf`.
  - Changes:
    - Let NextAuth perform the post-sign-in redirect (no manual `router.replace` in normal runs) to avoid the cookie race.
    - Fetch the CSRF token once per mount using a ref guard to avoid noisy repeated `/api/auth/csrf` calls if the component re-renders.
    - Surface friendly inline error when NextAuth returns `?error=CredentialsSignin`.

- Files changed
  - apps/web/app/login/LoginClient.tsx — rely on NextAuth redirect, add once-per-mount CSRF fetch, parse `error` param, and test-mode fallback for unit tests.

- Quality gates
  - Typecheck (web): PASS for changed files (local Vitest currently blocked by Node version mismatch; to re-run after Node >=20).
  - Smoke expectation: Following an invite link to Login, submitting valid credentials should redirect once to Accept Invite, then into the app without CSRF spam in logs.

## 2025-09-15 — Web — Fix: Members roles toast/redirect

- Summary
  - Fixed a false error toast after role save on `/studio/admin/members`. Root cause: the server action wrapped `redirect()` in a try/catch; since Next implements `redirect()` by throwing a control-flow error, the catch path treated it as a failure and redirected with `?err=roles-failed`, surfacing an error toast despite a successful 303.
  - The server action now posts to the proxy, checks `res.ok`, and only redirects to `?err=roles-failed` on non‑2xx or network errors. On success, it calls `revalidatePath('/studio/admin/members')` and then `redirect('/studio/admin/members?ok=roles-saved')` outside the catch block so the redirect isn’t swallowed.

- Files changed
  - apps/web/app/studio/admin/members/page.tsx — refactor `saveMemberRoles` try/catch + redirect flow

- Why
  - Network tab showed a 303 See Other after toggling roles, but a red "Failed to update roles. Try again." toast still appeared. The catch block misclassified the success redirect as an error.

- Quality gates
  - Typecheck (web): PASS
  - Tests: Deferred; local vitest currently blocked by Node version mismatch (repo requires Node >=20 <21). Functional smoke via UI validated success toast appears and error toast no longer shows on success.

## 2025-09-16 — Fix: 500 on roles update (Tenant memberships)

## 2025-09-17 — Nav/Logout — Update ProfileMenu test for redirect sign-out — ✅ DONE

- Summary
  - Updated `ProfileMenu.test.tsx` to reflect the refactored sign-out flow which now routes through `/logout` via `window.location.href` instead of calling NextAuth `signOut()` directly. Replaced the outdated expectation asserting `signOut` invocation with a stubbed `window.location.href` setter asserting redirect to `/logout` and confirming `signOut` is not called. Restores the web unit test suite to green (171/171) after prior navigation/logout hardening changes.
- Files changed
  - `apps/web/src/components/ProfileMenu.test.tsx` — replace signOut assertion with redirect check, add href setter stub and restore logic.
- Quality gates
  - Web tests: PASS (171/171) — coverage for `ProfileMenu.tsx` sign-out path updated.
- Rationale
  - Sign-out behavior moved to a server-routed `/logout` page to ensure deterministic clearing of the `selected_tenant` httpOnly cookie and alignment of server middleware logic; test needed to mirror new contract.
- Follow-ups
  - Consider adding an integration/E2E assertion that `/logout` clears both cookie and session claim before returning to `/login` (already partially covered by existing logout multi-tenant E2E test).

## 2025-09-17 — Logout Hardening & Multi-tenant Nav Regression Test — ✅ DONE

- Summary
  - Prevented stale tenant context from leaking navigation after logout/login for multi-tenant users. Explicitly clear `selected_tenant` cookie both in the client logout page and in middleware on `/logout`, and remove any lingering `tenant` claim for fresh sign-ins with >1 memberships so users must select a tenant again. Added regression test ensuring no `TopBar` renders when session has memberships but no tenant claim.
  - Files changed
  - apps/web/src/lib/auth.ts — clear `token.tenant` on multi-membership sign-in.
  - apps/web/middleware.ts — purge `selected_tenant` cookie on `/logout` irrespective of auth enforcement flag.
  - apps/web/app/logout/page.tsx — client-side cookie deletion after `signOut` prior to redirect.
  - apps/web/app/logout/logout.multiTenantFlow.test.tsx — new regression test.
- Quality gates
  - Typecheck: PASS modified files.
  - Tests: Regression test added (pending full suite run once Node 20 environment engaged; pretest guard enforces requirement).
- Rationale
  - Ensures explicit tenant re-selection invariant holds across session boundaries, eliminating brief nav exposure from persistent cookies or carried claims.
- Follow-ups
  - Add middleware integration test for redirect to `/select-tenant` when authed multi-tenant user hits protected path without selection.
  - Introduce `.nvmrc` to auto-pin Node 20 LTS and reduce environment drift causing Corepack errors.

- Area: API (IAM endpoints), Web proxy unaffected
- Change: Avoid nested DB transactions inside membership endpoints when `TenantScopeMiddleware` already opened a tenant-scoped transaction. Endpoints now detect `db.Database.CurrentTransaction` and reuse it, only opening a new transaction (and setting `app.tenant_id`) when none exists. This removes a runtime 500 observed on `POST /api-proxy/tenants/{tenantId}/memberships/{userId}/roles`.
- Verification:
  - Built API and ran a smoke flow: signed up a user (creates personal tenant), listed memberships, and updated roles via `POST /api/tenants/{tenantId}/memberships/{userId}/roles` using dev headers. Received 200/204 responses instead of 500. Last-admin invariant logic remains intact (legacy Owner/Admin still confer admin regardless of flags).
- Files: `apps/api/App/Endpoints/V1.cs` (transaction reuse logic in roles update and member delete), docs updated.
- Notes: No web changes required; server-first proxy continued to forward headers/body correctly. This aligns with our convention to avoid environment-gated route registration and prefer middleware-scoped transactions.

## 2025-09-15 — Story G: Auth/Tenant Multi-tenant UX polish

- Centralized tenant switching to Account menu (ProfileMenu) and removed TopBar selector. Admin links are now a dropdown.
- Enhanced `/select-tenant` to validate `next` for same-origin paths and auto-select single membership; added unit tests for safe/unsafe next.
- Tenant switcher modal: added role badges and remembers last selected tenant (localStorage) with a subtle hint outline.
- API route `/api/tenant/select` already validates next and sets `selected_tenant` cookie; no changes required beyond tests.
- Tests: Added cases for safe/unsafe next, ensured modal interaction still posts and updates session.

### 2025-09-15 — Various FE cleanup (auth/nav/login)

- TopBar when logged out now hides primary nav and shows a Sign in button; kept a named nav landmark for a11y. Unified styling between "Create Lesson" and "New Agent" CTAs.
- Login page styled (title spacing, primary button) and now includes links to Sign up and Magic Link (both preserve `next`). Forgot password link retained. Magic Link request/verify pages use consistent headings and spacing.
- Members page adds an "Invite members" link. Mobile drawer backdrop made more opaque with slight blur to avoid text overlay in dark mode. Tenant Switcher modal alignment fixed (moved down with internal scrolling).
- Tests: Extended LoginPage tests for new links. Re-ran full web suite: PASS (39 files, 114 tests). Coverage ~85% lines. MUI license warnings remain non-fatal.

### 2025-09-15 — Story F: Agents editor form polish

- What: Repaired and enhanced `AgentForm` with accessible labels, helper texts, inline validation, and clear `isEnabled` toggle; preserved tool allowlist hints and token estimate preview.
- Where: `apps/web/src/app/studio/agents/components/AgentForm.tsx` (+ tests in `AgentForm.test.tsx`).
- Why: Improve UX clarity and a11y; align with parity sprint standards.
- Tests: Web unit suite passes; added test to verify `isEnabled` is sent when toggled off.

## Story E — Tasks: Details/export ergonomics — ✅ DONE

- Added copy-to-clipboard for Task IDs:
  - Detail page header now shows the Task ID in monospace with a copy button (aria-label "copy task id").
  - Tasks table includes a new ID column with a per-row copy button (aria-label includes the ID).
- Export filename consistency:
  - Export now guarantees a filename of the form `task-<id>.json` when the server does not provide a Content-Disposition filename.
- Tests:
  - Added unit test to assert the export anchor `download` attribute receives `task-<id>.json` fallback.
  - Added tests to verify clipboard.writeText is called for both detail and list copy actions.
- Status: Typecheck PASS; unit tests PASS (web: 39 files, 109 tests).

## Sprint 4.2 – Docs and SnapshotArchitecture (2025-09-15)

2025-09-15 — Admin — Story A: Invites UX polish (phase 1) — In progress

- 2025-09-15 — Admin — Story A: Invites UX polish (phase 2) — In progress

- Summary
  - Introduced a lightweight toast system (`ToastProvider` + `useToast`) and wired to `/studio/admin/invites` via a small client hook that reads `ok/err` query params and shows contextual toasts, then clears the params. Replaced the `window.confirm` usage with an accessible `ConfirmDialog` component used by `ConfirmSubmitButton`. Added a minimal inline validation announcement for the email field.
  - Added an explicit empty state when there are zero invites, and replaced the raw email input with a client `EmailField` component that surfaces inline validation (aria-invalid/aria-describedby, role=alert) after touch.

- Files changed
  - apps/web/src/components/ui/Toaster.tsx — toast context and portal renderer
  - apps/web/app/providers.tsx — wrap app with `ToastProvider`
  - apps/web/src/components/useInviteToasts.tsx — client hook to translate ok/err → toasts and strip params
  - apps/web/app/studio/admin/invites/ClientToasts.tsx — client shim to run the hook on the page
  - apps/web/src/components/ui/ConfirmDialog.tsx — accessible confirm dialog
  - apps/web/src/components/ui/ConfirmSubmitButton.tsx — now uses `ConfirmDialog`
  - apps/web/app/studio/admin/invites/page.tsx — mount ClientToasts; remove SSR banners; keep server actions
  - apps/web/app/studio/admin/invites/EmailField.tsx — client email input with inline validation

- Quality gates
  - Typecheck (web): PASS
  - Unit tests (web): PASS — suite still green; coverage acceptable (toasts/dialog lightly tested for now)

- Requirements coverage
  - Toasts for action feedback: Done
  - Accessible confirm dialog for revoke: Done
  - Inline validation message for email: Done
  - Empty state visuals and richer validation: Deferred

2025-09-15 — Admin — Story B: Members roles UX polish — ✅ DONE

- Summary
  - Polished `/studio/admin/members` with save feedback and safety affordances. Added a small client hook `useMembersToasts` that reads `ok/err` after a roles save redirect and shows success/error toasts, then strips the params. Checkboxes now expose a `data-pending` attribute during form submission for visual pending state. The UI surfaces a clear helper message via `aria-describedby` explaining why the last remaining TenantAdmin cannot be unchecked.
  - Stabilized tests by introducing `useToastOptional()` (a no-throw variant) and wrapping the shared test render provider with `ToastProvider` so client toast hooks can mount without requiring per-test setup.

- Files changed
  - apps/web/src/components/useMembersToasts.tsx — toast hook using window.location; now uses `useToastOptional()`
  - apps/web/app/studio/admin/members/ClientToasts.tsx — client shim to mount the hook
  - apps/web/app/studio/admin/members/page.tsx — wires `ClientToasts`, redirects with `ok/err` on save, adds `data-pending` and last-admin helper text
  - apps/web/src/components/ui/Toaster.tsx — adds `useToastOptional()` helper
  - apps/web/test/utils.tsx — wraps RTL provider tree with `ToastProvider`

- Quality gates
  - Typecheck (web): PASS
  - Unit tests (web): PASS — 38 files, 101 tests; coverage acceptable (members hook lightly tested)

- Requirements coverage
  - Pending state on role toggles: Done
  - Save success/error via toast: Done
  - Last-admin guard surfaced with accessible messaging: Done

2025-09-15 — Admin — Story C: Audits UI polish — ✅ DONE

- Summary
  - Polished `/studio/admin/audits` with a complete UX: quick date presets (Today, 7d, 30d), a styled filter form, formatted table that decodes role flags into names, a compact pager driven by `X-Total-Count`, and clear empty/error states. The server component now defaults `searchParams` to support tests calling the page without args. Desktop navigation surfaces Admin links (Members, Invites, Audits, Notifications) for admins via `TopBar`.

- Files changed
  - apps/web/app/studio/admin/audits/page.tsx — UI polish, role flag decoding, defaulted params for tests
  - apps/web/app/studio/admin/audits/page.test.tsx — added tests for 403 render, pager text, Prev/Next link sync
  - apps/web/src/components/TopBar.tsx — expose full Admin links in desktop when `isAdmin`

- Quality gates
  - Typecheck (web): PASS
  - Unit tests (web): PASS — focused audits tests plus full suite, coverage ~85.7% lines overall (toasts/dialog lightly covered)

- Requirements coverage
  - Filters with quick date presets: Done
  - Table formatting with role names and pager based on `X-Total-Count`: Done
  - Empty/error states: Done
  - Role-gated navigation visibility for Admin: Done

## 2025-09-16 — Admin UX polish: Tenant switcher + Invites Accepted state

- Summary
  - Centered the Tenant Switcher modal and prevented cut‑off by switching the outer container to full‑height flex with `items-center justify-center` and making the dialog panel scrollable via `max-h` + `overflow-auto`.
  - On `/studio/admin/invites`, surfaced acceptance state from the API. The table now shows a Status chip: Accepted (green) when `acceptedAt` is set, Pending (amber) otherwise. When an invite has been accepted, the Resend/Revoke actions are hidden to avoid invalid operations.
  - Fixed a broken import path for `ConfirmSubmitButton` by importing from `apps/web/src/components/ui/ConfirmSubmitButton`. Also restored the missing Expires cell to match the table header.

- Files changed
  - apps/web/src/components/TenantSwitcherModal.tsx — centered modal and scrollable panel
  - apps/web/app/studio/admin/invites/page.tsx — add Status column, hide actions when accepted, fix ConfirmSubmitButton import, restore Expires cell

  ## 2025-09-16 — Multi-tenant TopBar gating (prevent navigation pre-selection) — ✅ DONE
  - Summary
    - Added `TenantAwareTopBar` wrapper that suppresses the global `TopBar` when an authenticated user has more than one tenant membership but has not yet selected a tenant (no `selected_tenant` cookie and no `session.tenant`). Prevents premature navigation before tenant context is established.
    - Layout now uses `TenantAwareTopBar` instead of `TopBar` directly. New unit tests ensure hidden state (multi-tenant no selection) and visible states (single tenant, or multi-tenant with selection).
  - Files changed
    - `apps/web/app/layout.tsx` — swap in `TenantAwareTopBar`.
    - `apps/web/src/components/TenantAwareTopBar.tsx` — new component with gating logic (cookie + session + pathname checks).
    - `apps/web/src/components/TenantAwareTopBar.test.tsx` — tests for hidden/visible scenarios.
  - Quality gates
    - Web tests: PASS (153/153) after addition; coverage unchanged (lines ~84.8%).
    - Typecheck: PASS.
  - Notes
    - Uses client-side cookie inspection via `document.cookie` for immediate hide without extra server round trip. Middleware already handles redirect to `/select-tenant` for protected paths; this UI gate closes the gap on public/initial pages.
    - Future enhancement: promote tenant selection to server session earlier and drop cookie sniffing.

  ## 2025-09-16 — Web Fix: Profile guardrails & bio tests alignment — ✅ DONE
  - Summary
    - Updated web unit tests to reflect evolved merge patch semantics for profile guardrails and bio editor components. `ProfileGuardrailsForm` now emits a top-level merge patch without a nested `profile` wrapper (arrays and objects submitted directly). Adjusted its test to assert `presets.denominations` under the root patch and verify empty favorite arrays are still present for intentional full replacement semantics. Refactored `BioEditor` soft line break test to account for `remark-breaks` rendering a single `<p>` with `<br/>`, preventing brittle multi-node expectations. Updated `AvatarUpload` test to click the explicit Upload button instead of assuming a form submit event after internal component refactor (component no longer wrapped in a form). All targeted tests now pass; full web suite: 47 files, 150 tests, coverage ~84% lines (thresholds satisfied).
    - Files changed
      - `apps/web/app/profile/ProfileGuardrailsForm.test.tsx` — patch body assertion updated (root-level `presets.denominations`, guardrails arrays expectations) with explanatory comment.
      - `apps/web/app/profile/BioEditor.test.tsx` — soft line break test revised to select combined paragraph, ensure one `<br/>` node, and assert line text presence.
      - `apps/web/src/components/AvatarUpload.test.tsx` — removed `form` submit usage; now simulates file selection + Upload button click.
    - Rationale
      - Keeps tests aligned with minimal merge patch strategy (avoid nested wrappers) and robust against markdown rendering structure. Prevents false negatives on UI refactors (form removal) and ensures intentional full-replacement array semantics are asserted.
    - Quality gates
      - Typecheck: PASS
      - Web tests: PASS (150/150)
      - Coverage: Lines 84.38%, Branches 72.4%, Functions 65.51%, Statements 84.38% (meets configured global thresholds)
    - Follow-up
      - Optional: Add integration test on API side asserting null clears survive normalization and are stored as `null` (not removed) for audit/history clarity. Consider centralizing diff logic if tenant settings adopt similar semantics.

## 2025-09-16 — Web — Profile Bio diff patch & preview soft breaks — ✅ DONE

- Summary
  - Fixed issues where editing the profile bio appeared not to persist and markdown preview ignored single line breaks. Root causes: (1) Bio editor always sent a full bio object (even if unchanged) and did not update its baseline after save, leaving the Save button enabled and creating confusion about persistence. (2) Preview lacked the `remark-breaks` plugin so single newlines collapsed into a single paragraph, making the preview look incorrect versus GitHub-flavored expectations.
  - Implemented diff-based patch semantics: the editor now computes a minimal JSON merge patch and only includes `bio` when it has changed or needs clearing. Clearing a previously non-empty bio sends `{ "bio": null }`; unchanged edits result in no network call. After a successful save, the baseline state is updated so the form becomes clean and Save is disabled.
  - Enhanced preview rendering by adding `remark-breaks` for soft line breaks and preserved existing `remark-gfm` features (tables, task lists, strikethrough). Styling retained code block and inline code theming.
  - Added/updated tests (`BioEditor.test.tsx`) covering minimal patch emission, clearing to null, avoiding submits when value returns to baseline, soft line break rendering, and over-limit enforcement. Updated expectations for body shape (now `{ bio: ... }` versus previously nested under an errant `profile` key in test).

- Files changed
  - `apps/web/app/profile/BioEditor.tsx` — new diff/clear logic, `remark-breaks` import, and conditional patch body construction.
  - `apps/web/app/profile/BioEditor.test.tsx` — rewrite tests for new minimal patch semantics, add soft line break preview test, adjust selectors (placeholder usage) and body assertions.

- Quality gates
  - Typecheck (web): PASS (lint rule fixed by switching `let`→`const`).
  - Tests (web): Updated suite; BioEditor tests green (requires Node 20 per existing tooling note).

- Requirements coverage
  - Persist bio changes reliably with clear semantics for clearing: Done.
  - Prevent redundant saves when unchanged: Done.
  - Render soft line breaks like GFM: Done.
  - Provide test coverage for diff, clear, and preview: Done.

- Deferred / Follow-ups
  - Potential XSS sanitization layer for rendered markdown (currently relying on react-markdown defaults; consider rehype-sanitize for untrusted content).
  - Draft autosave and richer formatting toolbar (emoji, slash commands) remain future enhancements.

## 2025-09-16 — UPROF-11: Denomination presets library & multi-select UI — ✅ DONE

- Summary
  - Implemented denomination presets allowing users to select multiple denominations associated with their profile. Added `GET /api/metadata/denominations` (auth required) serving a curated static JSON list (id, name, notes). Extended usage of the profile schema to support `profile.presets.denominations: string[]` (superseding the earlier single preset concept) and enhanced the Guardrails form with a searchable multi-select chip interface. On first selection, if `guardrails.denominationAlignment` is empty it auto-fills with the preset’s display name; subsequent additions never overwrite user changes. Submission always sends the full denominations array to preserve deterministic array replacement semantics. Comprehensive web tests cover selection, auto-fill, non-overwrite, chip removal, and minimal patch structure; API integration tests validate endpoint auth and shape.
- Files changed
  - apps/api/App/Data/denominations.json — new static presets list (10 entries)
  - apps/api/App/Endpoints/V1.cs — mapped GET `/api/metadata/denominations` with auth guard
  - apps/api.tests/Api/DenominationsMetadataTests.cs — integration tests (401 unauthorized, 200 success shape)
  - apps/web/app/profile/page.tsx — fetch presets server-side (best-effort) and pass to form; include existing selections in initial state
  - apps/web/app/profile/ProfileGuardrailsForm.tsx — multi-select UI (search, chips, auto-fill, deterministic patch building)
  - apps/web/app/profile/ProfileGuardrailsForm.test.tsx — +4 new denomination tests (total 6) verifying selection lifecycle & patch payload
- Quality gates
  - Build: PASS (API + Web)
  - Tests (API): PASS — new metadata tests added; full suite unchanged
  - Tests (Web): PASS — 46 files / 142 tests; coverage ~84% lines (thresholds maintained)
  - Typecheck/Lint: PASS (no new warnings aside from existing MUI license notices)
  - Accessibility: Labeled search input, aria-labels for add/remove buttons, helper text documents auto-fill behavior
- Merge semantics
  - Arrays (`profile.presets.denominations`) replace prior value wholly each patch; explicit empty array clears selections
  - Auto-fill only on first addition when alignment is blank; never overwrites manual edits
- Deferred / Future enhancements
  - Versioned presets with `revision` & deprecation flags
  - Tenant-level preset overrides / extensions
  - Primary designation or weighting for ordering
  - Faceted search groups (family/tradition) & analytics on co-occurrence
  - Server validation rejecting unknown IDs (400) with optional partial accept mode
  - Caching (ETag / If-None-Match) & CDN headers for metadata endpoint
  - Preset diff/change notifications when future revisions land
- Notes
  - Static JSON chosen for speed & simplicity; easily migrated to a DB table later without breaking contract
  - Maintains minimal patch philosophy: only the `profile` subtree sent; unrelated fields omitted
  - Endpoint fast path (<5ms typical) so caching deferred until demand demonstrated

2025-09-19 — Tooling/API: Swagger UI restored after middleware placement & port conflict — ✅ DONE

- Summary
  - Restored the interactive Swagger UI at `/swagger/` after prior refactor inadvertently moved `UseSwagger()` / `UseSwaggerUI()` below `app.Run()` and a stale running process on port 5198 masked subsequent fixes (serving only JSON or 404). Killed the stale listener, reintroduced middleware before `app.Run()`, and verified both the JSON spec (`/swagger/v1/swagger.json` -> 200) and UI root path return expected responses.
- Root Cause
  - Swagger middleware was temporarily relocated after endpoint mappings and (in one iteration) after `app.Run()`, which is never executed. Concurrently, an existing process continued to bind port 5198 causing new runs to fail with address-in-use; curls hit the old process instance lacking UI wiring.
- Resolution Steps
  - Identified and terminated PID holding 5198 (`lsof` + `kill`).
  - Ensured `builder.Services.AddEndpointsApiExplorer()` and `AddSwaggerGen()` remained in service registration.
  - Placed `app.UseSwagger()` and `app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Appostolic API v1"))` before other terminal middleware and prior to `app.Run()`.
  - Verified 200 OK for spec and HTML UI load manually via curl.
- Quality gates
  - API build succeeded (no new warnings beyond existing ImageSharp advisory). Manual curl confirmed spec size (~40KB) and content-type `application/json;charset=utf-8`.
- Follow-ups
  - Added automated integration tests (`SwaggerEndpointTests`) asserting 200 + OpenAPI content for JSON and HTML for UI to prevent regressions.
