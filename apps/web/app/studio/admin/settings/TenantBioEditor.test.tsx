import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { TenantBioEditor } from './TenantBioEditor';

const originalFetch = global.fetch;

describe('TenantBioEditor', () => {
  beforeEach(() => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200 });
  });
  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('keeps save disabled when unchanged and does not submit', () => {
    render(<TenantBioEditor initial={{ format: 'markdown', content: 'Hello' }} />);
    const btn = screen.getByRole('button', { name: /Save Bio/i });
    expect(btn).toBeDisabled();
    fireEvent.click(btn);
    expect(global.fetch).not.toHaveBeenCalled();
  });

  it('enables and submits new bio (minimal patch)', async () => {
    render(<TenantBioEditor initial={null} />);
    const ta = screen.getByPlaceholderText(/organization bio/i);
    fireEvent.change(ta, { target: { value: 'Org **bio**' } });
    const btn = screen.getByRole('button', { name: /Save Bio/i });
    expect(btn).toBeEnabled();
    fireEvent.click(btn);
    await waitFor(() => expect(screen.getByText(/Bio saved/i)).toBeInTheDocument());
    expect(global.fetch).toHaveBeenCalledWith('/api-proxy/tenants/settings', expect.any(Object));
  });
});
