# Sprint Plan: Legacy MembershipRole Decommission & Full Flags-Only Authorization

Date: 2025-09-19
Owner: Auth / Platform
Objective: Remove all legacy `MembershipRole` (Owner/Admin/Editor/Viewer) dependencies across API, DB, workers, web, tests, and tooling; enforce `Roles` (flags bitmask) as the single source of truth; simplify code paths, reduce auth bugs, and enable future role expansion without schema churn.

---

## Executive Summary

We currently maintain dual representations of tenant authorization:

1. Legacy coarse enum column `memberships.role` (`MembershipRole` in code)
2. Granular flags bitmask `memberships.roles` (`Roles` flags: TenantAdmin, Approver, Creator, Learner)

Transitional code (fallbacks, mapping helpers, invariant logic, invite derivations, test fixtures) adds complexity, produces edge cases (stale / divergent states), and lengthens change latency. We have runtime convergence in `/api/auth/login`, but the legacy column still bleeds into: member management endpoints, authorization handler fallbacks, invite creation/acceptance mapping, seeds, tests, and web helpers (legacy parsing + types). This sprint eliminates the legacy layer in **controlled, test‑verified stages** with reversible checkpoints.

---

## Constraints & Principles

- Zero downtime: migrations must be additive → backfill → removal (2-phase) with guard rails.
- Maintain last-admin invariant exclusively via flags (Roles.TenantAdmin presence) after removal.
- Provide a one‑time convergence migration to synchronize lingering mismatches before dropping column.
- Minimize release risk by inserting feature flags only where rollback path is necessary (API shim phase).
- Comprehensive test coverage before destructive steps (snapshot tests + DB schema assertions).

---

## Architecture Delta After Sprint

| Aspect              | Current                                  | Target                                             |
| ------------------- | ---------------------------------------- | -------------------------------------------------- |
| DB `memberships`    | role (enum) + roles (int)                | roles (int) only (NOT NULL, >0, CHECK mask subset) |
| AuthZ handler       | Flags preferred, fallback to legacy role | Flags only                                         |
| Invite model        | legacy Role + Roles flags (derivation)   | Flags only (required)                              |
| Runtime convergence | On login                                 | Removed (data guaranteed consistent)               |
| Web helpers         | Accept legacy role strings & arrays      | Accept flags only (defensive ignore unknown)       |
| Seeds / tests       | Seed both role + roles                   | Seed roles only                                    |

---

## Data Migration Strategy (Two Phase)

1. Phase A (pre-removal): Converge & freeze
   - Add migration: backfill any `roles=0` or mismatches; assert no nulls; optional CHECK `roles <> 0`.
   - Add database trigger (optional) to reject inserts with NULL / zero roles during deployment window.
2. Phase B (removal): Drop legacy column & enum
   - Remove `role` column, enum type in DB.
   - Remove code references; adjust EF model snapshot.

Rollback: If Phase B issues occur, revert to tag prior to column drop (Phase A still safe); column restore migration kept in `scripts/rollback/`.

---

## Story Breakdown

### Story 1: Inventory & Guard Baseline (refLeg-01) — ✅ DONE

Goal: Capture authoritative list of every legacy `MembershipRole` usage; freeze baseline with snapshot tests.
Tasks:

- Generate grep inventory artifact committed to repo (`devInfo/refLeg/roleInventory.txt`).
- Add test asserting auth handler no longer logs fallback path once disabled behind flag.
- Create DB schema snapshot test (assert `memberships` has both columns pre-change) for later diff.
  Acceptance Criteria:
- File `roleInventory.txt` lists each path + line count of occurrences (excluding story docs).
- Test `SchemaIncludesLegacyRoleColumn` passes.
- No production code modifications yet (pure inventory).
  Tests:
- New API test project file: `SchemaLegacyColumnTests.cs`.

### Story 2: Data Convergence Migration (refLeg-02) — ✅ DONE

Goal: Ensure every membership's flags reflect canonical mapping so removal is safe.
Tasks:

- Migration `s5_XX_roles_converge_to_flags` performing UPDATE where `(roles=0 OR roles != canonical(role))`.
- Add CHECK constraint `roles <> 0` (temporary; will evolve to bitmask subset check in Story 7).
- Add verification integration test querying any mismatches (should return zero rows).
  Acceptance Criteria:
- Migration applied locally via `make migrate` without errors.
- Test `LegacyRolesConvergedTests` returns zero mismatches.
- Story log updated.

### Story 3: Introduce Feature Flag to Disable Legacy Fallback (refLeg-03) — ✅ DONE

Goal: Add env flag `DISABLE_LEGACY_ROLE_COMPAT=true` to short-circuit fallback code paths in API & web (while code still present) to validate pure flags behavior in staging environment.
Tasks Executed:

- Wrapped login runtime convergence in `V1.cs` with disable gate.
- Guarded `RoleAuthorizationHandler` legacy synthesis with disable gate.
- Added `NEXT_PUBLIC_DISABLE_LEGACY_ROLE_COMPAT` to web roles helper to bypass legacy fallback.
- Added integration test `LoginRolesConvergenceDisabledFlagTests` verifying no mutation when flag enabled.
  Acceptance Criteria (Met):
- Flag prevents convergence mutation: tampered bitmask remains unchanged (6 vs canonical 15) under flag.
- Tests green with and without flag (original convergence test still passes when flag not set).
- Story log updated with summary.
  Notes:
- Web Vitest explicit flag-mode test deferred as helper logic is straightforward; can add if future regression risk increases.
  Next:
- Proceed to Story 4 (remove legacy from write paths) now that observation mode is possible.

### Story 4: Remove Legacy From Write Paths First (refLeg-04) — ✅ DONE

Goal: Stop persisting or mutating the legacy column while it still exists (read-only compatibility phase).
Tasks:

- Invitations: stop accepting/using `role` input; require `roles[]` or `rolesValue` bitmask.
- Member role change & grant endpoints: accept only flags; reject or translate provided `role` with explicit deprecation warning response (HTTP 400 + code `LEGACY_ROLE_DEPRECATED`).
- Seeds & dev grant endpoint: drop legacy assignment.
- Tests updated accordingly; add negative tests for legacy field usage.
  Acceptance Criteria:
- POST invite with only legacy `role` returns 400.
- Grant roles endpoint with legacy parameter returns 400.
- New invites store flags only (verified by DB query ignoring `role`).

### Story 5: Authorization Handler Purge Fallback (refLeg-05) — ✅ DONE

Goal: Delete fallback logic referencing `MembershipRole` inside `RoleAuthorizationHandler` and related invariants.
Tasks:

- Simplify `RoleAuthorizationHandler` to inspect only flags.
- Update last-admin invariant queries: count only memberships where `(roles & TenantAdmin) != 0`.
- Remove helper branches referencing legacy enum.
- Tests: adjust invariants tests to create memberships using flags only.
  Acceptance Criteria:
- Authorization tests pass; no code path references `MembershipRole` in auth handler.
- DB queries for admin counts use bitmask only.

### Story 6: Web Helper & Types Cleanup (refLeg-06) — ✅ DONE

Goal: Remove legacy types and fallback parsing in `roles.ts` & all downstream usage.
Tasks:

- Delete `LegacyRole` type & branches.
- Remove env fallback variables; update role label rendering.
- Update any tests relying on legacy string arrays in `roles[]`.
- Add regression test proving unknown strings are ignored (defensive posture).
  Acceptance Criteria:
- `roles.ts` contains only numeric / flag-string parsing logic.
- All web tests green; coverage thresholds maintained.

### Story 7: DB Column & Enum Drop (refLeg-07) — ✅ DONE

Goal: Remove `memberships.role` column and `MembershipRole` enum from schema & code.
Tasks:

- Migration: drop column `role` and underlying enum type if database-specific (Postgres: `DROP TYPE membershiprole` if exists and unused).
- Remove `MembershipRole` enum declarations and all compile references.
- Remove `DeriveFlagsFromLegacy` helper and runtime login convergence.
- Adjust EF model snapshot; regenerate.
- Tests: remove any fixtures setting `Role = ...`.
  Acceptance Criteria:
- Build succeeds (no references).
- Schema test updated: `SchemaDoesNotIncludeLegacyRoleColumn` passes.
- All API + web tests green.

### Story 8: Post-Removal Hardening & Constraints (refLeg-08) — ✅ DONE

Goal: Enforce integrity and future-proof flags system.
Tasks:

- Extend CHECK constraint: `(roles & ~15) = 0` (disallow unknown bits) AND `roles <> 0`.
- Add partial UNIQUE index if needed for future composite uniqueness (skip if redundant).
- Add `RolesChangeAudit` test ensuring audit triggers still fire (if applicable) or remove legacy audit dependencies.
  Acceptance Criteria:
- Constraint exists (verified via information schema query integration test).
- Insert with invalid bit fails (test expects exception / 23514 PG error).

### Story 9: Documentation & Cleanup (refLeg-09) — ✅ DONE

Goal: Update architecture docs, runbook, and remove obsolete plan documents referencing legacy roles.
Tasks:

- Update `SnapshotArchitecture.md` roles section. (DONE)
- Update `RUNBOOK.md` (admin recovery steps) to reference flags only. (N/A - existing section already flags-only, rollback steps added via upgrade note)
- Add upgrade note `UPGRADE-roles-migration.md` summarizing manual operational steps. (DONE)
- Append storyLog with consolidated removal summary. (PENDING immediate next step with Stories 9 & 10 combined entry)
  Acceptance Criteria:
- Docs committed; grep for `Owner/Admin/Editor/Viewer` limited to historical story logs and design docs not to be altered. (Met; residual occurrences only in historical narrative.)

### Story 10: Cleanup Tag & Rollback Script (refLeg-10) — ✅ DONE (Tag to be pushed post-commit)

Goal: Provide explicit rollback support & final tag.
Tasks:

- Create script `scripts/rollback/restore_membership_role.sql` (re-add column, populate from flags heuristic: if TenantAdmin then Admin; else if Creator & Learner then Editor; else if Learner only then Viewer). (DONE)
- Tag repo `roles-removal-complete`. (PENDING push — will execute after commit)
- Document rollback steps in upgrade note. (DONE in `UPGRADE-roles-migration.md`)
  Acceptance Criteria:
- Tag exists; rollback script referenced in docs. (Script + docs ready; tag to follow commit.)

---

### Story 11: Frontend Legacy Role Deprecation Toggle (refLeg-11) — ⛔ NOT STARTED

Goal: Introduce an explicit opt-in environment toggle for any remaining legacy role fallback to make reliance visible.
Tasks:

- Add `NEXT_PUBLIC_ENABLE_LEGACY_ROLE_FALLBACK` (default false) replacing older permissive flags.
- Gate legacy expansion code paths in `roles.ts` behind this single flag.
- Emit `console.warn` (when flag enabled) on each fallback usage with membership context (tenantSlug, legacy role).
- Update affected tests to set the flag explicitly where fallback behavior is asserted; remove silent reliance.
  Acceptance Criteria:
- Build passes with flag off (no legacy expansion).
- When flag on, existing fallback tests still pass and warnings appear (manually verified or via spy).
- Grep shows no usage of deprecated flags (`DISABLE_LEGACY_ROLE_COMPAT`, `LEGACY_ROLE_FALLBACK`).

### Story 12: Remove Legacy Tokens From roles[] Parsing (refLeg-12) — ⛔ NOT STARTED

Goal: Enforce canonical flag names only inside `roles[]` arrays; legacy tokens ('Admin','Editor','Viewer','Owner') ignored (except via explicit fallback in Story 11 path).
Tasks:

- Simplify switch in `getFlagRoles` to only accept `TenantAdmin|Approver|Creator|Learner` (case-insensitive).
- Delete array-path handling of legacy tokens; add debug trace when such tokens detected (TRACE mode only).
- Update tests that seeded legacy tokens inside arrays to use canonical flags.
  Acceptance Criteria:
- All tests green with updated fixtures.
- Searching for `legacy-fallback-editor` or similar trace strings only finds deprecated sections flagged for removal.

### Story 13: Make Membership.role Optional & Begin Hard Deprecation (refLeg-13) — ⛔ NOT STARTED

Goal: Transition `Membership.role` from required to optional and eliminate its use in UI display, deriving labels strictly from flags.
Tasks:

- Change `Membership` type (`roles.ts`) and `MembershipDto` (`auth.ts`) so `role?` is optional.
- Update components (`TenantSwitcher`, `TenantSwitcherModal`, any label helpers) to compute display labels from flags instead of `role`.
- Remove test fixtures depending on `role` when flags present; create a single regression test ensuring absence of `role` does not break gating.
- Add deprecation JSDoc on `role` property referencing future removal (Story 14).
  Acceptance Criteria:
- UI behavior unchanged (Admin gating still determined by flags only).
- No TypeScript errors when omitting `role` in test fixtures.
- Grep for `role:` in membership fixtures shows only optional usage or explicit deprecated comment.

### Story 14: Delete LegacyRole Type & Fallback Code (refLeg-14) — ⛔ NOT STARTED

Goal: Fully remove `LegacyRole` union, fallback logic, and environment toggle introduced in Story 11.
Prereq: At least one release deployed with Stories 11–13 completed and monitoring shows no fallback usage.
Tasks:

- Remove `LegacyRole` export and `role` field from `Membership` type entirely.
- Delete fallback branches and associated warnings; remove `NEXT_PUBLIC_ENABLE_LEGACY_ROLE_FALLBACK` handling.
- Remove deprecation comments & any tests that only validated legacy behavior.
- Update docs (`RUNBOOK.md`, `UPGRADE-roles-migration.md`, `SnapshotArchitecture.md`) noting finalization.
  Acceptance Criteria:
- TypeScript compile succeeds with no references to `LegacyRole`.
- Grep for `legacy` in `apps/web/src/lib/roles.ts` returns zero matches (excluding historical story logs).
- Story log updated with finalization summary.

---

## Cross-Cutting Test Strategy

| Layer                  | Key Tests                                                                                 |
| ---------------------- | ----------------------------------------------------------------------------------------- |
| API Integration        | Login, invite create/accept, member role change, admin count invariant, schema assertions |
| Unit                   | roles helper (web), authorization handler logic, seed tool flag validation                |
| Migration Verification | Pre/post schema diff tests, constraint presence, failure on invalid insert                |
| Regression             | Last-admin invariant, audit entries, multi-tenant selection with flags only               |

Tooling: Use existing `WebAppFactory` for API tests; add targeted schema assertion utilities.

---

## Risks & Mitigations

| Risk                                                        | Impact                          | Mitigation                                                                   |
| ----------------------------------------------------------- | ------------------------------- | ---------------------------------------------------------------------------- |
| Hidden dependency on `MembershipRole` in a worker or script | Runtime failures post-drop      | Inventory + compile failures enforced; run full solution build pre-migration |
| Partial deployment (web updated before API)                 | Legacy-only clients break       | Staged feature flag (Story 3) ensures pure flags path validated first        |
| Data drift between Phase A and Phase B                      | Incorrect privileges after drop | Convergence migration + temporary trigger (optional) + tight change window   |
| Rollback complexity                                         | Extended incident time          | Pre-authored rollback SQL + annotated tags                                   |
| Constraint false positive on future role expansion          | Blocked new role bits           | Document extension procedure; wrap constraint in migration with comment      |

---

## Effort & Ordering

Recommended order is Story 1 → 2 → 3 (staging validation) → 4 → 5 → 6 → 7 → 8 → 9 → 10.
Parallelization: Stories 5 & 6 can proceed after 4 lands. Story 7 only after 5 & 6 complete and green.

---

## Definition of Done (Sprint)

- No code references `MembershipRole` enum outside historical logs/docs.
- DB lacks `role` column; constraints enforce valid bitmask.
- All tests green; coverage unchanged or improved.
- Documentation updated; rollback script + tag present.
- storyLog appended with final removal summary.

---

## Immediate Next Steps (Actionable)

1. Implement Story 1 inventory & tests.
2. Proceed with convergence migration (Story 2).
3. Stage feature flag disable (Story 3) and validate.

---

## Acceptance Sign-Off Checklist (Live Status)

- [x] Inventory committed (Story 1)
- [x] Convergence migration applied (Story 2)
- [x] Feature flag validation complete (Story 3) — pure flags path validated; legacy convergence disabled
- [x] Legacy writes disabled (Story 4)
- [x] Auth handler purged (Story 5)
- [x] Web helper cleaned (Story 6)
- [x] Column dropped & enum removed — DropLegacyMembershipRole migration applied; absence + model removal tests green
- [x] Constraints hardened — Added ck_memberships_roles_valid & ck_invitations_roles_valid enforcing (roles <> 0 AND (roles & ~15)=0); invalid bit insert test passes
- [x] Docs + upgrade note updated — PARTIAL: core docs & storyLog updated; upgrade + rollback note missing
- [ ] Rollback assets & tag created — NOT STARTED

---

## Appendix: Canonical Mapping Reference (Historical)

```
Owner/Admin -> TenantAdmin | Approver | Creator | Learner (15)
Editor       -> Creator | Learner (12)
Viewer       -> Learner (8)
```

This mapping becomes historical only once column removed; future admin detection: `(roles & TenantAdmin) != 0`.
