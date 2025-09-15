// Mocks must be declared before imports
vi.mock('next/navigation');
vi.mock('./ThemeToggle', () => ({ ThemeToggle: () => <div data-testid="theme-toggle" /> }));
vi.mock('./TenantSwitcher', () => ({ TenantSwitcher: () => <div data-testid="tenant" /> }));
let mockSession: { data: unknown } = { data: null };
// eslint-disable-next-line @typescript-eslint/no-explicit-any
vi.mock('next-auth/react', () => ({ useSession: () => mockSession }) as any);

import { render, screen } from '@testing-library/react';
import React from 'react';
import * as nav from 'next/navigation';
import { TopBar } from './TopBar';

describe('TopBar', () => {
  it('marks the active nav with aria-current', () => {
    vi.spyOn(nav, 'usePathname').mockReturnValue('/shepherd/step1');
    render(<TopBar />);
    expect(screen.getByRole('link', { name: /shepherd/i })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: /dashboard/i })).not.toHaveAttribute('aria-current');
  });

  it('shows TenantSwitcher on protected paths (/studio, /dev)', () => {
    vi.spyOn(nav, 'usePathname').mockReturnValue('/studio/agents');
    render(<TopBar />);
    expect(screen.getByTestId('tenant')).toBeInTheDocument();
  });

  it('hides TenantSwitcher on select-tenant and public paths', () => {
    vi.spyOn(nav, 'usePathname').mockReturnValue('/select-tenant');
    render(<TopBar />);
    expect(screen.queryByTestId('tenant')).not.toBeInTheDocument();
  });

  it('shows TenantSwitcher on dashboard page (/)', () => {
    vi.spyOn(nav, 'usePathname').mockReturnValue('/');
    render(<TopBar />);
    expect(screen.getByTestId('tenant')).toBeInTheDocument();
  });

  it('does not render Create Lesson without canCreate', () => {
    vi.spyOn(nav, 'usePathname').mockReturnValue('/');
    render(<TopBar />);
    expect(screen.queryByRole('link', { name: /create lesson/i })).not.toBeInTheDocument();
  });

  it('renders Create Lesson when canCreate', () => {
    mockSession = { data: { canCreate: true } };
    vi.spyOn(nav, 'usePathname').mockReturnValue('/');
    render(<TopBar />);
    expect(screen.getByRole('link', { name: /create lesson/i })).toBeInTheDocument();
  });
});
