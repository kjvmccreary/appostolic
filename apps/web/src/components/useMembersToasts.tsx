'use client';

import * as React from 'react';
import { useToastOptional } from './ui/Toaster';

export function useMembersToasts() {
  const toast = useToastOptional();
  React.useEffect(() => {
    if (!toast) return; // no provider in this render tree (e.g., certain tests)
    // Use window.location to avoid requiring Next app router context in tests
    const url = new URL(window.location.href);
    const ok = url.searchParams.get('ok');
    const err = url.searchParams.get('err');
    if (!ok && !err) return;
    if (ok === 'roles-saved') toast.showToast({ kind: 'success', message: 'Roles updated.' });
    else if (err) toast.showToast({ kind: 'error', message: 'Failed to update roles. Try again.' });
    url.searchParams.delete('ok');
    url.searchParams.delete('err');
    window.history.replaceState(null, '', url.pathname + (url.search ? '?' + url.search : ''));
  }, [toast]);
}
