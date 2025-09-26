import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  makeMembership,
  makeTenantSession,
  type SessionWithClaims,
} from '../../test/fixtures/authSession';
import { authMockState } from '../../test/fixtures/mswAuthHandlers';

vi.mock('next/navigation', () => ({ useRouter: () => ({ refresh: vi.fn() }) }));

let mockSession: {
  data: SessionWithClaims;
  update: (payload: { tenant: string }) => Promise<void>;
};

vi.mock('next-auth/react', () => ({ useSession: () => mockSession }));

import { TenantSwitcherModal } from './TenantSwitcherModal';

describe('TenantSwitcherModal', () => {
  beforeEach(() => {
    mockSession = {
      data: makeTenantSession({
        tenant: 't1',
        memberships: [
          makeMembership({ tenantSlug: 't1', roles: ['TenantAdmin'] }),
          makeMembership({ tenantSlug: 't2', roles: ['Creator'] }),
        ],
      }),
      update: async () => {},
    };
  });

  it('closes on backdrop click', () => {
    const onClose = vi.fn();
    render(<TenantSwitcherModal open onClose={onClose} />);
    // Backdrop is rendered as a sibling inside a portal; query it directly
    const backdrop = document.querySelector('[data-testid="tenant-switcher-backdrop"]') as Element;
    fireEvent.click(backdrop);
    expect(onClose).toHaveBeenCalled();
  });

  it('updates session, posts to API, and refreshes on selection', async () => {
    const onClose = vi.fn();
    const update = vi.fn().mockResolvedValue(undefined);
    mockSession.update = update;
    render(<TenantSwitcherModal open onClose={onClose} />);
    fireEvent.click(screen.getByRole('button', { name: /t2/i }));
    await waitFor(() => expect(update).toHaveBeenCalledWith({ tenant: 't2' }));
    await waitFor(() => expect(authMockState.tenantSelect.calls).toHaveLength(1));
    const request = authMockState.tenantSelect.calls[0];
    expect(request.url).toContain('/api/tenant/select');
    expect(request.body).toMatchObject({ tenant: 't2' });
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it('renders role badges and marks current', () => {
    render(<TenantSwitcherModal open onClose={() => {}} />);
    const badges = document.querySelectorAll('[data-testid="role-badge"]');
    expect(badges.length).toBeGreaterThan(0);
    const roles = Array.from(badges).map((b) => b.getAttribute('data-role'));
    expect(roles).toContain('Current');
    // Non-current badges should be canonical labels (e.g., Admin/Creator/Learner), not legacy names
    expect(roles.some((r) => r === 'Admin' || r === 'Creator' || r === 'Learner')).toBe(true);
  });

  it('remembers last selected tenant in localStorage', async () => {
    const onClose = vi.fn();
    const update = vi.fn().mockResolvedValue(undefined);
    mockSession.update = update;
    // Ensure clean state
    window.localStorage.removeItem('last_selected_tenant');
    render(<TenantSwitcherModal open onClose={onClose} />);
    // Click the second membership (t2)
    const btn = screen.getByRole('button', { name: /t2/i });
    btn.click();
    await waitFor(() => expect(update).toHaveBeenCalled());
    await waitFor(() => expect(authMockState.tenantSelect.calls).toHaveLength(1));
    expect(window.localStorage.getItem('last_selected_tenant')).toBe('t2');
  });

  it('hints the last selected tenant with a dashed border when reopening', () => {
    // Arrange: last selected is t2, current is t1
    window.localStorage.setItem('last_selected_tenant', 't2');
    const { getByRole } = render(<TenantSwitcherModal open onClose={() => {}} />);
    const hintedBtn = getByRole('button', { name: /t2/i });
    // The component adds 'border-dashed' class when hinted
    expect(hintedBtn.className).toContain('border-dashed');
    // Current tenant (t1) should not be hinted with dashed border
    const currentBtn = getByRole('button', { name: /t1\s+Current/i });
    expect(currentBtn.className).not.toContain('border-dashed');
  });
});
