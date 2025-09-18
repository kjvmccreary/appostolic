import React from 'react';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import TenantSettingsForm from './TenantSettingsForm';

const originalFetch = global.fetch;

describe('TenantSettingsForm', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });
  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('submits displayName patch and shows success', async () => {
    const fetchSpy = vi.fn().mockResolvedValue({ ok: true, status: 200 });
    global.fetch = fetchSpy as unknown as typeof fetch;
    render(<TenantSettingsForm initial={{ displayName: 'Old Org' }} />);
    fireEvent.change(screen.getByLabelText(/Organization Display Name/i), {
      target: { value: 'New Org' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Save Changes/i }));
    await waitFor(() => expect(screen.getByText(/Settings updated/i)).toBeInTheDocument());
    expect(fetchSpy).toHaveBeenCalledWith('/api-proxy/tenants/settings', expect.any(Object));
    const init = fetchSpy.mock.calls[0][1] as { body: string };
    const body = JSON.parse(init.body) as { displayName?: string };
    expect(body.displayName).toBe('New Org');
  });

  it('normalizes website without protocol before submit', async () => {
    const fetchSpy = vi.fn().mockResolvedValue({ ok: true, status: 200 });
    global.fetch = fetchSpy as unknown as typeof fetch;
    render(<TenantSettingsForm initial={{ contact: { website: '' } }} />);
    fireEvent.change(screen.getByLabelText(/Website/i), { target: { value: 'example.com' } });
    fireEvent.click(screen.getByRole('button', { name: /Save Changes/i }));
    await waitFor(() => expect(screen.getByText(/Settings updated/i)).toBeInTheDocument());
    const init = fetchSpy.mock.calls[0][1] as { body: string };
    const body = JSON.parse(init.body) as { contact?: { website?: string } };
    expect(body.contact?.website).toBe('https://example.com');
  });

  it('shows error on failed update', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 500 }) as unknown as typeof fetch;
    render(<TenantSettingsForm initial={{}} />);
    fireEvent.click(screen.getByRole('button', { name: /Save Changes/i }));
    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument());
  });
});
