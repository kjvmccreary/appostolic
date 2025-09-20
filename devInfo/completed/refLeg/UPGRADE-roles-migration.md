# Upgrade Guide: Legacy MembershipRole Removal & Roles Flags Hardening

Date: 2025-09-20
Related Stories: refLeg-07 (Drop legacy column), refLeg-08 (Bitmask constraints), refLeg-09 (Docs), refLeg-10 (Rollback Script)
Tag (planned after Story 10): `roles-removal-complete`

## Overview

This guide captures the operational steps, verification checks, and rollback procedure surrounding the removal of the legacy single-role column (`app.memberships.role` / `app.invitations.role`) and enforcement of the Roles flags bitmask.

## What Changed

1. Dropped legacy columns `role` from `app.memberships` and `app.invitations`.
2. Added CHECK constraints:
   - `ck_memberships_roles_nonzero`: `roles <> 0`
   - `ck_invitations_roles_nonzero`: `roles <> 0`
   - `ck_memberships_roles_valid`: `roles <> 0 AND (roles & ~15) = 0`
   - `ck_invitations_roles_valid`: `roles <> 0 AND (roles & ~15) = 0`
3. Added tests verifying: (a) model property absence, (b) physical column absence (relational provider only), (c) invalid bitmask insert fails.
4. Web now treats flags (`roles` bitmask or canonical names) as sole authority; legacy role fallback scheduled for deprecation (see Stories 11–14).

## Deploy Sequence (Forward)

1. Ensure code at commit including migrations `DropLegacyMembershipRole` and `AddRolesBitmaskConstraint` is built & tested.
2. Apply database migrations (standard deploy pipeline). Confirm no blocking long-running transactions on `app.memberships` / `app.invitations`.
3. Post-migration verification:
   - SELECT count(\*) FROM app.memberships WHERE roles = 0; => expect 0
   - SELECT count(\*) FROM app.memberships WHERE (roles & ~15) <> 0; => expect 0
   -
4. Run API integration suite (or smoke subset) to validate auth/login & admin endpoints.
5. Run targeted web tests for admin gating (`TopBar.admin.test.tsx`) & roles helper to confirm no reintroduction of legacy path.
6. Tag repository (after rollback artifacts present): `git tag roles-removal-complete` then `git push --tags`.

## Smoke Checklist

| Item                            | Command / Check                                                                                  | Expect                 |
| ------------------------------- | ------------------------------------------------------------------------------------------------ | ---------------------- |
| Constraint non-zero memberships | `\d+ ck_memberships_roles_nonzero`                                                               | present                |
| Constraint valid memberships    | `\d+ ck_memberships_roles_valid`                                                                 | present                |
| Admin menu gating               | Sign in as admin user → select tenant                                                            | Admin dropdown visible |
| Invalid bit insert attempt      | Manual `INSERT roles=32`                                                                         | fails (23514)          |
| Invite creation                 | Create invite with roles flags                                                                   | 200 + roles persisted  |
| Legacy column absence           | `SELECT 1 FROM information_schema.columns WHERE table_name='memberships' AND column_name='role'` | empty                  |

## Rollback (Emergency)

Use only if a production issue directly attributable to roles column removal requires interim restoration. Preferred mitigation is forward-fix (e.g., updating client fixtures). Rollback reintroduces technical debt – schedule re-removal promptly.

1. Execute rollback SQL script: `scripts/rollback/restore_membership_role.sql` (idempotent where possible). It will:
   - Re-add nullable `role` columns.
   - Backfill heuristic: flags → legacy role (Admin, Editor, Viewer) best-effort.
   - Drop bitmask constraints (re-add requires redeploy).
2. Restart API instances (so EF model mismatch surfaces as warning only; code no longer reads the column but presence is tolerated for emergency).
3. DO NOT re-enable legacy authorization logic; keep flags as source of truth.
4. Create issue tracking forward re-removal.

## Heuristic Mapping (Flags → Legacy)

| Flags Combination                      | Legacy Role Mapped |
| -------------------------------------- | ------------------ |
| TenantAdmin & (Approver or Creator)    | Admin              |
| Creator (+Learner) without TenantAdmin | Editor             |
| Learner only                           | Viewer             |
| Other / ambiguous                      | NULL               |

Justification: Preserve maximum privilege semantics on downgrade; ambiguous partial sets left NULL to avoid over-grant.

## Validation After Rollback

| Check               | Expect                                                                                                       |
| ------------------- | ------------------------------------------------------------------------------------------------------------ |
| Column present      | `SELECT 1 FROM information_schema.columns WHERE table_name='memberships' AND column_name='role'` returns row |
| Backfill coverage   | `SELECT count(*) FROM app.memberships WHERE role IS NULL` small / understood                                 |
| Constraints dropped | `\d+ ck_memberships_roles_valid` absent                                                                      |

## Monitoring & Observability

Add (temporary) dashboard queries:

- Count distinct invalid bitmask attempts (should remain 0): inspect API logs for constraint violation names.
- Track sessions where web legacy fallback path triggers (TRACE/warn) — should trend to 0 before Stories 11–14.

## Post-Deployment Follow-ups

- Proceed with Story 11 (deprecation toggle) once fallback usage is zero in staging.
- Remove legacy tokens acceptance inside `roles[]` (Story 12).
- Optional: Add composite index for future roles queries (deferred until role expansion is defined).

## References

- Migrations: `20250920002345_DropLegacyMembershipRole`, `20250920121114_AddRolesBitmaskConstraint`
- Tests: `SchemaAbsenceTests`, `LegacyRoleColumnRemovalTests`, `RolesBitmaskConstraintTests`
- Frontend gating: `TopBar.tsx`, `roles.ts`, `roleGuard.ts`

## Changelog Entry Template

> IAM: Removed legacy `MembershipRole` column, enforced flags-only roles with bitmask integrity constraints; added rollback SQL and upgrade guide. Tag: `roles-removal-complete`.

---

Prepared by: Migration Story 9 automation
