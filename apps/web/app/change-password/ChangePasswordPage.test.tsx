import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import ChangePasswordPage from './page';

const originalFetch = global.fetch;

describe('ChangePasswordPage', () => {
  beforeEach(() => {
    global.fetch = vi.fn().mockResolvedValue({ status: 204 });
  });
  afterEach(() => {
    global.fetch = originalFetch;
  });

  function fill(base: { current?: string; pw?: string; confirm?: string }) {
    if (base.current)
      fireEvent.change(screen.getByLabelText(/Current password/i), {
        target: { value: base.current },
      });
    if (base.pw)
      fireEvent.change(screen.getByLabelText(/^New password/i), { target: { value: base.pw } });
    if (base.confirm)
      fireEvent.change(screen.getByLabelText(/Confirm new password/i), {
        target: { value: base.confirm },
      });
  }

  it('blocks weak password client-side', async () => {
    render(<ChangePasswordPage />);
    fill({ current: 'oldPass1', pw: 'short1', confirm: 'short1' });
    fireEvent.click(screen.getByRole('button', { name: /Save Password/i }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/at least 8/);
    expect(global.fetch).not.toHaveBeenCalled();
  });

  it('blocks mismatch confirm client-side', async () => {
    render(<ChangePasswordPage />);
    fill({ current: 'oldPass1', pw: 'LongerPass1', confirm: 'Different1' });
    fireEvent.click(screen.getByRole('button', { name: /Save Password/i }));
    const alerts = await screen.findAllByRole('alert');
    expect(alerts.some((a) => /match/i.test(a.textContent || ''))).toBe(true);
    expect(global.fetch).not.toHaveBeenCalled();
  });

  it('handles success path', async () => {
    render(<ChangePasswordPage />);
    fill({ current: 'oldPass1', pw: 'LongerPass1', confirm: 'LongerPass1' });
    fireEvent.click(screen.getByRole('button', { name: /Save Password/i }));
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/Password changed/));
    expect(global.fetch).toHaveBeenCalledWith('/api-proxy/users/me/password', expect.any(Object));
  });

  it('maps 400 current password error', async () => {
    global.fetch = vi.fn().mockResolvedValue({ status: 400 });
    render(<ChangePasswordPage />);
    fill({ current: 'wrong', pw: 'LongerPass1', confirm: 'LongerPass1' });
    fireEvent.click(screen.getByRole('button', { name: /Save Password/i }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/incorrect/);
  });
});
