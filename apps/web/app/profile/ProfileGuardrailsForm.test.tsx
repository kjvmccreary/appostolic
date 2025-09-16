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
});
