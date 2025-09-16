import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { BioEditor } from './BioEditor';

const originalFetch = global.fetch;

describe('BioEditor', () => {
  beforeEach(() => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200 });
  });
  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('disables save when unchanged', () => {
    render(<BioEditor initial={{ format: 'markdown', content: 'Hello' }} />);
    expect(screen.getByRole('button', { name: /Save Bio/i })).toBeDisabled();
  });

  it('enables and submits new bio', async () => {
    render(<BioEditor initial={null} />);
    const ta = screen.getByLabelText(/Bio \(Markdown\)/i);
    fireEvent.change(ta, { target: { value: 'My **bio**' } });
    const btn = screen.getByRole('button', { name: /Save Bio/i });
    expect(btn).not.toBeDisabled();
    fireEvent.click(btn);
    await waitFor(() => expect(screen.getByText(/Bio saved/i)).toBeInTheDocument());
    expect(global.fetch).toHaveBeenCalledWith(
      '/api-proxy/users/me',
      expect.objectContaining({ method: 'PUT' }),
    );
  });

  it('clears bio sending null', async () => {
    render(<BioEditor initial={{ format: 'markdown', content: 'Existing' }} />);
    // change then clear to produce dirty empty state
    const ta = screen.getByLabelText(/Bio \(Markdown\)/i);
    fireEvent.change(ta, { target: { value: '' } });
    const btn = screen.getByRole('button', { name: /Save Bio/i });
    expect(btn).not.toBeDisabled();
    fireEvent.click(btn);
    await waitFor(() => expect(screen.getByText(/Bio saved/i)).toBeInTheDocument());
    // body should include bio: null
    // Casting fetch to any within test scope to inspect mock call arguments (Vitest provides .mock).
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const fetchMock = global.fetch as any;
    const call = fetchMock.mock.calls[0];
    const body = JSON.parse((call[1] as RequestInit).body as string);
    expect(body).toEqual({ profile: { bio: null } });
  });

  it('blocks over-limit bio', () => {
    render(<BioEditor initial={null} maxChars={10} />);
    const ta = screen.getByLabelText(/Bio \(Markdown\)/i);
    fireEvent.change(ta, { target: { value: 'This is definitely longer than ten chars' } });
    const btn = screen.getByRole('button', { name: /Save Bio/i });
    expect(btn).toBeDisabled();
    expect(screen.getByRole('alert')).toHaveTextContent(/too long/i);
  });

  it('shows error on failure', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 500 });
    render(<BioEditor initial={null} />);
    const ta = screen.getByLabelText(/Bio \(Markdown\)/i);
    fireEvent.change(ta, { target: { value: 'Test' } });
    fireEvent.click(screen.getByRole('button', { name: /Save Bio/i }));
    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument());
  });
});
