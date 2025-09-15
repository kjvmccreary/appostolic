import { describe, it, expect } from 'vitest';
import { mapAuditRows, AuditRow } from './page';

// Focused unit test to ensure numeric role flag decoding for audits UI
// stays consistent with server [Flags] enum ordering (1,2,4,8).

describe('audits page mapping', () => {
  it('decodes single and combined role flags to names', () => {
    const rows: AuditRow[] = [
      {
        id: '1',
        userId: 'u1',
        changedByUserId: 'a1',
        changedByEmail: 'actor@example.com',
        oldRoles: 0,
        newRoles: 1, // TenantAdmin
        changedAt: new Date().toISOString(),
      },
      {
        id: '2',
        userId: 'u2',
        changedByUserId: 'a1',
        changedByEmail: 'actor@example.com',
        oldRoles: 1 | 2 | 4 | 8, // all roles
        newRoles: 2 | 4, // Approver + Creator
        changedAt: new Date().toISOString(),
      },
    ];

    const mapped = mapAuditRows(rows);
    const first = mapped[0];
    const second = mapped[1];

    expect(first.oldNames).toBe('None');
    expect(first.newNames).toContain('TenantAdmin');
    expect(second.oldNames.split(', ').length).toBeGreaterThanOrEqual(4);
    expect(second.newNames).toBe('Approver, Creator');
  });
});
