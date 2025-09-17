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
  const membershipCount = memberships.length;
  const hasMultiple = membershipCount > 1;

  // Determine if a tenant has already been selected either via session claim or cookie.
  const selectedFromSession = anySession?.tenant || null;
  const cookieHasSelection =
    typeof document !== 'undefined' && /(?:^|; )selected_tenant=/.test(document.cookie);
  const hasSelection = !!selectedFromSession || cookieHasSelection;

  const pathname = typeof window !== 'undefined' ? window.location.pathname : '';
  const onSelectTenantPage = pathname.startsWith('/select-tenant');

  // Determine if authenticated (email presence) — if not authed, allow existing TopBar logic (it renders minimal sign-in navigation)
  const isAuthed = Boolean(
    (anySession as unknown as { user?: { email?: string } } | null)?.user?.email,
  );

  /**
   * Revised gating rules (strict):
   * 1. While session loading => hidden (handled above).
   * 2. If authenticated and NO tenant selection yet (neither cookie nor session.tenant) => hide regardless of membership count
   *    Rationale: Even a single-tenant account should not expose tenant-scoped navigation until the server asserts the tenant context.
   * 3. Exception: allow visibility on the /select-tenant page itself so the layout can remain consistent if desired (but we still hide to reduce clutter – choose to keep hidden here).
   */
  if (isAuthed && !hasSelection) {
    return null; // enforce selection-first navigation gating
  }

  // Additional guard: multi-tenant & no selection already covered above, but keep logic explicit.
  if (hasMultiple && !hasSelection && !onSelectTenantPage) {
    return null;
  }

  return <TopBar />;
}

export default TenantAwareTopBar;
