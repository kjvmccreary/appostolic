import { render, screen, fireEvent } from '@testing-library/react';
import React from 'react';
import { describe, it, expect, vi } from 'vitest';

import { NavDrawer } from './NavDrawer';

// Mutable mock for usePathname so tests can simulate route changes
let mockPathname = '/';
vi.mock('next/navigation', () => ({
  usePathname: () => mockPathname,
}));

describe('NavDrawer', () => {
  it('renders primary items and closes on backdrop click', () => {
    const onClose = vi.fn();
    render(
      <NavDrawer
        open
        onClose={onClose}
        isAdmin={false}
        navItems={[{ label: 'Home', href: '/' }]}
      />,
    );
    // Item present
    expect(screen.getByRole('link', { name: /home/i })).toBeInTheDocument();
    // Backdrop close
    fireEvent.click(screen.getByTestId('backdrop'));
    expect(onClose).toHaveBeenCalled();
  });

  it('shows Admin section only when isAdmin', () => {
    const onClose = vi.fn();
    const adminItem = { label: 'Members', href: '/studio/admin/members' };
    const { rerender } = render(
      <NavDrawer open onClose={onClose} isAdmin={false} navItems={[]} adminItems={[adminItem]} />,
    );
    expect(screen.queryByRole('link', { name: /members/i })).not.toBeInTheDocument();

    rerender(<NavDrawer open onClose={onClose} isAdmin navItems={[]} adminItems={[adminItem]} />);
    expect(screen.getByRole('link', { name: /members/i })).toBeInTheDocument();
  });

  it('closes on ESC and route change', () => {
    const onClose = vi.fn();
    mockPathname = '/';
    const { rerender } = render(
      <NavDrawer
        open
        onClose={onClose}
        isAdmin={false}
        navItems={[{ label: 'Home', href: '/' }]}
      />,
    );
    // ESC key
    onClose.mockClear();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(onClose).toHaveBeenCalled();

    // Route change
    onClose.mockClear();
    mockPathname = '/studio/agents';
    rerender(
      <NavDrawer
        open
        onClose={onClose}
        isAdmin={false}
        navItems={[{ label: 'Home', href: '/' }]}
      />,
    );
    expect(onClose).toHaveBeenCalled();
  });
});
