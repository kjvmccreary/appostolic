'use client';
import { useSession } from 'next-auth/react';
import { useEffect, useRef } from 'react';
import { useRouter } from 'next/navigation';

/**
 * TenantSessionHydrator
 * Ensures that after a tenant has been selected (cookie written server-side) but
 * before the next JWT refresh cycle, the client session reflects the selected tenant.
 * This avoids forcing the user to navigate or reload before gated nav appears.
 */
export default function TenantSessionHydrator(props: { tenant: string }) {
  const { data: session, status, update } = useSession();
  const router = useRouter();
  const didUpdateRef = useRef(false);
  useEffect(() => {
    if (didUpdateRef.current) return;
    if (status === 'authenticated' && props.tenant) {
      const current = (session as unknown as { tenant?: string } | null)?.tenant;
      if (!current) {
        didUpdateRef.current = true;
        // Align session tenant with cookie, then force a revalidation-driven refresh
        // so that the next server-render includes the TopBar without manual reload.
        update({ tenant: props.tenant }).finally(() => {
          // Small microtask delay to let next-auth context settle before refresh.
          setTimeout(() => {
            try {
              router.refresh();
            } catch {
              // no-op: refresh best-effort
            }
          }, 0);
        });
      }
    }
  }, [status, session, props.tenant, update, router]);
  return null;
}
