import { describe, it, expect, vi } from 'vitest';
import { render, waitFor } from '@testing-library/react';

// Mock next-auth and next/navigation before importing the page
const signOutMock = vi.fn().mockResolvedValue(undefined);
const replaceMock = vi.fn();

vi.mock('next-auth/react', () => ({
  signOut: signOutMock,
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ replace: replaceMock }),
}));

describe('/logout page', () => {
  it('calls signOut and redirects to /login', async () => {
    const { default: LogoutPage } = await import('./page');

    render(<LogoutPage />);

    await waitFor(() => {
      expect(signOutMock).toHaveBeenCalledWith({ redirect: false });
    });

    await waitFor(() => {
      expect(replaceMock).toHaveBeenCalledWith('/login?loggedOut=1');
    });
  });
});
