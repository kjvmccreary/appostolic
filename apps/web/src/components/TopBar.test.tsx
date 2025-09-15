import { render, screen } from '@testing-library/react';
import React from 'react';
import * as nav from 'next/navigation';
import { TopBar } from './TopBar';

// Mock usePathname to return a stable path
vi.mock('next/navigation');

// Mock ThemeToggle and TenantSwitcher to keep test simple
vi.mock('./ThemeToggle', () => ({ ThemeToggle: () => <div data-testid="theme-toggle" /> }));
vi.mock('./TenantSwitcher', () => ({ TenantSwitcher: () => <div data-testid="tenant" /> }));

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
});
