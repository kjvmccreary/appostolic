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
  const { data: session, status } = useSession();

  /**
   * SECURITY / UX NOTE
   * We intentionally default to "hidden" (return null) until the session is resolved so that
   * multi‑tenant users without a selection do NOT see a flash of the navigation bar during
   * the hydration window. (Previously the component rendered the TopBar on first paint when
   * session was still undefined, then removed it after hydration, allowing a brief period
   * where navigation links were interactable.)
   *
   * This errs on the side of hiding the nav for ALL users until the session loads. The
   * resulting delay is typically sub‑100ms in practice and acceptable for the stricter
   * gating requirement. If needed later we can introduce a server component wrapper that
   * provides a pre‑validated session snapshot to avoid the delay for single‑tenant users.
   */
  if (status === 'loading') return null; // session not yet known; hide defensively

  const anySession = session as unknown as {
    memberships?: { tenant: string }[];
    tenant?: string | null;
  } | null;
  const memberships = Array.isArray(anySession?.memberships) ? anySession!.memberships! : [];
  const hasMultiple = memberships.length > 1;

  // Determine if a tenant has already been selected either via session claim or cookie.
  const selectedFromSession = anySession?.tenant || null;
  const cookieHasSelection =
    typeof document !== 'undefined' && /(?:^|; )selected_tenant=/.test(document.cookie);
  const hasSelection = !!selectedFromSession || cookieHasSelection;

  const pathname = typeof window !== 'undefined' ? window.location.pathname : '';
  const onSelectTenantPage = pathname.startsWith('/select-tenant');

  if (hasMultiple && !hasSelection && !onSelectTenantPage) {
    return null; // suppress TopBar until tenant chosen
  }

  return <TopBar />;
}

export default TenantAwareTopBar;
