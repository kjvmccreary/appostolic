import { redirect } from 'next/navigation';
import { fetchFromProxy } from '../../../../../app/lib/serverFetch';
import { GuardrailSuperAdminClient } from '../components/GuardrailSuperAdminClient';
import type { GuardrailSuperadminSummary } from '../types';

async function loadSummary(): Promise<GuardrailSuperadminSummary> {
  const res = await fetchFromProxy('/api-proxy/guardrails/super/state', {
    cache: 'no-store',
  });

  if (res.status === 401) {
    redirect('/login');
  }
  if (res.status === 403) {
    redirect('/studio');
  }
  if (!res.ok) {
    throw new Error(`Failed to load superadmin guardrail summary: ${res.status}`);
  }

  return (await res.json()) as GuardrailSuperadminSummary;
}

export default async function SuperadminGuardrailsPage() {
  const summary = await loadSummary();
  return <GuardrailSuperAdminClient summary={summary} />;
}
