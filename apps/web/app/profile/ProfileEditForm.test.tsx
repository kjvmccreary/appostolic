import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { ProfileEditForm } from './ProfileEditForm';

// Basic fetch mock
const originalFetch = global.fetch;

describe('ProfileEditForm', () => {
  beforeEach(() => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200 });
  });
  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('submits patch and shows success', async () => {
    render(<ProfileEditForm initial={{ display: 'Old Name' }} />);
    const input = screen.getByLabelText(/Display Name/i) as HTMLInputElement;
    fireEvent.change(input, { target: { value: 'New Name' } });
    fireEvent.click(screen.getByRole('button', { name: /Save Changes/i }));
    await waitFor(() => expect(screen.getByText(/Profile updated/i)).toBeInTheDocument());
    expect(global.fetch).toHaveBeenCalledWith('/api-proxy/users/me', expect.any(Object));
  });

  it('shows error on failure', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 500 });
    render(<ProfileEditForm initial={{ display: 'Name' }} />);
    fireEvent.click(screen.getByRole('button', { name: /Save Changes/i }));
    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument());
  });

  it('renders timezone dropdown and submits selected timezone', async () => {
    const fetchSpy = vi.fn().mockResolvedValue({ ok: true, status: 200 });
    global.fetch = fetchSpy;
    render(<ProfileEditForm initial={{ display: 'Name', timezone: '' }} />);
    const select = screen.getByLabelText(/Timezone/i) as HTMLSelectElement;
    // pick first non-empty option
    await waitFor(() => expect(select.options.length).toBeGreaterThan(1));
    const firstValue = Array.from(select.options).find((o) => o.value)?.value;
    if (firstValue) {
      fireEvent.change(select, { target: { value: firstValue } });
    }
    fireEvent.click(screen.getByRole('button', { name: /Save Changes/i }));
    await waitFor(() => expect(screen.getByText(/Profile updated/i)).toBeInTheDocument());
    const callInit = fetchSpy.mock.calls[0][1] as unknown as { body: string };
    const body = JSON.parse(callInit.body) as { contact: { timezone: string } };
    expect(body.contact.timezone).toBe(firstValue);
  });

  it('submits first and last name changes', async () => {
    const fetchSpy = vi.fn().mockResolvedValue({ ok: true, status: 200 });
    global.fetch = fetchSpy;
    render(<ProfileEditForm initial={{ first: 'Jane', last: 'Doe' }} />);
    const firstInput = screen.getByLabelText(/First Name/i) as HTMLInputElement;
    const lastInput = screen.getByLabelText(/Last Name/i) as HTMLInputElement;
    fireEvent.change(firstInput, { target: { value: 'Janet' } });
    fireEvent.change(lastInput, { target: { value: 'Smith' } });
    fireEvent.click(screen.getByRole('button', { name: /Save Changes/i }));
    await waitFor(() => expect(screen.getByText(/Profile updated/i)).toBeInTheDocument());
    const callInit = fetchSpy.mock.calls[0][1] as unknown as { body: string };
    const body = JSON.parse(callInit.body) as { name: { first: string; last: string } };
    expect(body.name.first).toBe('Janet');
    expect(body.name.last).toBe('Smith');
  });

  it('clears first and last name when emptied', async () => {
    const fetchSpy = vi.fn().mockResolvedValue({ ok: true, status: 200 });
    global.fetch = fetchSpy;
    render(<ProfileEditForm initial={{ first: 'Alice', last: 'Wonder' }} />);
    const firstInput = screen.getByLabelText(/First Name/i) as HTMLInputElement;
    const lastInput = screen.getByLabelText(/Last Name/i) as HTMLInputElement;
    fireEvent.change(firstInput, { target: { value: '' } });
    fireEvent.change(lastInput, { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: /Save Changes/i }));
    await waitFor(() => expect(screen.getByText(/Profile updated/i)).toBeInTheDocument());
    const callInit = fetchSpy.mock.calls[0][1] as unknown as { body: string };
    const body = JSON.parse(callInit.body) as { name: { first: null; last: null } };
    expect(body.name.first).toBeNull();
    expect(body.name.last).toBeNull();
  });

  it('clears only first name when first emptied and last changed', async () => {
    const fetchSpy = vi.fn().mockResolvedValue({ ok: true, status: 200 });
    global.fetch = fetchSpy;
    render(<ProfileEditForm initial={{ first: 'Alice', last: 'Wonder' }} />);
    const firstInput = screen.getByLabelText(/First Name/i) as HTMLInputElement;
    const lastInput = screen.getByLabelText(/Last Name/i) as HTMLInputElement;
    fireEvent.change(firstInput, { target: { value: '' } });
    fireEvent.change(lastInput, { target: { value: 'Wonderson' } });
    fireEvent.click(screen.getByRole('button', { name: /Save Changes/i }));
    await waitFor(() => expect(screen.getByText(/Profile updated/i)).toBeInTheDocument());
    const callInit = fetchSpy.mock.calls[0][1] as unknown as { body: string };
    const body = JSON.parse(callInit.body) as { name: { first: null; last: string } };
    expect(body.name.first).toBeNull();
    expect(body.name.last).toBe('Wonderson');
  });
});
