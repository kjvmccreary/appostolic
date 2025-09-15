import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('next/navigation', () => ({ useRouter: () => ({ refresh: vi.fn() }) }));

type Membership = { tenantSlug: string; role: string };
type MockSession = {
  data: { tenant: string; memberships: Membership[] };
  update: (u: { tenant: string }) => Promise<void>;
};

let mockSession: MockSession = {
  data: {
    tenant: 't1',
    memberships: [
      { tenantSlug: 't1', role: 'Admin' },
      { tenantSlug: 't2', role: 'Creator' },
    ],
  },
  update: async () => {},
};
vi.mock('next-auth/react', () => ({ useSession: () => mockSession }));

import { TenantSwitcherModal } from './TenantSwitcherModal';

describe('TenantSwitcherModal', () => {
  beforeEach(() => {
    mockSession = {
      data: {
        tenant: 't1',
        memberships: [
          { tenantSlug: 't1', role: 'Admin' },
          { tenantSlug: 't2', role: 'Creator' },
        ],
      },
      update: async () => {},
    };
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({}) } as Response);
  });

  it('closes on backdrop click', () => {
    const onClose = vi.fn();
    const { container } = render(<TenantSwitcherModal open onClose={onClose} />);
    const dialog = screen.getByRole('dialog');
    const backdrop = (dialog.firstChild || container.querySelector('.bg-black/40')) as Element;
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
    expect(global.fetch).toHaveBeenCalledWith(
      '/api/tenant/select',
      expect.objectContaining({ method: 'POST' }),
    );
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it('renders role badges and marks current', () => {
    const { container } = render(<TenantSwitcherModal open onClose={() => {}} />);
    const badges = container.querySelectorAll('[data-testid="role-badge"]');
    expect(badges.length).toBeGreaterThan(0);
    const roles = Array.from(badges).map((b) => b.getAttribute('data-role'));
    expect(roles).toContain('Current');
    expect(roles).toContain('Creator');
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
    expect(window.localStorage.getItem('last_selected_tenant')).toBe('t2');
  });
});
