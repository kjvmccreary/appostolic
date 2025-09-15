// Helper types and functions for the Audits page. Kept separate from page.tsx
// to satisfy Next.js App Router export constraints (page modules should only
// export allowed symbols). This module is unit-tested independently.

import { roleNamesFromFlags } from '../../../../src/lib/roles';

export type AuditRow = {
  id: string;
  userId: string;
  changedByUserId: string;
  changedByEmail: string;
  oldRoles: number; // flags value
  newRoles: number; // flags value
  changedAt: string;
};

// mapAuditRows
// Pure helper to transform raw audit rows by expanding numeric flag bitmasks
// into human-readable comma-separated role name lists.
export function mapAuditRows(raw: AuditRow[]) {
  return raw.map((r) => ({
    ...r,
    oldNames: roleNamesFromFlags(r.oldRoles).join(', ') || 'None',
    newNames: roleNamesFromFlags(r.newRoles).join(', ') || 'None',
  }));
}
