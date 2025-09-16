import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, waitFor } from '../../test/utils';
import userEvent from '@testing-library/user-event';
import type { Session } from 'next-auth';
import type { SignInResponse } from 'next-auth/react';
import { http, HttpResponse } from 'msw';

// MSW server from global (exposed in setup.ts)
const server: import('msw/node').SetupServer = (globalThis as unknown as { __mswServer: unknown })
  .__mswServer as import('msw/node').SetupServer;

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
    // default CSRF endpoint
    server.use(
      http.get('http://localhost/api/auth/csrf', () =>
        HttpResponse.json({ csrfToken: 'test-token' }),
      ),
    );
  });

  afterEach(() => {
    replaceMock.mockReset();
  });

  it('renders a hidden CSRF token input', async () => {
    render(<LoginPage />);
    await waitFor(() => {
      const input = document.querySelector('input[name="csrfToken"]') as HTMLInputElement | null;
      expect(input).toBeTruthy();
      expect(input!.value).toBe('test-token');
    });
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
      expect(replaceMock).toHaveBeenCalledWith('/studio/agents');
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
});
