import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import React from 'react';
import { TenantSelector } from './TenantSelector';

describe('TenantSelector', () => {
  beforeEach(() => {
    // @ts-expect-error mock fetch for tests
    global.fetch = vi.fn(async () => ({ ok: true }));
  });

  it('submits selected tenant to API', async () => {
    render(<TenantSelector />);

    const input = screen.getByLabelText('Tenant');
    fireEvent.change(input, { target: { value: 'acme' } });

    const btn = screen.getByRole('button', { name: /set tenant/i });
    expect(btn).toBeEnabled();

    fireEvent.click(btn);

    await waitFor(() => {
      expect(global.fetch).toHaveBeenCalledTimes(1);
    });

    expect(global.fetch).toHaveBeenCalledWith(
      '/api/tenant/select',
      expect.objectContaining({
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tenant: 'acme' }),
      }),
    );
  });
});
