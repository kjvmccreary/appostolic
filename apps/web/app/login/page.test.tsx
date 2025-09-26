import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor } from '../../test/utils';
import userEvent from '@testing-library/user-event';
import type { Session } from 'next-auth';
import type { SignInResponse } from 'next-auth/react';

// Mocks for next-auth/react and next/navigation
const replaceMock = vi.fn();

vi.mock('next-auth/react', () => {
  const mockedUseSession = vi.fn(() => ({ data: null as Session | null }));
  return {
    signIn: vi.fn(),
    useSession: mockedUseSession,
  };
});

vi.mock('next/navigation', () => {
  return {
    useRouter: () => ({ replace: replaceMock }),
    useSearchParams: () => ({ get: (k: string) => (k === 'next' ? '/studio/agents' : null) }),
  };
});

// Import component under test AFTER mocks
import LoginPage from './page';
import { signIn, useSession } from 'next-auth/react';

describe('LoginPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    // default session: unauthenticated
    vi.mocked(useSession).mockReturnValue({ data: null } as unknown as ReturnType<
      typeof useSession
    >);
  });

  afterEach(() => {
    replaceMock.mockReset();
  });

  it('shows inline error on invalid credentials and does not redirect', async () => {
    vi.mocked(signIn).mockResolvedValue({
      error: 'CredentialsSignin',
    } as unknown as SignInResponse);

    render(<LoginPage />);

    await userEvent.type(screen.getByLabelText(/email/i), 'x@example.com');
    await userEvent.type(screen.getByLabelText(/password/i), 'wrongpass');
    await userEvent.click(screen.getByRole('button', { name: /sign in/i }));

    // Error should appear
    expect(await screen.findByText(/invalid email or password/i)).toBeInTheDocument();
    // No redirect
    expect(replaceMock).not.toHaveBeenCalled();
  });

  it('redirects to next on successful sign-in', async () => {
    vi.mocked(signIn).mockResolvedValue({ error: undefined } as unknown as SignInResponse);

    render(<LoginPage />);

    await userEvent.type(screen.getByLabelText(/email/i), 'u@example.com');
    await userEvent.type(screen.getByLabelText(/password/i), 'pass');
    await userEvent.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => {
      expect(replaceMock).toHaveBeenCalledWith('/select-tenant?next=%2Fstudio%2Fagents&reselect=1');
    });
  });

  it('renders a link to forgot-password', async () => {
    render(<LoginPage />);
    const link = await screen.findByRole('link', { name: /forgot password/i });
    expect(link).toHaveAttribute('href', '/forgot-password');
  });

  it('includes Sign up and Magic Link links with next param', async () => {
    render(<LoginPage />);
    const signup = await screen.findByRole('link', { name: /sign up/i });
    expect(signup).toHaveAttribute('href', '/signup?next=%2Fstudio%2Fagents');
    const magic = await screen.findByRole('link', { name: /use magic link/i });
    expect(magic).toHaveAttribute('href', '/magic/request?next=%2Fstudio%2Fagents');
  });

  it('applies unified input and button styling classes', async () => {
    render(<LoginPage />);
    const email = await screen.findByLabelText(/email/i);
    const password = await screen.findByLabelText(/password/i);
    const submit = await screen.findByRole('button', { name: /sign in/i });
    // Check a representative subset of class tokens
    for (const el of [email, password]) {
      expect(el.className).toMatch(/rounded-md/);
      expect(el.className).toMatch(/border-line/);
      expect(el.className).toMatch(/focus:ring-2/);
    }
    expect(submit.className).toMatch(/bg-\[var\(--color-accent-600\)\]/);
    expect(submit.className).toMatch(/rounded-md/);
  });
});
