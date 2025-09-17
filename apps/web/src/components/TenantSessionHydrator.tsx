'use client';
import { useSession } from 'next-auth/react';
import { useEffect } from 'react';

/**
 * TenantSessionHydrator
 * Ensures that after a tenant has been selected (cookie written server-side) but
 * before the next JWT refresh cycle, the client session reflects the selected tenant.
 * This avoids forcing the user to navigate or reload before gated nav appears.
 */
export default function TenantSessionHydrator(props: { tenant: string }) {
  const { data: session, status, update } = useSession();
  useEffect(() => {
    if (status === 'authenticated' && props.tenant) {
      const current = (session as unknown as { tenant?: string } | null)?.tenant;
      if (!current) {
        update({ tenant: props.tenant });
      }
    }
  }, [status, session, props.tenant, update]);
  return null;
}
