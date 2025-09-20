-- Rollback Script: Restore legacy single-role column and relax bitmask constraints
-- Use only for emergency rollback after legacy column removal.
-- Applies heuristic backfill from flags to legacy role values.
-- Idempotency: checks for column existence / constraint presence where feasible.

BEGIN;

-- 1. Drop strict valid bitmask constraints (nonzero + subset) if they exist.
ALTER TABLE app.memberships DROP CONSTRAINT IF EXISTS ck_memberships_roles_valid;
ALTER TABLE app.invitations DROP CONSTRAINT IF EXISTS ck_invitations_roles_valid;

-- Non-zero constraints may remain; they are safe with legacy column present.
-- (Optionally drop if causing issues; uncomment if needed.)
-- ALTER TABLE app.memberships DROP CONSTRAINT IF EXISTS ck_memberships_roles_nonzero;
-- ALTER TABLE app.invitations DROP CONSTRAINT IF EXISTS ck_invitations_roles_nonzero;

-- 2. Re-add legacy role column if missing (integer, nullable)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns 
    WHERE table_schema='app' AND table_name='memberships' AND column_name='role'
  ) THEN
    ALTER TABLE app.memberships ADD COLUMN role integer NULL;
  END IF;
END$$;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns 
    WHERE table_schema='app' AND table_name='invitations' AND column_name='role'
  ) THEN
    ALTER TABLE app.invitations ADD COLUMN role integer NULL;
  END IF;
END$$;

-- 3. Heuristic backfill (only where role IS NULL)
-- Mapping (bit flags): 1=TenantAdmin,2=Approver,4=Creator,8=Learner
-- Heuristic priority:
--   Full admin-like (has 1 and (2 or 4)) -> Admin (1)
--   Editor-like (has 4 and 8, without TenantAdmin) -> Editor (2)
--   Viewer-like (has 8 only) -> Viewer (3)
--   Else NULL (ambiguous or no flags)
UPDATE app.memberships SET role = CASE
  WHEN (roles & 1) <> 0 AND ((roles & 2) <> 0 OR (roles & 4) <> 0) THEN 1
  WHEN (roles & 1) = 0 AND (roles & 4) <> 0 AND (roles & 8) <> 0 THEN 2
  WHEN (roles & 1) = 0 AND (roles & 4) = 0 AND (roles & 8) <> 0 THEN 3
  ELSE NULL END
WHERE role IS NULL;

UPDATE app.invitations SET role = CASE
  WHEN (roles & 1) <> 0 AND ((roles & 2) <> 0 OR (roles & 4) <> 0) THEN 1
  WHEN (roles & 1) = 0 AND (roles & 4) <> 0 AND (roles & 8) <> 0 THEN 2
  WHEN (roles & 1) = 0 AND (roles & 4) = 0 AND (roles & 8) <> 0 THEN 3
  ELSE NULL END
WHERE role IS NULL;

COMMIT;

-- Verification suggestions (run manually):
-- SELECT COUNT(*) FROM app.memberships WHERE role IS NULL;  -- inspect remaining ambiguous rows
-- SELECT role, COUNT(*) FROM app.memberships GROUP BY role;
-- SELECT COUNT(*) FROM app.memberships WHERE (roles & ~15) <> 0; -- should still be 0 unless invalid bits were introduced.
