import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import React from 'react';
import { AvatarUpload } from './AvatarUpload';

describe('AvatarUpload', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  function makeFile(name: string, type: string, size = 10) {
    const blob = new Blob([new Uint8Array(size)], { type });
    return new File([blob], name, { type });
  }

  it('rejects disallowed mime types', () => {
    render(<AvatarUpload />);
    const input = screen.getByLabelText(/choose avatar image/i) as HTMLInputElement;
    const file = makeFile('x.gif', 'image/gif');
    fireEvent.change(input, { target: { files: [file] } });
    expect(screen.getByRole('alert')).toHaveTextContent(/only png, jpeg, or webp/i);
  });

  it('rejects oversize file', () => {
    render(<AvatarUpload />);
    const input = screen.getByLabelText(/choose avatar image/i) as HTMLInputElement;
    const file = makeFile('big.png', 'image/png', 2 * 1024 * 1024 + 1);
    fireEvent.change(input, { target: { files: [file] } });
    expect(screen.getByRole('alert')).toHaveTextContent(/too large/i);
  });

  it('uploads valid image and invokes onUploaded', async () => {
    const onUploaded = vi.fn();
    render(<AvatarUpload onUploaded={onUploaded} />);
    const input = screen.getByLabelText(/choose avatar image/i) as HTMLInputElement;
    const file = makeFile('a.png', 'image/png');

    const json = vi.fn().mockResolvedValue({ avatar: { url: '/media/users/u/avatar.png' } });
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json } as unknown as Response);
    Object.defineProperty(window, 'location', { value: { reload: vi.fn() }, writable: true });

    fireEvent.change(input, { target: { files: [file] } });
    // Component uses explicit Upload button (no wrapping form). Simulate clicking it.
    const uploadBtn = screen.getByRole('button', { name: /Upload/i });
    expect(uploadBtn).not.toBeDisabled();
    fireEvent.click(uploadBtn);

    await waitFor(() => expect(onUploaded).toHaveBeenCalled());
    expect(fetch).toHaveBeenCalled();
  });
});
