import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { TenantLogoUpload } from './TenantLogoUpload';

// jsdom doesn't implement createObjectURL; stub it
const originalCreate = global.URL.createObjectURL;
const originalRevoke = global.URL.revokeObjectURL;

describe('TenantLogoUpload', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    // minimal stubs
    (global.URL.createObjectURL as unknown as (obj: Blob) => string) = vi.fn(() => 'blob:fake');
    (global.URL.revokeObjectURL as unknown as (url: string) => void) = vi.fn();
  });

  afterAll(() => {
    global.URL.createObjectURL = originalCreate;
    global.URL.revokeObjectURL = originalRevoke;
  });

  it('uploads selected file via POST and shows success', async () => {
    const userFile = new File(['data'], 'logo.png', { type: 'image/png' });
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ logo: { url: 'https://cdn/logo.png' } }),
    });
    (globalThis as unknown as { fetch: typeof fetch }).fetch = fetchMock as unknown as typeof fetch;

    render(<TenantLogoUpload initialUrl={null} />);

    // trigger choose via hidden input
    const input = screen.getByLabelText('Choose tenant logo image') as HTMLInputElement;
    // simulate user picking a file
    fireEvent.change(input, { target: { files: [userFile] } });

    // click Upload
    fireEvent.click(screen.getByText('Upload'));

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const [url, opts] = fetchMock.mock.calls[0];
    expect(url).toBe('/api-proxy/tenants/logo');
    expect(opts.method).toBe('POST');

    expect(await screen.findByText(/Logo updated\./i)).toBeInTheDocument();
  });

  it('removes existing server logo via DELETE', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, text: async () => '' });
    (globalThis as unknown as { fetch: typeof fetch }).fetch = fetchMock as unknown as typeof fetch;

    render(<TenantLogoUpload initialUrl={'https://cdn/existing.png'} />);

    fireEvent.click(screen.getByRole('button', { name: /Remove/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const [url, opts] = fetchMock.mock.calls[0];
    expect(url).toBe('/api-proxy/tenants/logo');
    expect(opts.method).toBe('DELETE');

    expect(await screen.findByText(/Logo removed\./i)).toBeInTheDocument();
  });

  it('clears a just-selected blob without network', async () => {
    const fetchMock = vi.fn();
    (globalThis as unknown as { fetch: typeof fetch }).fetch = fetchMock as unknown as typeof fetch;

    render(<TenantLogoUpload initialUrl={null} />);
    const input = screen.getByLabelText('Choose tenant logo image') as HTMLInputElement;
    const userFile = new File(['data'], 'logo.png', { type: 'image/png' });
    fireEvent.change(input, { target: { files: [userFile] } });

    fireEvent.click(screen.getByRole('button', { name: /Remove/i }));

    // No network call should be made
    expect(fetchMock).not.toHaveBeenCalled();
    expect(await screen.findByText(/Selection cleared\./i)).toBeInTheDocument();
  });
});
