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

  it('enables and submits new bio (minimal patch)', async () => {
    render(<BioEditor initial={null} />);
    const ta = screen.getByPlaceholderText(/write your bio/i);
    fireEvent.change(ta, { target: { value: 'My **bio**' } });
    const btn = screen.getByRole('button', { name: /Save Bio/i });
    expect(btn).not.toBeDisabled();
    fireEvent.click(btn);
    await waitFor(() => expect(screen.getByText(/Bio saved/i)).toBeInTheDocument());
    const mockFetch = global.fetch as unknown as { mock: { calls: unknown[][] } };
    const call = mockFetch.mock.calls[0];
    const body = JSON.parse((call[1] as RequestInit).body as string);
    expect(body).toEqual({ bio: { format: 'markdown', content: 'My **bio**' } });
  });

  it('clears bio sending null when baseline non-empty', async () => {
    render(<BioEditor initial={{ format: 'markdown', content: 'Existing' }} />);
    const ta = screen.getByPlaceholderText(/write your bio/i);
    fireEvent.change(ta, { target: { value: '' } });
    const btn = screen.getByRole('button', { name: /Save Bio/i });
    expect(btn).not.toBeDisabled();
    fireEvent.click(btn);
    await waitFor(() => expect(screen.getByText(/Bio saved/i)).toBeInTheDocument());
    const mockFetch = global.fetch as unknown as { mock: { calls: unknown[][] } };
    const call = mockFetch.mock.calls[0];
    const body = JSON.parse((call[1] as RequestInit).body as string);
    expect(body).toEqual({ bio: null });
  });

  it('does not submit when value returns to baseline', async () => {
    render(<BioEditor initial={{ format: 'markdown', content: 'Same' }} />);
    const ta = screen.getByPlaceholderText(/write your bio/i);
    fireEvent.change(ta, { target: { value: 'Different' } });
    fireEvent.change(ta, { target: { value: 'Same' } });
    expect(screen.getByRole('button', { name: /Save Bio/i })).toBeDisabled();
  });

  it('blocks over-limit bio', () => {
    render(<BioEditor initial={null} maxChars={10} />);
    const ta = screen.getByPlaceholderText(/write your bio/i);
    fireEvent.change(ta, { target: { value: 'This is definitely longer than ten chars' } });
    const btn = screen.getByRole('button', { name: /Save Bio/i });
    expect(btn).toBeDisabled();
  });

  it('shows error on failure', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 500 });
    render(<BioEditor initial={null} />);
    const ta = screen.getByPlaceholderText(/write your bio/i);
    fireEvent.change(ta, { target: { value: 'Test' } });
    fireEvent.click(screen.getByRole('button', { name: /Save Bio/i }));
    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument());
  });

  it('renders soft line breaks in preview', () => {
    render(<BioEditor initial={null} />);
    const ta = screen.getByPlaceholderText(/write your bio/i);
    fireEvent.change(ta, { target: { value: 'Line one\nLine two' } });
    fireEvent.click(screen.getByRole('tab', { name: /Preview/i }));
    // remark-breaks turns single newlines into <br/> elements inside one paragraph.
    const para = screen.getByText(/Line one/).closest('p');
    expect(para).toBeInTheDocument();
    // combined text should contain both lines
    expect(para?.textContent).toContain('Line one');
    expect(para?.textContent).toContain('Line two');
    // Ensure exactly one <br> inserted (soft break)
    const brs = para?.querySelectorAll('br');
    expect(brs?.length).toBe(1);
  });
});
