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
      className="px-3 py-2 rounded bg-blue-600 text-white hover:bg-blue-700"
    >
      New Agent
    </Link>
  );
}
