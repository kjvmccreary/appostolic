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

  it('uploads valid image, replaces preview with cache-busted server URL, dispatches event, and invokes onUploaded', async () => {
    const onUploaded = vi.fn();
    const eventListener = vi.fn();
    window.addEventListener('avatar-updated', eventListener as EventListener);

    render(<AvatarUpload onUploaded={onUploaded} />);
    const input = screen.getByLabelText(/choose avatar image/i) as HTMLInputElement;
    const file = makeFile('a.png', 'image/png');

    const json = vi.fn().mockResolvedValue({ avatar: { url: '/media/users/u/avatar.png' } });
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json } as unknown as Response);

    fireEvent.change(input, { target: { files: [file] } });
    const uploadBtn = screen.getByRole('button', { name: /Upload/i });
    fireEvent.click(uploadBtn);

    await waitFor(() => expect(onUploaded).toHaveBeenCalled());
    expect(fetch).toHaveBeenCalled();

    // Preview replacement: find the img inside MUI Avatar root (it renders as <div><img/></div>)
    // We query by alt text.
    const img = screen.getByAltText(/avatar preview/i) as HTMLImageElement | null;
    expect(img).not.toBeNull();
    const src = img!.getAttribute('src') || '';
    expect(src).toMatch(/\/media\/users\/u\/avatar\.png\?v=\d+/);
    // Ensure event fired with cache-busted URL
    expect(eventListener).toHaveBeenCalledTimes(1);
    const call = eventListener.mock.calls[0][0] as CustomEvent;
    expect(call.detail.url).toEqual(src);
  });
});
