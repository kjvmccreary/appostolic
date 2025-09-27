import { redirect } from 'next/navigation';
import { fetchFromProxy } from '../../../../app/lib/serverFetch';
import { GuardrailAdminClient } from './components/GuardrailAdminClient';
import type { TenantGuardrailSummary } from './types';

async function loadSummary(): Promise<TenantGuardrailSummary> {
  const res = await fetchFromProxy('/api-proxy/guardrails/tenant?policyKey=default');
  if (res.status === 401) {
    redirect('/select-tenant');
  }
  if (res.status === 403) {
    redirect('/studio');
  }
  if (!res.ok) {
    throw new Error(`Failed to load guardrail summary: ${res.status}`);
  }
  return (await res.json()) as TenantGuardrailSummary;
}

export default async function Page() {
  const summary = await loadSummary();
  return <GuardrailAdminClient summary={summary} />;
}
