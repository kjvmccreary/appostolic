import { describe, it, expect, vi, beforeEach } from 'vitest';
import React from 'react';
import { render, screen } from '../../test/utils';

// The root page now renders a real dashboard (unauthenticated users still redirect server-side).
// We mock only the authenticated path here, asserting section headings render.

vi.mock('next-auth', () => ({
  getServerSession: vi.fn(),
}));

// Mock next/navigation redirect so we can detect unauthenticated behavior without crashing tests
const redirectMock = vi.fn();
vi.mock('next/navigation', () => ({
  redirect: (...args: unknown[]) => redirectMock(...args),
}));

import RootPage from '../../app/page';
import { getServerSession } from 'next-auth';

describe('RootPage dashboard', () => {
  beforeEach(() => {
    redirectMock.mockReset();
    (getServerSession as unknown as { mockReset: () => void }).mockReset?.();
  });

  it('redirects unauthenticated users to /login (server)', async () => {
    (
      getServerSession as unknown as { mockResolvedValue: (v: unknown) => void }
    ).mockResolvedValue?.(null);
    // Execute the server component function
    await RootPage();
    expect(redirectMock).toHaveBeenCalledWith('/login');
  });

  it('renders dashboard sections for authenticated users', async () => {
    (
      getServerSession as unknown as { mockResolvedValue: (v: unknown) => void }
    ).mockResolvedValue?.({ user: { email: 'u@example.com' } });
    // Invoke the server component to get its JSX and then render it
    const jsx = await RootPage();
    render(jsx as React.ReactElement);
    // Heading + representative section headings
    expect(
      await screen.findByRole('heading', { name: /dashboard/i, level: 1 }),
    ).toBeInTheDocument();
    for (const h2 of [
      /quick start/i,
      /recent lessons/i,
      /plan & usage/i,
      /templates/i,
      /guardrails/i,
      /marketplace/i,
    ]) {
      expect(screen.getByRole('heading', { name: h2 })).toBeInTheDocument();
    }
  });
});
