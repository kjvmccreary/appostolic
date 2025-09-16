'use client';

import Link from 'next/link';
import { useSession } from 'next-auth/react';

export function NewAgentButton() {
  const { data: session } = useSession();
  const canCreate = Boolean((session as unknown as { canCreate?: boolean } | null)?.canCreate);
  if (!canCreate) return null;
  return (
    <Link
      href="/studio/agents/new"
      className="px-3 py-1 rounded-md text-sm font-medium text-white bg-[var(--color-accent-600)] hover:brightness-110"
    >
      New Agent
    </Link>
  );
}
