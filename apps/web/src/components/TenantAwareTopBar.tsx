'use client';
import React from 'react';
import { useSession } from 'next-auth/react';
import { TopBar } from './TopBar';

/**
 * TenantAwareTopBar
 * Hides the TopBar navigation when:
 *  - User is authenticated with more than one tenant membership AND
 *  - No tenant has been selected yet (no selected_tenant cookie and no session.tenant) AND
 *  - Not currently on /select-tenant (selection screen itself)
 * Rationale: Prevent navigating app areas before establishing tenant context.
 */
export function TenantAwareTopBar() {
  const { data: session } = useSession();
  const anySession = session as unknown as {
    memberships?: { tenant: string }[];
    tenant?: string | null;
  } | null;
  const memberships = Array.isArray(anySession?.memberships) ? anySession!.memberships! : [];
  const hasMultiple = memberships.length > 1;
  // selected_tenant cookie not directly available in client without parsing document.cookie
  const selectedFromSession = anySession?.tenant || null;
  const hasSelection =
    !!selectedFromSession ||
    (typeof document !== 'undefined' && /(?:^|; )selected_tenant=/.test(document.cookie));
  const pathname = typeof window !== 'undefined' ? window.location.pathname : '';
  const onSelectTenantPage = pathname.startsWith('/select-tenant');

  if (hasMultiple && !hasSelection && !onSelectTenantPage) {
    return null; // suppress TopBar until tenant chosen
  }
  return <TopBar />;
}

export default TenantAwareTopBar;
