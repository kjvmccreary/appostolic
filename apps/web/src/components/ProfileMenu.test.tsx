import { render, screen, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('next-auth/react', async () => {
  const actual = (await vi.importActual('next-auth/react')) as Record<string, unknown>;
  return {
    ...actual,
    signOut: vi.fn(),
    useSession: () => ({ data: { isSuperAdmin: true } }),
  };
});

// Avoid actually rendering modal logic in this test
vi.mock('./TenantSwitcherModal', () => ({
  TenantSwitcherModal: ({ open }: { open: boolean }) => (
    <div data-testid="switcher" data-open={open} />
  ),
}));

import { ProfileMenu } from './ProfileMenu';
import { signOut } from 'next-auth/react';

describe('ProfileMenu', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows superadmin chip and toggles menu', async () => {
    const user = userEvent.setup();
    render(<ProfileMenu />);
    const trigger = screen.getByRole('button', { name: /account/i });
    expect(screen.getByText(/superadmin/i)).toBeInTheDocument();
    await user.click(trigger);
    expect(screen.getByRole('menu')).toBeInTheDocument();
  });

  it('opens tenant switcher when clicking menu item', async () => {
    const user = userEvent.setup();
    render(<ProfileMenu />);
    await user.click(screen.getByRole('button', { name: /account/i }));
    await user.click(screen.getByRole('menuitem', { name: /switch tenant/i }));
    expect(screen.getByTestId('switcher')).toHaveAttribute('data-open', 'true');
  });

  it('calls signOut on Sign out', async () => {
    const user = userEvent.setup();
    render(<ProfileMenu />);
    await user.click(screen.getByRole('button', { name: /account/i }));
    await user.click(screen.getByRole('menuitem', { name: /sign out/i }));
    expect(signOut).toHaveBeenCalled();
  });

  it('updates avatar button when avatar-updated event dispatched', async () => {
    const user = userEvent.setup();
    render(<ProfileMenu />);
    const trigger = screen.getByRole('button', { name: /account/i });
    // Initially no img tag inside the trigger
    expect(trigger.querySelector('img')).toBeNull();
    const newUrl = '/media/users/abc/avatar.png?v=123';
    await act(async () => {
      window.dispatchEvent(new CustomEvent('avatar-updated', { detail: { url: newUrl } }));
    });
    // Open menu to ensure re-render not required, avatar should update immediately
    await user.click(trigger);
    const updatedTrigger = screen.getByRole('button', { name: /account/i });
    expect(updatedTrigger.querySelector('img')?.getAttribute('src')).toContain(newUrl);
  });
});
