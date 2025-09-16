import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { ProfileGuardrailsForm } from './ProfileGuardrailsForm';

const originalFetch = global.fetch;

describe('ProfileGuardrailsForm', () => {
  beforeEach(() => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200 });
  });
  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('adds and removes favorite author chip', () => {
    render(<ProfileGuardrailsForm initial={{}} />);
    const input = screen.getByLabelText(/Add author/i);
    fireEvent.change(input, { target: { value: 'Lewis' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(screen.getByText('Lewis')).toBeInTheDocument();
    const removeBtn = screen.getByRole('button', { name: /Remove Lewis/ });
    fireEvent.click(removeBtn);
    expect(screen.queryByText('Lewis')).not.toBeInTheDocument();
  });

  it('submits patch with lesson format', async () => {
    render(<ProfileGuardrailsForm initial={{ lessonFormat: '' }} />);
    fireEvent.change(screen.getByLabelText(/Preferred Lesson Format/i), {
      target: { value: 'Games' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Save Guardrails/i }));
    await waitFor(() => expect(screen.getByText(/Guardrails updated/i)).toBeInTheDocument());
    expect(global.fetch).toHaveBeenCalledWith('/api-proxy/users/me', expect.any(Object));
  });

  it('adds first denomination and auto-fills alignment when empty', async () => {
    const presets = [
      { id: 'anglican', name: 'Anglican' },
      { id: 'baptist', name: 'Baptist' },
    ];
    render(<ProfileGuardrailsForm initial={{}} presets={presets} />);
    const search = screen.getByPlaceholderText(/Search denominations/i);
    fireEvent.change(search, { target: { value: 'ang' } });
    const addBtn = screen.getByRole('button', { name: /Add Anglican/i });
    fireEvent.click(addBtn);
    // Chip appears
    expect(screen.getByLabelText('Anglican')).toBeInTheDocument();
    // Alignment auto-filled
    const alignment = screen.getByLabelText(/Denomination Alignment/i) as HTMLInputElement;
    expect(alignment.value).toBe('Anglican');
  });

  it('does not overwrite alignment when adding additional denominations', () => {
    const presets = [
      { id: 'anglican', name: 'Anglican' },
      { id: 'baptist', name: 'Baptist' },
    ];
    render(
      <ProfileGuardrailsForm
        initial={{ denominationAlignment: 'Custom Align' }}
        presets={presets}
      />,
    );
    const search = screen.getByPlaceholderText(/Search denominations/i);
    fireEvent.change(search, { target: { value: 'ang' } });
    fireEvent.click(screen.getByRole('button', { name: /Add Anglican/i }));
    const alignment = screen.getByLabelText(/Denomination Alignment/i) as HTMLInputElement;
    expect(alignment.value).toBe('Custom Align');
    fireEvent.change(search, { target: { value: 'bap' } });
    fireEvent.click(screen.getByRole('button', { name: /Add Baptist/i }));
    expect(alignment.value).toBe('Custom Align');
  });

  it('removes a selected denomination chip', () => {
    const presets = [{ id: 'baptist', name: 'Baptist' }];
    render(<ProfileGuardrailsForm initial={{ denominations: ['baptist'] }} presets={presets} />);
    const chip = screen.getByLabelText('Baptist');
    expect(chip).toBeInTheDocument();
    const removeBtn = screen.getByRole('button', { name: /Remove Baptist/i });
    fireEvent.click(removeBtn);
    expect(screen.queryByLabelText('Baptist')).not.toBeInTheDocument();
  });

  it('submits patch including denominations array', async () => {
    const presets = [{ id: 'baptist', name: 'Baptist' }];
    render(<ProfileGuardrailsForm initial={{}} presets={presets} />);
    const search = screen.getByPlaceholderText(/Search denominations/i);
    fireEvent.change(search, { target: { value: 'bap' } });
    fireEvent.click(screen.getByRole('button', { name: /Add Baptist/i }));
    fireEvent.click(screen.getByRole('button', { name: /Save Guardrails/i }));
    await waitFor(() => expect(screen.getByText(/Guardrails updated/i)).toBeInTheDocument());
    expect(global.fetch).toHaveBeenCalled();
    type FetchCall = [input: string, init?: { body?: unknown }];
    interface MockWithCalls {
      mock: { calls: FetchCall[] };
    }
    const mock = global.fetch as unknown as MockWithCalls;
    const call = mock.mock.calls.find((c) => c[0] === '/api-proxy/users/me');
    expect(call).toBeTruthy();
    if (!call) throw new Error('fetch call not found');
    const body = JSON.parse((call[1]?.body as string) || '{}');
    expect(body.profile.presets.denominations).toEqual(['baptist']);
  });
});
