import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { ProfileEditForm } from './ProfileEditForm';

// Basic fetch mock
const originalFetch = global.fetch;

describe('ProfileEditForm', () => {
  beforeEach(() => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200 });
  });
  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('submits patch and shows success', async () => {
    render(<ProfileEditForm initial={{ display: 'Old Name' }} />);
    const input = screen.getByLabelText(/Display Name/i) as HTMLInputElement;
    fireEvent.change(input, { target: { value: 'New Name' } });
    fireEvent.click(screen.getByRole('button', { name: /Save Changes/i }));
    await waitFor(() => expect(screen.getByText(/Profile updated/i)).toBeInTheDocument());
    expect(global.fetch).toHaveBeenCalledWith('/api-proxy/users/me', expect.any(Object));
  });

  it('shows error on failure', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 500 });
    render(<ProfileEditForm initial={{ display: 'Name' }} />);
    fireEvent.click(screen.getByRole('button', { name: /Save Changes/i }));
    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument());
  });
});
