'use client';

import * as React from 'react';
import { useSearchParams, useRouter } from 'next/navigation';
import { useToast } from './ui/Toaster';

/**
 * useInviteToasts reads ok/err params and shows contextual toasts.
 * After showing, it removes the params from the URL to prevent repeats on refresh.
 */
export function useInviteToasts() {
  const params = useSearchParams();
  const router = useRouter();
  const { showToast } = useToast();
  React.useEffect(() => {
    if (!params) return;
    const ok = params.get('ok');
    const err = params.get('err');
    if (!ok && !err) return;
    if (ok === 'invite-created')
      showToast({ kind: 'success', message: 'Invite created and email sent.' });
    else if (ok === 'invite-resent')
      showToast({ kind: 'success', message: 'Invite email resent.' });
    else if (ok === 'invite-revoked') showToast({ kind: 'success', message: 'Invite revoked.' });
    else if (err) showToast({ kind: 'error', message: 'Something went wrong. Please try again.' });

    // Strip ok/err from URL
    const url = new URL(window.location.href);
    url.searchParams.delete('ok');
    url.searchParams.delete('err');
    router.replace(url.pathname + (url.search ? '?' + url.searchParams.toString() : ''));
  }, [params, router, showToast]);
}
