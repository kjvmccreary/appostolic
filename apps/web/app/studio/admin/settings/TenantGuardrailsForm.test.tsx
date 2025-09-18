import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { TenantGuardrailsForm } from './TenantGuardrailsForm';

const originalFetch = global.fetch;

describe('TenantGuardrailsForm', () => {
  beforeEach(() => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200 });
  });
  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('adds and removes favorite author chip', () => {
    render(<TenantGuardrailsForm initial={{}} />);
    const input = screen.getByLabelText(/Add author/i);
    fireEvent.change(input, { target: { value: 'Lewis' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(screen.getByText('Lewis')).toBeInTheDocument();
    const removeBtn = screen.getByRole('button', { name: /Remove Lewis/ });
    fireEvent.click(removeBtn);
    expect(screen.queryByText('Lewis')).not.toBeInTheDocument();
  });

  it('submits patch including denominations array', async () => {
    const presets = [{ id: 'baptist', name: 'Baptist' }];
    render(<TenantGuardrailsForm initial={{}} presets={presets} />);
    const search = screen.getByPlaceholderText(/Search denominations/i);
    fireEvent.change(search, { target: { value: 'bap' } });
    fireEvent.click(screen.getByRole('button', { name: /Add Baptist/i }));
    fireEvent.click(screen.getByRole('button', { name: /Save Guardrails/i }));
    await waitFor(() => expect(screen.getByText(/updated/i)).toBeInTheDocument());
    expect(global.fetch).toHaveBeenCalledWith('/api-proxy/tenants/settings', expect.any(Object));
  });
});
