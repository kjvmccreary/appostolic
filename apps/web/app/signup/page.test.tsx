import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '../../test/utils';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

// MSW server from global (exposed in setup.ts)
const server: import('msw/node').SetupServer = (globalThis as unknown as { __mswServer: unknown })
  .__mswServer as import('msw/node').SetupServer;

const replaceMock = vi.fn();

vi.mock('next/navigation', () => ({
  useRouter: () => ({ replace: replaceMock }),
  useSearchParams: () => ({ get: (k: string) => (k === 'next' ? '/select-tenant' : null) }),
}));

vi.mock('next-auth/react', () => ({
  signIn: vi.fn(),
}));

// Mock serverEnv before importing component to satisfy API_BASE requirement
vi.mock('../../src/lib/serverEnv', () => ({
  API_BASE: 'http://localhost',
}));

import SignupPage from './page';
import { signIn } from 'next-auth/react';
import type { SignInResponse } from 'next-auth/react';

describe('SignupPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows inline error when API returns failure', async () => {
    server.use(
      http.post('http://localhost/api/auth/signup', () =>
        HttpResponse.text('Email in use', { status: 400 }),
      ),
    );

    render(<SignupPage />);

    await userEvent.type(screen.getByLabelText(/email/i), 'taken@example.com');
    await userEvent.type(screen.getByLabelText(/password/i), 'Proverbs!09');
    await userEvent.click(screen.getByRole('button', { name: /sign up/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/email in use/i);
    expect(replaceMock).not.toHaveBeenCalled();
  });

  it('signs in after successful signup and redirects to next', async () => {
    server.use(
      http.post('http://localhost/api/auth/signup', () =>
        HttpResponse.json({ id: 'u1', email: 'k@example.com' }, { status: 201 }),
      ),
    );
    vi.mocked(signIn).mockResolvedValue({ error: undefined } as unknown as SignInResponse);

    render(<SignupPage />);

    await userEvent.type(screen.getByLabelText(/email/i), 'k@example.com');
    await userEvent.type(screen.getByLabelText(/password/i), 'Proverbs!09');
    await userEvent.click(screen.getByRole('button', { name: /sign up/i }));

    await waitFor(() => {
      expect(signIn).toHaveBeenCalledWith(
        'credentials',
        expect.objectContaining({
          email: 'k@example.com',
          password: 'Proverbs!09',
          redirect: false,
          callbackUrl: '/select-tenant',
        }),
      );
      expect(replaceMock).toHaveBeenCalledWith('/select-tenant');
    });
  });
});
