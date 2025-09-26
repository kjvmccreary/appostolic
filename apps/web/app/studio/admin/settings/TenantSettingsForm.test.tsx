import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import TenantSettingsForm from './TenantSettingsForm';

const server = (
  globalThis as {
    __mswServer?: import('msw/node').SetupServer;
  }
).__mswServer!;

describe('TenantSettingsForm', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('submits displayName patch and shows success', async () => {
    let intercepted: { body: unknown; headers: Headers } | null = null;
    server.use(
      http.put('http://localhost/api-proxy/tenants/settings', async ({ request }) => {
        intercepted = {
          body: await request.json(),
          headers: request.headers,
        };
        return HttpResponse.json({ ok: true });
      }),
    );
    render(<TenantSettingsForm initial={{ displayName: 'Old Org' }} />);
    fireEvent.change(screen.getByLabelText(/Organization Display Name/i), {
      target: { value: 'New Org' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Save Changes/i }));
    await waitFor(() => expect(screen.getByText(/Settings updated/i)).toBeInTheDocument());
    expect(intercepted).not.toBeNull();
    const captured = intercepted!;
    const body = captured.body as { displayName?: string };
    expect(body.displayName).toBe('New Org');
    expect(captured.headers.get('content-type')).toMatch(/application\/json/i);
  });

  it('normalizes website without protocol before submit', async () => {
    let intercepted: { body: unknown } | null = null;
    server.use(
      http.put('http://localhost/api-proxy/tenants/settings', async ({ request }) => {
        intercepted = { body: await request.json() };
        return HttpResponse.json({ ok: true });
      }),
    );
    render(<TenantSettingsForm initial={{ contact: { website: '' } }} />);
    fireEvent.change(screen.getByLabelText(/Website/i), { target: { value: 'example.com' } });
    fireEvent.click(screen.getByRole('button', { name: /Save Changes/i }));
    await waitFor(() => expect(screen.getByText(/Settings updated/i)).toBeInTheDocument());
    const captured = intercepted!;
    const body = captured.body as { contact?: { website?: string } };
    expect(body?.contact?.website).toBe('https://example.com');
  });

  it('shows error on failed update', async () => {
    server.use(
      http.put('http://localhost/api-proxy/tenants/settings', () =>
        HttpResponse.json({ error: 'fail' }, { status: 500 }),
      ),
    );
    render(<TenantSettingsForm initial={{}} />);
    fireEvent.click(screen.getByRole('button', { name: /Save Changes/i }));
    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument());
  });
});
