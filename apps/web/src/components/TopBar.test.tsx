// Mocks must be declared before imports
vi.mock('next/navigation');
vi.mock('./ThemeToggle', () => ({ ThemeToggle: () => <div data-testid="theme-toggle" /> }));
// TenantSwitcher is no longer rendered in TopBar; keep mock but tests no longer expect it present
vi.mock('./TenantSwitcher', () => ({ TenantSwitcher: () => <div data-testid="tenant" /> }));
vi.mock('./NewAgentButton', () => ({
  NewAgentButton: () => (
    <a href="#" data-testid="new-agent">
      New Agent
    </a>
  ),
}));
vi.mock('./NavDrawer', () => ({
  NavDrawer: ({ open, onClose }: { open: boolean; onClose: () => void }) => (
    <div data-testid="drawer" data-open={open} onClick={() => onClose()} />
  ),
}));
vi.mock('./ProfileMenu', () => ({ ProfileMenu: () => <div data-testid="profile-menu" /> }));
let mockSession: { data: unknown } = { data: null };
// eslint-disable-next-line @typescript-eslint/no-explicit-any
vi.mock('next-auth/react', () => ({ useSession: () => mockSession }) as any);

import { render, screen, act, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import * as nav from 'next/navigation';
import { TopBar } from './TopBar';

describe('TopBar', () => {
  beforeEach(() => {
    mockSession = { data: null };
  });

  it('marks the active nav with aria-current', () => {
    vi.spyOn(nav, 'usePathname').mockReturnValue('/shepherd/step1');
    render(<TopBar />);
    expect(screen.getByRole('link', { name: /shepherd/i })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: /dashboard/i })).not.toHaveAttribute('aria-current');
  });

  it('includes Agents in primary nav and marks it active on /studio/agents', () => {
    vi.spyOn(nav, 'usePathname').mockReturnValue('/studio/agents');
    render(<TopBar />);
    expect(screen.getByRole('link', { name: /agents/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /agents/i })).toHaveAttribute('aria-current', 'page');
  });

  it('does not render TenantSwitcher in TopBar anymore (moved to Account menu)', () => {
    vi.spyOn(nav, 'usePathname').mockReturnValue('/studio/agents');
    render(<TopBar />);
    expect(screen.queryByTestId('tenant')).not.toBeInTheDocument();
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

  it('renders New Agent CTA only when canCreate', () => {
    // Not visible by default
    vi.spyOn(nav, 'usePathname').mockReturnValue('/');
    render(<TopBar />);
    expect(screen.queryByTestId('new-agent')).not.toBeInTheDocument();

    // Visible when canCreate
    mockSession = { data: { canCreate: true } };
    vi.spyOn(nav, 'usePathname').mockReturnValue('/');
    render(<TopBar />);
    expect(screen.getByTestId('new-agent')).toBeInTheDocument();
  });

  it('opens drawer when hamburger clicked and closes on drawer click', async () => {
    vi.spyOn(nav, 'usePathname').mockReturnValue('/');
    render(<TopBar />);
    const user = userEvent.setup();
    // drawer initially closed
    expect(screen.getByTestId('drawer')).toHaveAttribute('data-open', 'false');
    // click the hamburger (button with aria-label Open navigation)
    await user.click(screen.getByRole('button', { name: /open navigation/i }));
    expect(screen.getByTestId('drawer')).toHaveAttribute('data-open', 'true');
    // clicking on the drawer mock triggers onClose
    await user.click(screen.getByTestId('drawer'));
    expect(screen.getByTestId('drawer')).toHaveAttribute('data-open', 'false');
  });

  it('adds elevation on scroll', async () => {
    vi.spyOn(nav, 'usePathname').mockReturnValue('/');
    render(<TopBar />);
    const header = screen.getByRole('banner');
    // Initially at top
    expect(header).toHaveAttribute('data-elevated', 'false');
    // Simulate scroll
    Object.defineProperty(window, 'scrollY', { value: 10, writable: true });
    await act(async () => {
      window.dispatchEvent(new Event('scroll'));
    });
    await waitFor(() => expect(header).toHaveAttribute('data-elevated', 'true'));
  });

  it('exposes accessible labels for nav and hamburger', () => {
    vi.spyOn(nav, 'usePathname').mockReturnValue('/');
    render(<TopBar />);
    // Hamburger button has clear aria-label
    expect(screen.getByRole('button', { name: /open navigation/i })).toBeInTheDocument();
    // Desktop nav landmark has an accessible name
    expect(screen.getByRole('navigation', { name: /main navigation/i })).toBeInTheDocument();
  });
});
