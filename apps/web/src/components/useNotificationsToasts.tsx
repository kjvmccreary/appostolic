'use client';

// Notifications DLQ toast bridge: reads ok/err from URL and shows a toast,
// then cleans the URL to avoid duplicate toasts on back/forward.
import { useEffect } from 'react';
import { useToastOptional } from './ui/Toaster';

export function useNotificationsToasts() {
  const toast = useToastOptional();

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const url = new URL(window.location.href);
    const ok = url.searchParams.get('ok');
    const err = url.searchParams.get('err');
    if (!ok && !err) return;

    if (toast) {
      if (ok) toast.showToast({ message: ok, kind: 'success' });
      if (err) toast.showToast({ message: err, kind: 'error' });
    }

    // Clean the URL so re-renders/back do not re-trigger the toast
    url.searchParams.delete('ok');
    url.searchParams.delete('err');
    window.history.replaceState({}, '', url.toString());
  }, [toast]);
}
